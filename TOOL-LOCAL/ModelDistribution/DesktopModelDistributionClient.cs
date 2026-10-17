using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Updates;
using TOOL_LOCAL.Vietsub.Voice;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_LOCAL.ModelDistribution;

internal sealed class DesktopModelDistributionClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Uri _origin;
    private readonly ModelCatalogTrustPolicy _policy;
    private readonly string _cacheRoot;
    private readonly TimeProvider _clock;
    private readonly string _statePath;
    private readonly string _stateLockPath;
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private readonly string _desktopVersion;
    private readonly int _desktopBuild;
    public bool CanDownload { get; }

    internal DesktopModelDistributionClient(Uri origin, ModelCatalogTrustPolicy policy, string cacheRoot,
        HttpClient? http = null, TimeProvider? clock = null, string? desktopVersion = null, int? desktopBuild = null,
        bool downloadsEnabled = true)
    {
        if (origin.Scheme != Uri.UriSchemeHttps || origin.Port != 443 || !string.IsNullOrEmpty(origin.UserInfo) ||
            !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment))
            throw new InvalidDataException("Kho model phải dùng địa chỉ HTTPS/443 đã cấu hình.");
        _origin = new Uri(origin.AbsoluteUri.TrimEnd('/') + "/");
        _policy = policy;
        CanDownload = downloadsEnabled;
        _cacheRoot = Path.GetFullPath(cacheRoot);
        _clock = clock ?? TimeProvider.System;
        _desktopVersion = desktopVersion ?? DesktopBuildInfo.Version;
        _desktopBuild = desktopBuild ?? DesktopBuildInfo.BuildNumber;
        _http = http ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.None
        }) { Timeout = TimeSpan.FromMinutes(30) };
        _ownsHttp = http is null;
        var scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            policy.RepositoryId + "|" + policy.Channel + "|" + policy.Platform))).ToLowerInvariant();
        _statePath = Path.Combine(_cacheRoot, "trust", scope + ".json");
        _stateLockPath = _statePath + ".lock";
    }

    public bool IsKnownRevoked(string sha256)
    {
        var state = ReadState();
        return state?.RevokedArtifactSha256?.Contains(sha256, StringComparer.Ordinal) == true;
    }

    internal PiperOfflineBundle? GetCachedPiperBundle()
    {
        var definition = PiperOfflineBundle.ApprovedDefinition();
        if (IsKnownRevoked(definition.ArchiveSha256) || IsKnownRevoked(definition.ManifestSha256))
            throw new ModelDistributionException(ModelDistributionErrors.ArtifactNotApproved,
                "Gói giọng Việt này đã bị thu hồi.");
        var bundle = new PiperOfflineBundle(Path.Combine(_cacheRoot, "bundles", "piper", definition.BundleVersion), definition);
        return bundle.Available(out _) ? bundle : null;
    }

    internal async Task<PiperOfflineBundle> PreparePiperBundleAsync(IProgress<ModelDownloadProgress>? progress,
        CancellationToken token)
    {
        RequireDownloadsEnabled();
        var definition = PiperOfflineBundle.ApprovedDefinition();
        var root = Path.Combine(_cacheRoot, "bundles", "piper", definition.BundleVersion);
        SystemSetupPaths.EnsureSafeDirectory(Path.GetDirectoryName(root)!);
        var leasePath = root + ".prepare.lock";
        ResumableModelDownloader.AssertSafeFile(leasePath);
        FileStream lease;
        try { lease = new(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException)
        {
            throw new ModelDistributionException(ModelDistributionErrors.Busy,
                "Gói giọng Việt đang được chuẩn bị ở cửa sổ khác. Hãy đợi rồi thử lại.");
        }
        using var prepareLease = lease;
        RequirePiperNotRevoked(definition);
        var manifest = await GetArtifactAsync("piper-offline-manifest", progress, token);
        var zip = await GetArtifactAsync("piper-offline-archive", progress, token);
        RequirePiperNotRevoked(definition);
        SystemSetupPaths.EnsureSafeDirectory(root);
        foreach (var item in new[] { (Source: manifest, Name: "manifest.json"), (Source: zip, Name: "piper-offline.zip") })
        {
            RequirePiperNotRevoked(definition);
            var path = Path.Combine(root, item.Name);
            ResumableModelDownloader.AssertSafeFile(path);
            ResumableModelDownloader.AssertSafeFile(path + ".tmp");
            File.Copy(item.Source, path + ".tmp", true);
            token.ThrowIfCancellationRequested();
            RequirePiperNotRevoked(definition);
            File.Move(path + ".tmp", path, true);
        }
        var bundle = new PiperOfflineBundle(root, definition);
        bundle.VerifyArchive(force: true);
        RequirePiperNotRevoked(definition);
        return bundle;
    }

    public async Task<string> GetArtifactAsync(string artifactId, IProgress<ModelDownloadProgress>? progress,
        CancellationToken token)
    {
        var catalog = await RefreshCatalogAsync(token);
        var artifact = catalog.Catalog.Artifacts.SingleOrDefault(item => item.ArtifactId == artifactId)
            ?? throw new ModelDistributionException(ModelDistributionErrors.ArtifactNotFound,
                "Model này không được cung cấp trong danh mục đã xác thực.");
        EnsureCompatible(artifact);
        if (IsKnownRevoked(artifact.Sha256))
            throw new ModelDistributionException(ModelDistributionErrors.ArtifactNotApproved,
                "Phiên bản model đã bị thu hồi.");
        var destination = Path.Combine(_cacheRoot, "artifacts", artifact.Sha256, artifact.FileName);
        try
        {
            var result = await new ResumableModelDownloader(_http, _origin, _policy.Channel)
                .DownloadAsync(artifact, destination, progress, token);
            if (IsKnownRevoked(artifact.Sha256))
                throw new ModelDistributionException(ModelDistributionErrors.ArtifactNotApproved,
                    "Phiên bản model đã bị thu hồi trong khi tải.");
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (ModelDistributionException) { throw; }
        catch (InvalidDataException)
        {
            throw new ModelDistributionException(ModelDistributionErrors.ArtifactInvalid,
                "File model không hợp lệ; chưa được đưa vào sử dụng.");
        }
        catch (IOException)
        {
            throw new ModelDistributionException(ModelDistributionErrors.DownloadFailed,
                "Chưa hoàn tất tải model. Hãy kiểm tra dung lượng, quyền ghi và thử lại.");
        }
        catch (HttpRequestException)
        {
            throw new ModelDistributionException(ModelDistributionErrors.DownloadFailed,
                "Không thể kết nối kho model. Có thể thử lại để tiếp tục lượt tải.");
        }
    }

    internal async Task<VerifiedModelCatalog> RefreshCatalogAsync(CancellationToken token)
    {
        RequireDownloadsEnabled();
        await _catalogGate.WaitAsync(token);
        try
        {
            SystemSetupPaths.EnsureSafeDirectory(Path.GetDirectoryName(_statePath)!);
            ResumableModelDownloader.AssertSafeFile(_stateLockPath);
            using var lease = new FileStream(_stateLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var previous = ReadState();
            var uri = new Uri(_origin, $"api/model-distribution/{Uri.EscapeDataString(_policy.Channel)}/catalog");
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is { } actual && actual != uri)
                throw new ModelDistributionException(ModelDistributionErrors.CatalogInvalid,
                    "Kho model trả chuyển hướng không hợp lệ.");
            if (response.Content.Headers.ContentLength > ModelCatalogVerifier.MaximumEnvelopeBytes)
                throw new InvalidDataException("Danh mục model vượt giới hạn.");
            await using var input = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            var block = new byte[8192];
            while (true)
            {
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                readTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                var count = await input.ReadAsync(block, readTimeout.Token);
                if (count == 0) break;
                if (buffer.Length + count > ModelCatalogVerifier.MaximumEnvelopeBytes)
                    throw new InvalidDataException("Danh mục model vượt giới hạn.");
                buffer.Write(block, 0, count);
            }
            var verified = ModelCatalogVerifier.Verify(buffer.ToArray(), _policy, previous, _clock.GetUtcNow());
            ResumableModelDownloader.AssertSafeFile(_statePath);
            ResumableModelDownloader.AssertSafeFile(_statePath + ".tmp");
            await File.WriteAllBytesAsync(_statePath + ".tmp", JsonSerializer.SerializeToUtf8Bytes(verified.NextState, Json), token);
            File.Move(_statePath + ".tmp", _statePath, true);
            return verified;
        }
        finally { _catalogGate.Release(); }
    }

    private ModelCatalogTrustState? ReadState()
    {
        ResumableModelDownloader.AssertSafeFile(_statePath);
        if (!File.Exists(_statePath)) return null;
        if (new FileInfo(_statePath).Length > 32 * 1024) throw new InvalidDataException("Trạng thái tin cậy model không hợp lệ.");
        try
        {
            var state = JsonSerializer.Deserialize<ModelCatalogTrustState>(File.ReadAllBytes(_statePath), Json)
                ?? throw new InvalidDataException("Trạng thái tin cậy model không hợp lệ.");
            ModelCatalogVerifier.ValidateTrustState(state, _policy);
            return state;
        }
        catch (JsonException) { throw new InvalidDataException("Trạng thái tin cậy model không hợp lệ."); }
        catch (ModelDistributionException) { throw new InvalidDataException("Trạng thái tin cậy model không hợp lệ."); }
    }

    private void RequireDownloadsEnabled()
    {
        if (!CanDownload)
            throw new ModelDistributionException(ModelDistributionErrors.Disabled,
                "Kênh tải model đang tắt. Bản offline tiếp tục dùng file đã được xác minh.");
    }

    private void RequirePiperNotRevoked(PiperOfflineDefinition definition)
    {
        if (IsKnownRevoked(definition.ArchiveSha256) || IsKnownRevoked(definition.ManifestSha256))
            throw new ModelDistributionException(ModelDistributionErrors.ArtifactNotApproved,
                "Gói giọng Việt này đã bị thu hồi.");
    }

    private void EnsureCompatible(ModelDistributionArtifact artifact)
    {
        var current = _desktopVersion.Split('-', 2)[0];
        var minimum = artifact.MinimumDesktopVersion.Split('-', 2)[0];
        if (!Version.TryParse(current, out var currentVersion) || !Version.TryParse(minimum, out var required) ||
            currentVersion < required || _desktopBuild < artifact.MinimumDesktopBuildNumber ||
            (artifact.RequiredWorkerVersion is { } worker &&
                worker != TOOL_LOCAL.Vietsub.Translation.VietsubTranslationWorkerProtocol.WorkerVersion) ||
            (artifact.RequiredWorkerProtocolVersion is { } protocol &&
                protocol != TOOL_LOCAL.Vietsub.Translation.VietsubTranslationWorkerProtocol.Version))
            throw new ModelDistributionException(ModelDistributionErrors.Incompatible,
                "Model yêu cầu phiên bản ứng dụng hoặc worker khác. Hãy cập nhật ứng dụng.");
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
        _catalogGate.Dispose();
    }
}

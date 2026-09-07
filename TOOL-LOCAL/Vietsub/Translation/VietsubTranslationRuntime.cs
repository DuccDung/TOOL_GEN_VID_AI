using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace TOOL_LOCAL.Vietsub.Translation;

internal sealed record VietsubTranslationComponentDefinition(
    string ComponentId,
    string EngineId,
    string EngineVersion,
    string Architecture,
    string ModelFileName,
    long ModelSizeBytes,
    string ModelSha256,
    Uri DownloadUri,
    string SourceRepository,
    string SourceRevision,
    string LicenseExpression,
    long MinimumRamBytes,
    long MinimumFreeDiskBytes);

internal static class VietsubTranslationApprovedComponents
{
    public static readonly VietsubTranslationComponentDefinition Qwen3_4B_Q4Km = new(
        ComponentId: "qwen3-4b-q4-k-m-cpu",
        EngineId: "qwen3-4b-q4-k-m-cpu",
        EngineVersion: "bc64014-llamasharp-0.27.0-adapter-1",
        Architecture: "win-x64-cpu",
        ModelFileName: "Qwen3-4B-Q4_K_M.gguf",
        ModelSizeBytes: 2_497_280_256,
        ModelSha256: "7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5",
        DownloadUri: new Uri(
            "https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/bc640142c66e1fdd12af0bd68f40445458f3869b/Qwen3-4B-Q4_K_M.gguf?download=true"),
        SourceRepository: "Qwen/Qwen3-4B-GGUF",
        SourceRevision: "bc640142c66e1fdd12af0bd68f40445458f3869b",
        LicenseExpression: "Apache-2.0",
        MinimumRamBytes: 6L * 1024 * 1024 * 1024,
        MinimumFreeDiskBytes: 2_497_280_256 + 768L * 1024 * 1024);
}

internal sealed record VietsubTranslationRuntimeInstallProgress(
    string Stage,
    double Percent,
    string Message,
    long BytesProcessed,
    long TotalBytes);

internal interface IVietsubManagedLocalTranslationProvider : IVietsubLocalTranslationProvider
{
    string RuntimeProfileId { get; }

    bool LowMemoryMode { get; }

    bool TrySelectRuntimeProfile(string profileId);

    VietsubTranslationRuntimeStatus GetRuntimeStatus(bool selectLowerMemoryProfile = true);

    Task InstallAsync(
        IProgress<VietsubTranslationRuntimeInstallProgress>? progress,
        CancellationToken cancellationToken,
        bool resourceWarningAccepted = false);
}

internal sealed record VietsubTranslationRuntimeInspection(
    bool ModelPresent,
    bool ModelVerified,
    bool ProbeVerified,
    string? ErrorCode,
    string Message);

internal sealed record VietsubTranslationProbeEvidence(
    string WorkerVersion,
    int ProtocolVersion,
    string WorkerBinaryFingerprint,
    string BackendIdentity,
    string AvxLevel,
    string NativeLibraryHash,
    string ConfigFingerprint);

internal sealed class VietsubTranslationComponentStore : IDisposable
{
    private const int BufferSize = 1024 * 1024;
    private const int MaximumRedirects = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _verificationLock = new();
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly VietsubTranslationComponentDefinition _component;
    private readonly IReadOnlyList<string> _approvedLocalModelCandidates;
    private readonly IVietsubTranslationMemoryProbe _memoryProbe;
    private readonly VietsubTranslationResourceRequirements _resourceRequirements;
    private VerifiedFileStamp? _verifiedStamp;
    private int _installing;
    private bool _disposed;

    public VietsubTranslationComponentStore(
        VietsubTranslationComponentDefinition component,
        string? componentsRoot = null,
        HttpClient? httpClient = null,
        IEnumerable<string>? approvedLocalModelCandidates = null,
        IVietsubTranslationMemoryProbe? memoryProbe = null,
        VietsubTranslationResourceRequirements? resourceRequirements = null)
    {
        _component = component;
        ComponentsRoot = componentsRoot ?? ResolveDefaultComponentsRoot(component);
        ComponentDirectory = Path.Combine(ComponentsRoot, component.ComponentId, component.EngineVersion);
        ModelPath = Path.Combine(ComponentDirectory, component.ModelFileName);
        ProbeMarkerPath = Path.Combine(ComponentDirectory, "probe.json");
        _approvedLocalModelCandidates = (approvedLocalModelCandidates
                ?? ResolveApprovedLocalModelCandidates(component))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(path => !PathEquals(path, ModelPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _ownsHttpClient = httpClient is null;
        _memoryProbe = memoryProbe ?? new WindowsVietsubTranslationMemoryProbe();
        var baseline = resourceRequirements
            ?? (string.Equals(component.ComponentId, VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km.ComponentId, StringComparison.Ordinal)
                && string.Equals(component.EngineVersion, VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km.EngineVersion, StringComparison.Ordinal)
                    ? VietsubTranslationResourceRequirements.SafeCpuBaseline
                    : new VietsubTranslationResourceRequirements(
                        checked((ulong)Math.Max(0, component.MinimumRamBytes)),
                        0,
                        0));
        _resourceRequirements = baseline with
        {
            MinimumTotalPhysicalBytes = Math.Max(
                baseline.MinimumTotalPhysicalBytes,
                checked((ulong)Math.Max(0, component.MinimumRamBytes)))
        };
    }

    public string ComponentsRoot { get; }

    public string ComponentDirectory { get; }

    public string ModelPath { get; }

    public string ProbeMarkerPath { get; }

    public bool IsInstalling => Volatile.Read(ref _installing) == 1;

    public VietsubTranslationRuntimeInspection Inspect(
        bool requireProbe,
        bool checkResources = true,
        VietsubTranslationWorkerRuntimeProfile? runtimeProfile = null)
    {
        ThrowIfDisposed();
        if (!Environment.Is64BitProcess || !OperatingSystem.IsWindows())
        {
            return new VietsubTranslationRuntimeInspection(
                false,
                false,
                false,
                VietsubTranslationErrorCodes.RuntimeUnsupportedPlatform,
                "Engine dịch local hiện chỉ hỗ trợ Windows 64-bit.");
        }

        if (!File.Exists(ModelPath))
        {
            return new VietsubTranslationRuntimeInspection(
                false,
                false,
                false,
                VietsubTranslationErrorCodes.RuntimeNotInstalled,
                "Chưa cài model dịch local Qwen3 4B (2,50 GB).");
        }

        FileInfo model;
        try
        {
            model = new FileInfo(ModelPath);
            if (model.Length != _component.ModelSizeBytes)
            {
                return Invalid("Model dịch local sai kích thước; hãy bấm Sửa engine.");
            }

            lock (_verificationLock)
            {
                if (_verifiedStamp is null
                    || _verifiedStamp.Length != model.Length
                    || _verifiedStamp.LastWriteUtcTicks != model.LastWriteTimeUtc.Ticks)
                {
                    using var stream = new FileStream(
                        ModelPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        BufferSize,
                        FileOptions.SequentialScan);
                    var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                    if (!string.Equals(sha256, _component.ModelSha256, StringComparison.Ordinal))
                    {
                        _verifiedStamp = null;
                        return Invalid("Model dịch local sai SHA-256; hãy bấm Sửa engine.");
                    }

                    _verifiedStamp = new VerifiedFileStamp(model.Length, model.LastWriteTimeUtc.Ticks);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Invalid("Không thể kiểm tra model dịch local; hãy đóng tác vụ đang dùng model rồi thử lại.");
        }

        if (checkResources)
        {
            var resources = EvaluateResources(runtimeProfile?.ResourceRequirements);
            if (resources.IsBlocking)
            {
                return new VietsubTranslationRuntimeInspection(
                    true,
                    true,
                    false,
                    VietsubTranslationErrorCodes.RuntimeUnsupportedPlatform,
                    resources.Message);
            }
        }

        var probeVerified = IsProbeMarkerValid(runtimeProfile);
        if (requireProbe && !probeVerified)
        {
            return new VietsubTranslationRuntimeInspection(
                true,
                true,
                false,
                VietsubTranslationErrorCodes.ModelNotReady,
                "Model đã tải nhưng chưa vượt qua probe Anh/Trung → Việt; hãy bấm Sửa engine.");
        }

        return new VietsubTranslationRuntimeInspection(
            true,
            true,
            probeVerified,
            null,
            "Qwen3 4B local đã kiểm tra model và sẵn sàng dịch theo scene.");
    }

    public async Task InstallModelAsync(
        IProgress<VietsubTranslationRuntimeInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _installing, 1) == 1)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.JobConflict,
                "Engine dịch local đang được cài đặt.");
        }

        try
        {
            var current = Inspect(requireProbe: false, checkResources: false);
            if (current.ModelVerified)
            {
                progress?.Report(new VietsubTranslationRuntimeInstallProgress(
                    "VERIFYING",
                    85,
                    "Model đã đúng SHA-256; đang chạy probe dịch thật.",
                    _component.ModelSizeBytes,
                    _component.ModelSizeBytes));
                return;
            }

            Directory.CreateDirectory(ComponentDirectory);
            var partialPath = ModelPath + ".partial";
            TryDelete(partialPath);
            TryDelete(ProbeMarkerPath);
            if (await TryReuseApprovedLocalModelAsync(partialPath, progress, cancellationToken))
            {
                return;
            }

            EnsureDiskCapacity();
            progress?.Report(new VietsubTranslationRuntimeInstallProgress(
                "DOWNLOADING",
                0,
                "Đang tải model dịch local Qwen3 4B (2,50 GB).",
                0,
                _component.ModelSizeBytes));

            try
            {
                await DownloadAndVerifyAsync(partialPath, progress, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(partialPath, ModelPath, overwrite: true);
                lock (_verificationLock)
                {
                    var info = new FileInfo(ModelPath);
                    _verifiedStamp = new VerifiedFileStamp(info.Length, info.LastWriteTimeUtc.Ticks);
                }
            }
            finally
            {
                TryDelete(partialPath);
            }
        }
        finally
        {
            Volatile.Write(ref _installing, 0);
        }
    }

    private async Task<bool> TryReuseApprovedLocalModelAsync(
        string partialPath,
        IProgress<VietsubTranslationRuntimeInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        foreach (var candidatePath in _approvedLocalModelCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            progress?.Report(new VietsubTranslationRuntimeInstallProgress(
                "REUSING_LOCAL",
                5,
                "Đã tìm thấy model local đúng phiên bản; đang kiểm tra SHA-256.",
                0,
                _component.ModelSizeBytes));
            TryDelete(partialPath);
            var linked = TryCreateHardLink(partialPath, candidatePath);
            var verified = linked
                ? await HasApprovedModelContentAsync(partialPath, cancellationToken)
                : await TryCopyAndVerifyApprovedModelAsync(
                    candidatePath,
                    partialPath,
                    progress,
                    cancellationToken);
            if (!verified)
            {
                TryDelete(partialPath);
                continue;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(partialPath, ModelPath, overwrite: true);
                var info = new FileInfo(ModelPath);
                lock (_verificationLock)
                {
                    _verifiedStamp = new VerifiedFileStamp(info.Length, info.LastWriteTimeUtc.Ticks);
                }

                progress?.Report(new VietsubTranslationRuntimeInstallProgress(
                    "VERIFYING",
                    85,
                    linked
                        ? "Đã tái sử dụng model local bằng hard link và xác minh SHA-256."
                        : "Đã sao chép model local và xác minh SHA-256.",
                    _component.ModelSizeBytes,
                    _component.ModelSizeBytes));
                return true;
            }
            finally
            {
                TryDelete(partialPath);
            }
        }

        return false;
    }

    private async Task<bool> HasApprovedModelContentAsync(
        string candidatePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                candidatePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length != _component.ModelSizeBytes)
            {
                return false;
            }

            var actualHash = await SHA256.HashDataAsync(stream, cancellationToken);
            return string.Equals(
                Convert.ToHexString(actualHash),
                _component.ModelSha256,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<bool> TryCopyAndVerifyApprovedModelAsync(
        string sourcePath,
        string partialPath,
        IProgress<VietsubTranslationRuntimeInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            EnsureDiskCapacity();
            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var destination = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            long total = 0;
            var stopwatch = Stopwatch.StartNew();
            var lastReport = TimeSpan.Zero;
            try
            {
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > _component.ModelSizeBytes)
                    {
                        return false;
                    }

                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    if (stopwatch.Elapsed - lastReport >= TimeSpan.FromMilliseconds(350))
                    {
                        lastReport = stopwatch.Elapsed;
                        progress?.Report(new VietsubTranslationRuntimeInstallProgress(
                            "REUSING_LOCAL",
                            5 + Math.Min(75, total * 75d / _component.ModelSizeBytes),
                            "Đang sao chép model local đã được duyệt.",
                            total,
                            _component.ModelSizeBytes));
                    }
                }

                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
                var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                return total == _component.ModelSizeBytes
                    && string.Equals(actualHash, _component.ModelSha256, StringComparison.Ordinal);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (VietsubTranslationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void MarkProbeVerified(
        VietsubTranslationProbeEvidence evidence,
        VietsubTranslationWorkerRuntimeProfile? runtimeProfile = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(evidence);
        var inspection = Inspect(requireProbe: false, checkResources: false);
        if (!inspection.ModelVerified)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.RuntimeInvalid,
                inspection.Message);
        }

        Directory.CreateDirectory(ComponentDirectory);
        runtimeProfile ??= VietsubTranslationWorkerProfiles.CreateStandardCpuRuntimeProfile(
            Environment.ProcessorCount);
        if (!IsExpectedProbeEvidence(evidence, runtimeProfile))
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.RuntimeInvalid,
                "Không thể ghi marker vì worker/backend/config không khớp bản đã pin.");
        }

        var marker = new ProbeMarker(
            3,
            _component.EngineId,
            _component.EngineVersion,
            _component.ModelSha256,
            runtimeProfile.ProfileId,
            runtimeProfile.ResourcePolicyVersion,
            evidence.WorkerVersion,
            evidence.ProtocolVersion,
            evidence.WorkerBinaryFingerprint,
            evidence.BackendIdentity,
            evidence.AvxLevel,
            evidence.NativeLibraryHash,
            evidence.ConfigFingerprint,
            DateTime.UtcNow);
        var temporaryPath = ProbeMarkerPath + ".partial";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(marker, JsonOptions));
        File.Move(temporaryPath, ProbeMarkerPath, overwrite: true);
    }

    public void InvalidateProbe() => TryDelete(ProbeMarkerPath);

    public VietsubTranslationResourceEvaluation EvaluateResources(
        VietsubTranslationResourceRequirements? requirements = null) =>
        VietsubTranslationResourceGate.Evaluate(_memoryProbe, requirements ?? _resourceRequirements);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task DownloadAndVerifyAsync(
        string partialPath,
        IProgress<VietsubTranslationRuntimeInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithRestrictedRedirectsAsync(_component.DownloadUri, cancellationToken);
        if (response.Content.Headers.ContentLength is long contentLength
            && contentLength != _component.ModelSizeBytes)
        {
            throw DownloadFailed("Nguồn model trả về kích thước không khớp allowlist.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            partialPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long total = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > _component.ModelSizeBytes)
                {
                    throw DownloadFailed("Nguồn model trả về dữ liệu vượt kích thước allowlist.");
                }

                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                if (stopwatch.Elapsed - lastReport >= TimeSpan.FromMilliseconds(350))
                {
                    lastReport = stopwatch.Elapsed;
                    progress?.Report(new VietsubTranslationRuntimeInstallProgress(
                        "DOWNLOADING",
                        Math.Min(80, total * 80d / _component.ModelSizeBytes),
                        "Đang tải và kiểm tra model dịch local.",
                        total,
                        _component.ModelSizeBytes));
                }
            }

            await destination.FlushAsync(cancellationToken);
            if (total != _component.ModelSizeBytes)
            {
                throw DownloadFailed("Model tải về chưa đủ dữ liệu.");
            }

            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actualHash, _component.ModelSha256, StringComparison.Ordinal))
            {
                throw DownloadFailed("Model tải về không khớp SHA-256 allowlist.");
            }

            progress?.Report(new VietsubTranslationRuntimeInstallProgress(
                "VERIFYING",
                85,
                "Đã xác minh kích thước và SHA-256 của model.",
                total,
                _component.ModelSizeBytes));
        }
        catch (VietsubTranslationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.RuntimeDownloadFailed,
                "Không thể tải component dịch local. Hãy kiểm tra kết nối và dung lượng đĩa rồi thử lại.",
                retryable: true,
                innerException: exception);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<HttpResponseMessage> SendWithRestrictedRedirectsAsync(
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        var current = initialUri;
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            EnsureAllowedDownloadUri(current);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("VideoMaker", "1.0"));
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode is HttpStatusCode.MovedPermanently
                or HttpStatusCode.Redirect
                or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null)
                {
                    throw DownloadFailed("Nguồn model chuyển hướng nhưng không cung cấp đích hợp lệ.");
                }

                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw DownloadFailed("Nguồn model tạm thời không khả dụng.");
            }

            return response;
        }

        throw DownloadFailed("Nguồn model chuyển hướng quá số lần cho phép.");
    }

    private void EnsureDiskCapacity()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(ComponentsRoot));
            if (string.IsNullOrWhiteSpace(root)
                || new DriveInfo(root).AvailableFreeSpace < _component.MinimumFreeDiskBytes)
            {
                throw new VietsubTranslationException(
                    VietsubTranslationErrorCodes.RuntimeInsufficientDisk,
                    "Không đủ dung lượng trống để tải và xác minh model dịch local 2,50 GB.");
            }
        }
        catch (VietsubTranslationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.RuntimeInsufficientDisk,
                "Không thể kiểm tra dung lượng đĩa cho component dịch local.",
                innerException: exception);
        }
    }

    internal static IReadOnlyList<string> ResolveApprovedLocalModelCandidates(
        VietsubTranslationComponentDefinition component,
        IEnumerable<string>? anchors = null)
    {
        var relativePath = Path.Combine(
            "third_party",
            "translation",
            "runtime-test",
            component.ComponentId,
            component.EngineVersion,
            component.ModelFileName);
        var candidates = new List<string>();
        foreach (var anchor in anchors ?? [AppContext.BaseDirectory, Environment.CurrentDirectory])
        {
            DirectoryInfo? directory;
            try
            {
                directory = new DirectoryInfo(Path.GetFullPath(anchor));
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                continue;
            }

            for (var depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
            {
                candidates.Add(Path.Combine(directory.FullName, relativePath));
            }
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool TryCreateHardLink(string linkPath, string existingPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return CreateHardLinkWindows(linkPath, existingPath, IntPtr.Zero);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static string ResolveDefaultComponentsRoot(
        VietsubTranslationComponentDefinition component)
    {
        var localAppDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolGenPostVideo",
            "components",
            "vietsub-translation");
        var candidates = new List<string> { localAppDataRoot };
        try
        {
            candidates.AddRange(DriveInfo.GetDrives()
                .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
                .OrderByDescending(drive => drive.AvailableFreeSpace)
                .Select(drive => Path.Combine(
                    drive.RootDirectory.FullName,
                    "VideoMakerData",
                    "components",
                    "vietsub-translation")));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var installedModel = Path.Combine(
                candidate,
                component.ComponentId,
                component.EngineVersion,
                component.ModelFileName);
            if (File.Exists(installedModel))
            {
                return candidate;
            }
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(candidate));
                if (!string.IsNullOrWhiteSpace(root)
                    && new DriveInfo(root).AvailableFreeSpace >= component.MinimumFreeDiskBytes)
                {
                    return candidate;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return localAppDataRoot;
    }

    private bool IsProbeMarkerValid(VietsubTranslationWorkerRuntimeProfile? runtimeProfile)
    {
        try
        {
            if (!File.Exists(ProbeMarkerPath))
            {
                return false;
            }

            runtimeProfile ??= VietsubTranslationWorkerProfiles.CreateStandardCpuRuntimeProfile(
                Environment.ProcessorCount);
            var marker = JsonSerializer.Deserialize<ProbeMarker>(File.ReadAllText(ProbeMarkerPath), JsonOptions);
            return marker is not null
                && marker.SchemaVersion == 3
                && string.Equals(marker.EngineId, _component.EngineId, StringComparison.Ordinal)
                && string.Equals(marker.EngineVersion, _component.EngineVersion, StringComparison.Ordinal)
                && string.Equals(marker.ModelSha256, _component.ModelSha256, StringComparison.Ordinal)
                && string.Equals(marker.RuntimeProfileId, runtimeProfile.ProfileId, StringComparison.Ordinal)
                && marker.ResourcePolicyVersion == runtimeProfile.ResourcePolicyVersion
                && IsExpectedProbeEvidence(new VietsubTranslationProbeEvidence(
                    marker.WorkerVersion,
                    marker.ProtocolVersion,
                    marker.WorkerBinaryFingerprint,
                    marker.BackendIdentity,
                    marker.AvxLevel,
                    marker.NativeLibraryHash,
                    marker.ConfigFingerprint), runtimeProfile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static bool IsExpectedProbeEvidence(
        VietsubTranslationProbeEvidence evidence,
        VietsubTranslationWorkerRuntimeProfile runtimeProfile)
    {
        var workerDirectory = Path.Combine(AppContext.BaseDirectory, "_translation_worker");
        var expectedWorkerFingerprint = VietsubTranslationWorkerProtocol.ComputeWorkerBinaryFingerprint(workerDirectory);
        var expectedConfigFingerprint = VietsubTranslationWorkerProtocol.ComputeConfigFingerprint(
            runtimeProfile.InferenceConfig);
        var avxDirectory = VietsubTranslationWorkerProfiles.SelectAvxName();
        var expectedAvxLevel = avxDirectory switch
        {
            "avx2" => "Avx2",
            "avx" => "Avx",
            _ => "None"
        };
        var nativePath = Path.Combine(
            workerDirectory,
            "runtimes",
            "win-x64",
            "native",
            avxDirectory,
            "llama.dll");
        var expectedNativeHash = string.Empty;
        if (File.Exists(nativePath))
        {
            using var stream = new FileStream(nativePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            expectedNativeHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        return string.Equals(evidence.WorkerVersion, VietsubTranslationWorkerProtocol.WorkerVersion, StringComparison.Ordinal)
            && evidence.ProtocolVersion == VietsubTranslationWorkerProtocol.Version
            && !string.IsNullOrWhiteSpace(expectedWorkerFingerprint)
            && string.Equals(evidence.WorkerBinaryFingerprint, expectedWorkerFingerprint, StringComparison.Ordinal)
            && string.Equals(evidence.BackendIdentity, $"cpu-{expectedAvxLevel.ToLowerInvariant()}", StringComparison.Ordinal)
            && string.Equals(evidence.AvxLevel, expectedAvxLevel, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(expectedNativeHash)
            && string.Equals(evidence.NativeLibraryHash, expectedNativeHash, StringComparison.Ordinal)
            && string.Equals(evidence.ConfigFingerprint, expectedConfigFingerprint, StringComparison.Ordinal);
    }

    private static void EnsureAllowedDownloadUri(Uri uri)
    {
        var host = uri.IdnHost;
        if (!uri.IsAbsoluteUri
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Port != 443
            || !(string.Equals(host, "huggingface.co", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".cdn.hf.co", StringComparison.OrdinalIgnoreCase)))
        {
            throw DownloadFailed("Nguồn model nằm ngoài allowlist HTTPS đã duyệt.");
        }
    }

    private static VietsubTranslationRuntimeInspection Invalid(string message) => new(
        true,
        false,
        false,
        VietsubTranslationErrorCodes.RuntimeInvalid,
        message);

    private static VietsubTranslationException DownloadFailed(string message) => new(
        VietsubTranslationErrorCodes.RuntimeDownloadFailed,
        message,
        retryable: true);

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWindows(
        string fileName,
        string existingFileName,
        IntPtr securityAttributes);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record VerifiedFileStamp(long Length, long LastWriteUtcTicks);

    private sealed record ProbeMarker(
        int SchemaVersion,
        string EngineId,
        string EngineVersion,
        string ModelSha256,
        string RuntimeProfileId,
        int ResourcePolicyVersion,
        string WorkerVersion,
        int ProtocolVersion,
        string WorkerBinaryFingerprint,
        string BackendIdentity,
        string AvxLevel,
        string NativeLibraryHash,
        string ConfigFingerprint,
        DateTime ProbedAtUtc);
}

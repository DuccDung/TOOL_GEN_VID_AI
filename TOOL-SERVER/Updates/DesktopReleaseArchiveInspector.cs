using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Configuration;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_SERVER.Updates;

public sealed record DesktopReleaseInspectionRequest(Guid PreparationId, int Generation, string DownloadUrl, string ExpectedVersion);
public sealed record DesktopReleaseInspectionProgress(string Stage, long BytesReceived, long? TotalBytes);
public sealed record DesktopReleaseInspectionResult(string FileName, long SizeBytes, string Sha256,
    DesktopUpdateManifest PackageManifest, long ExpandedSizeBytes, int EntryCount,
    string ModelBundleId, string MinimumBootstrapperVersion, DesktopPackageDistributionProfile DistributionProfile,
    DesktopPackageMetadataDocument? PackageMetadata = null);

public interface IDesktopReleaseArchiveInspector
{
    Task<DesktopReleaseInspectionResult> InspectAsync(DesktopReleaseInspectionRequest request,
        IProgress<DesktopReleaseInspectionProgress>? progress, CancellationToken cancellationToken);
}

public sealed class DesktopReleaseInspectionException(string code, string safeMessage) : Exception(safeMessage)
{
    public string Code { get; } = code;
    public string SafeMessage { get; } = safeMessage;
}

/// <summary>Reads native version resources without loading or executing an uploaded application.</summary>
public static partial class DesktopExecutableMetadata
{
    public const string BootstrapperPrefix = "VideoMaker.SharedBootstrapper.v1:";

    public static DesktopBootstrapperBindingInfo ReadBootstrapperBinding(string executablePath)
    {
        AssertPe(executablePath);
        var comments = FileVersionInfo.GetVersionInfo(executablePath).Comments;
        if (comments is null || comments.Length > 32_768 || !comments.StartsWith(BootstrapperPrefix, StringComparison.Ordinal))
            throw new InvalidDataException("Setup thiếu metadata bộ cài dùng chung.");
        try
        {
            var bytes = Convert.FromBase64String(comments[BootstrapperPrefix.Length..]);
            return JsonSerializer.Deserialize<DesktopBootstrapperBindingInfo>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                { PropertyNameCaseInsensitive = false }) ?? throw new InvalidDataException("Metadata Setup không hợp lệ.");
        }
        catch (Exception error) when (error is FormatException or JsonException)
        { throw new InvalidDataException("Metadata Setup không hợp lệ."); }
    }

    public static (string Version, int BuildNumber) ReadApplicationVersion(string executablePath)
    {
        AssertPe(executablePath);
        var value = FileVersionInfo.GetVersionInfo(executablePath).ProductVersion;
        var match = ApplicationVersion().Match(value ?? "");
        if (!match.Success || !int.TryParse(match.Groups["build"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var build)
            || build <= 0 || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(match.Groups["version"].Value))
            throw new InvalidDataException("Binary ứng dụng thiếu version/build phát hành hợp lệ.");
        return (match.Groups["version"].Value, build);
    }

    private static void AssertPe(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length < 64 || file.ReadByte() != 'M' || file.ReadByte() != 'Z')
            throw new InvalidDataException("File không phải Windows executable hợp lệ.");
    }

    [GeneratedRegex(@"\A(?<version>\d+\.\d+\.\d+)\+desktopbuild\.(?<build>[1-9]\d*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationVersion();
}

public static class DesktopReleaseInspectionTransport
{
    public const string ClientName = "DesktopReleaseInspection";

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
        AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2), MaxConnectionsPerServer = 1,
        ConnectCallback = ConnectPublicAsync
    };

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return b[0] is >= 0x20 and <= 0x3f && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8);
        return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224
            && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] is >= 16 and <= 31)
            && !(b[0] == 192 && b[1] == 168) && !(b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)
            && !(b[0] == 100 && b[1] is >= 64 and <= 127) && !(b[0] == 198 && b[1] is 18 or 19)
            && !(b[0] == 198 && b[1] == 51 && b[2] == 100) && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
    }

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new HttpRequestException("Artifact source resolved to a blocked address.");
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(address, context.DnsEndPoint.Port, token); return new NetworkStream(socket, ownsSocket: true); }
            catch (SocketException) { socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new HttpRequestException("Artifact source could not be reached.");
    }
}

public sealed partial class DesktopReleaseArchiveInspector(HttpClient httpClient, IWebHostEnvironment environment,
    IOptions<DesktopReleaseOptions> options) : IDesktopReleaseArchiveInspector
{
    private const long FreeSpaceReserve = 64L * 1024 * 1024;
    private const int MaximumManifestBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };

    public async Task<DesktopReleaseInspectionResult> InspectAsync(DesktopReleaseInspectionRequest request,
        IProgress<DesktopReleaseInspectionProgress>? progress, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        Uri url;
        try
        {
            if (!settings.SharedBootstrapperEnabled || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(request.ExpectedVersion)
                || request.PreparationId == Guid.Empty || request.Generation <= 0) throw new InvalidDataException();
            url = DesktopArtifactDownloadPolicy.Validate(new Uri(settings.ApiBaseUrl), request.DownloadUrl,
                DesktopArtifactSourceKinds.ExternalVps, settings.AllowedExternalPrefixes);
            if (!DesktopArtifactDownloadPolicy.MatchesPrefix(url, DesktopArtifactDownloadPolicy.ApplicationPrefix)
                && !DesktopArtifactDownloadPolicy.MatchesPrefix(url, DesktopArtifactDownloadPolicy.ModelHostPackagePrefix)) throw new InvalidDataException();
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or UriFormatException)
        { throw Failure("desktop_preparation_url_invalid", "Link ZIP hoặc cấu hình phân phối chưa hợp lệ."); }

        var scratch = Path.GetFullPath(settings.ScratchRoot, environment.ContentRootPath);
        Directory.CreateDirectory(scratch); AssertNoReparse(scratch);
        var operation = Path.Combine(scratch, $"{request.PreparationId:N}-{request.Generation}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(operation); AssertNoReparse(operation);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.InspectionTimeoutSeconds, 5, 7200)));
        var token = deadline.Token;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            message.Headers.Accept.ParseAdd("application/zip, application/octet-stream");
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Any())
                throw Failure("desktop_preparation_download_failed", "Không thể tải ZIP trực tiếp từ link đã nhập.");
            var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (mediaType is not ("application/zip" or "application/octet-stream" or "application/x-zip-compressed"))
                throw Failure("desktop_preparation_not_zip", "Link tải không trả về file ZIP hợp lệ.");
            var maximum = Math.Min(settings.MaximumExternalArtifactBytes, DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes);
            var declared = response.Content.Headers.ContentLength;
            if (maximum < 1 || declared is <= 0 || declared > maximum)
                throw Failure("desktop_preparation_size_invalid", "Dung lượng ZIP vượt giới hạn hoặc không hợp lệ.");
            RequireFreeSpace(operation, (declared ?? maximum) + FreeSpaceReserve);
            var fileName = ResolveFileName(response, url, request.PreparationId);
            var archivePath = Path.Combine(operation, "package.zip.part");
            var downloaded = 0L;
            string sha256;
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                var buffer = ArrayPool<byte>.Shared.Rent(81920);
                var nextProgress = 0L;
                try
                {
                    int count;
                    while ((count = await input.ReadAsync(buffer.AsMemory(0, 81920), token)) > 0)
                    {
                        downloaded = checked(downloaded + count);
                        if (downloaded > maximum || declared.HasValue && downloaded > declared.Value)
                            throw Failure("desktop_preparation_size_invalid", "Dung lượng ZIP thực không hợp lệ.");
                        hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), token);
                        if (downloaded >= nextProgress)
                        {
                            RequireFreeSpace(operation, FreeSpaceReserve);
                            progress?.Report(new(DesktopReleasePreparationStatuses.Downloading, downloaded, declared));
                            nextProgress = downloaded + 4 * 1024 * 1024;
                        }
                    }
                    await output.FlushAsync(token);
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
                if (downloaded < 4 || declared.HasValue && declared.Value != downloaded)
                    throw Failure("desktop_preparation_download_incomplete", "File ZIP tải về chưa đầy đủ.");
                sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
            progress?.Report(new(DesktopReleasePreparationStatuses.Inspecting, downloaded, downloaded));
            var inspected = await InspectPackageAsync(archivePath, operation, sha256, request.ExpectedVersion, settings, token);
            return new(fileName, downloaded, sha256, inspected.Manifest, inspected.Expanded, inspected.Entries,
                inspected.Profile.ModelBundleId, inspected.Profile.MinimumBootstrapperVersion, inspected.Profile, inspected.Metadata);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw Failure("desktop_preparation_timeout", "Kiểm tra ZIP quá thời gian cho phép. Hãy thử lại."); }
        catch (DesktopReleaseInspectionException) { throw; }
        catch (HttpRequestException) { throw Failure("desktop_preparation_download_failed", "Không tải được ZIP. Kiểm tra link, kết nối và chứng chỉ HTTPS."); }
        catch (Exception error) when (error is InvalidDataException or JsonException or OverflowException)
        { throw Failure("desktop_preparation_package_invalid", "Gói ZIP không hợp lệ hoặc thiếu thông tin phát hành."); }
        catch (IOException) { throw Failure("desktop_preparation_storage_failed", "Không đọc hoặc lưu được file kiểm tra. Kiểm tra dung lượng và thử lại."); }
        finally
        {
            try { AssertNoReparse(operation); Directory.Delete(operation, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* A later lease-aware cleanup may retry. */ }
        }
    }

    private static async Task<(DesktopUpdateManifest Manifest, DesktopPackageDistributionProfile Profile, long Expanded, int Entries, DesktopPackageMetadataDocument? Metadata)>
        InspectPackageAsync(string archivePath, string operation, string archiveSha256, string expectedVersion, DesktopReleaseOptions settings, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var layout = DesktopPackageMetadataPolicy.InspectLayout(archive);
        var files = layout.Files;
        var expanded = layout.ExpandedSizeBytes;
        var metadataCount = new[] { "update-manifest.json", "distribution-profile.json", "package-inventory.json" }.Count(files.ContainsKey);
        if (metadataCount == 0)
        {
            var metadata = await InspectPlainPublishAsync(layout, operation, archiveSha256, expectedVersion, settings, token);
            return (metadata.Manifest, metadata.DistributionProfile, expanded, layout.EntryCount, metadata);
        }
        if (!files.TryGetValue("update-manifest.json", out var manifestEntry)
            || !files.TryGetValue("distribution-profile.json", out var profileEntry)
            || !files.TryGetValue("package-inventory.json", out var inventoryEntry)
            || !files.ContainsKey("TOOL-LOCAL.exe")) throw new InvalidDataException();
        var manifest = await ReadJsonAsync<DesktopUpdateManifest>(manifestEntry, MaximumManifestBytes, token);
        var profile = await ReadJsonAsync<DesktopPackageDistributionProfile>(profileEntry, 64 * 1024, token);
        if (manifest.Product != "VideoMaker" || manifest.BuildNumber <= 0 || manifest.Platform != "win-x64"
            || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(manifest.Version)) throw new InvalidDataException();
        if (manifest.Version != expectedVersion)
            throw Failure("desktop_preparation_version_mismatch", $"Phiên bản đã nhập: {expectedVersion}; gói ZIP: {manifest.Version} (build {manifest.BuildNumber}). Hãy chọn đúng ZIP hoặc sửa phiên bản.");
        if (profile.SchemaVersion != 1 || profile.DistributionProtocol != DesktopDistributionProtocol.Shared
            || profile.Profile != settings.Profile || profile.Channel != "Stable" || profile.Platform != "win-x64"
            || !Uri.TryCreate(profile.ServerBaseUrl, UriKind.Absolute, out var api) || api.Scheme != Uri.UriSchemeHttps
            || profile.ServerBaseUrl.TrimEnd('/') != settings.ApiBaseUrl.TrimEnd('/')
            || profile.ModelBundleId != ApprovedStaticModelBundle.Current.BundleId
            || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(profile.MinimumBootstrapperVersion))
            throw Failure("desktop_preparation_profile_mismatch", "Gói ZIP không phù hợp API, bộ model hoặc Setup hệ thống.");
        if (manifest.ManagedFiles is not { Count: > 0 and <= DesktopArtifactDownloadPolicy.MaximumEntryCount }) throw new InvalidDataException();
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.ManagedFiles) if (!owned.Add(NormalizePath(file)) || !files.ContainsKey(file)) throw new InvalidDataException();
        if (owned.Count != files.Count || !owned.SetEquals(files.Keys)
            || DesktopMediaBundleIntegrity.RequiredRelativePaths.Any(path => !owned.Contains(path))) throw new InvalidDataException();

        var inventory = await ReadJsonAsync<PackageInventory>(inventoryEntry, MaximumManifestBytes, token);
        if (inventory.SchemaVersion != 1 || inventory.Excludes is not { Count: 1 }
            || inventory.Excludes[0] != "package-inventory.json" || inventory.Files is null
            || inventory.Files.Count != files.Count - 1) throw new InvalidDataException();
        var expectedFiles = new Dictionary<string, PackageInventoryFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in inventory.Files)
        {
            if (item is null || !expectedFiles.TryAdd(NormalizePath(item.Path), item)
                || item.Path.Equals("package-inventory.json", StringComparison.OrdinalIgnoreCase)
                || !files.TryGetValue(item.Path, out var entry) || item.SizeBytes != entry.Length
                || item.Sha256 is not { Length: 64 } || item.Sha256.Any(c => !char.IsAsciiHexDigit(c)))
                throw new InvalidDataException();
        }

        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long actualExpanded = 0;
            foreach (var (path, entry) in files)
            {
                token.ThrowIfCancellationRequested();
                // Extract only fixed inspection targets; never execute the package.
                var extract = path.Equals("TOOL-LOCAL.exe", StringComparison.OrdinalIgnoreCase)
                    || DesktopMediaBundleIntegrity.RequiredRelativePaths.Contains(path, StringComparer.OrdinalIgnoreCase);
                string? destination = extract ? Path.Combine(operation, path.Replace('/', Path.DirectorySeparatorChar)) : null;
                if (destination is not null)
                {
                    RequireFreeSpace(operation, entry.Length + FreeSpaceReserve);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!); AssertNoReparse(Path.GetDirectoryName(destination)!);
                }
                await using var output = destination is null ? null : new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                await using var input = entry.Open();
                using var entryHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long actualEntry = 0;
                int count;
                while ((count = await input.ReadAsync(buffer.AsMemory(0, 81920), token)) > 0)
                {
                    actualEntry = checked(actualEntry + count); actualExpanded = checked(actualExpanded + count);
                    if (actualEntry > entry.Length || actualExpanded > DesktopArtifactDownloadPolicy.MaximumExpandedBytes) throw new InvalidDataException();
                    entryHash.AppendData(buffer, 0, count);
                    if (output is not null) await output.WriteAsync(buffer.AsMemory(0, count), token);
                }
                if (actualEntry != entry.Length) throw new InvalidDataException();
                if (!path.Equals("package-inventory.json", StringComparison.OrdinalIgnoreCase)
                    && (!expectedFiles.TryGetValue(path, out var expectedFile)
                        || !Convert.ToHexString(entryHash.GetHashAndReset()).Equals(expectedFile.Sha256, StringComparison.OrdinalIgnoreCase)))
                    throw Failure("desktop_preparation_inventory_mismatch", "Checksum file bên trong ZIP không khớp danh mục. Hãy build lại và tải lên ZIP nguyên vẹn.");
            }
            if (actualExpanded != expanded) throw new InvalidDataException();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        var binary = DesktopExecutableMetadata.ReadApplicationVersion(Path.Combine(operation, "TOOL-LOCAL.exe"));
        if (binary.Version != manifest.Version || binary.BuildNumber != manifest.BuildNumber)
            throw Failure("desktop_preparation_binary_mismatch", $"Danh mục ZIP: {manifest.Version} (build {manifest.BuildNumber}); binary: {binary.Version} (build {binary.BuildNumber}). Hãy build lại đúng gói ứng dụng.");
        _ = DesktopMediaBundleIntegrity.ValidatePackageRoot(operation, owned);
        // Rich packages retain the original strict metadata/inventory checks. A wrapper is
        // supported only for plain Publish ZIPs: old clients still read rich roots directly.
        if (layout.PayloadRoot.Length != 0) throw new InvalidDataException();
        return (manifest, profile, expanded, archive.Entries.Count, null);
    }

    private static async Task<DesktopPackageMetadataDocument> InspectPlainPublishAsync(DesktopPackageArchiveLayout layout,
        string operation, string archiveSha256, string expectedVersion, DesktopReleaseOptions settings, CancellationToken token)
    {
        var files = layout.Files;
        if (!files.ContainsKey("TOOL-LOCAL.exe") || !files.TryGetValue("appsettings.json", out var settingsEntry)
            || DesktopMediaBundleIntegrity.RequiredRelativePaths.Any(path => !files.ContainsKey(path)))
            throw Failure("desktop_preparation_publish_incomplete", "ZIP Publish thiếu ứng dụng, appsettings.json hoặc bộ FFmpeg phát hành. Hãy nén đầy đủ thư mục Publish.");
        var appSettings = await ReadJsonAsync<JsonElement>(settingsEntry, 1024 * 1024, token);
        ValidatePlainSettings(appSettings, settings);
        var hashes = new List<DesktopPackageFileHash>(files.Count);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long expanded = 0;
            foreach (var (path, entry) in files.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                if (path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("piper-offline.zip", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("components/piper/", StringComparison.OrdinalIgnoreCase)
                    || path.Split('/').Any(x => x.Equals("__pycache__", StringComparison.OrdinalIgnoreCase)
                        || x.Equals(".venv", StringComparison.OrdinalIgnoreCase)))
                    throw Failure("desktop_preparation_private_payload", "ZIP ứng dụng chứa model hoặc cache cục bộ. Hãy dùng thư mục Publish sạch; Setup tải bộ model riêng.");
                var extract = path.Equals("TOOL-LOCAL.exe", StringComparison.OrdinalIgnoreCase)
                    || DesktopMediaBundleIntegrity.RequiredRelativePaths.Contains(path, StringComparer.OrdinalIgnoreCase);
                var destination = extract ? Path.Combine(operation, path.Replace('/', Path.DirectorySeparatorChar)) : null;
                if (destination is not null)
                {
                    RequireFreeSpace(operation, entry.Length + FreeSpaceReserve);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!); AssertNoReparse(Path.GetDirectoryName(destination)!);
                }
                await using var output = destination is null ? null : new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                await using var input = entry.Open();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, 81920), token)) > 0)
                {
                    received = checked(received + read); expanded = checked(expanded + read);
                    if (received > entry.Length || expanded > DesktopArtifactDownloadPolicy.MaximumExpandedBytes) throw new InvalidDataException();
                    hash.AppendData(buffer, 0, read);
                    if (output is not null) await output.WriteAsync(buffer.AsMemory(0, read), token);
                }
                if (received != entry.Length) throw new InvalidDataException();
                hashes.Add(new(path, received, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));
            }
            if (expanded != layout.ExpandedSizeBytes) throw new InvalidDataException();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        var binary = DesktopExecutableMetadata.ReadApplicationVersion(Path.Combine(operation, "TOOL-LOCAL.exe"));
        if (binary.Version != expectedVersion)
            throw Failure("desktop_preparation_version_mismatch", $"Phiên bản đã nhập: {expectedVersion}; binary ZIP: {binary.Version} (build {binary.BuildNumber}). Hãy chọn đúng ZIP hoặc sửa phiên bản.");
        _ = DesktopMediaBundleIntegrity.ValidatePackageRoot(operation, hashes.Select(x => x.Path));
        var manifest = new DesktopUpdateManifest("VideoMaker", binary.Version, binary.BuildNumber, "win-x64",
            hashes.Select(x => x.Path).Concat(["update-manifest.json", "distribution-profile.json", "package-inventory.json"]).Order(StringComparer.Ordinal).ToArray());
        var profile = new DesktopPackageDistributionProfile(1, DesktopDistributionProtocol.Shared, settings.Profile,
            DesktopSharedReleaseValidation.NormalizeApi(settings.ApiBaseUrl), "Stable", "win-x64", ApprovedStaticModelBundle.Current.BundleId, "1.1.0");
        var metadata = new DesktopPackageMetadataDocument(1, archiveSha256, layout.PayloadRoot, manifest, profile, hashes,
            layout.ExpandedSizeBytes, layout.EntryCount);
        DesktopPackageMetadataPolicy.ValidateDocument(metadata);
        return metadata;
    }

    private static void ValidatePlainSettings(JsonElement root, DesktopReleaseOptions settings)
    {
        RejectAmbiguousSettingNames(root);
        static JsonElement Required(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var found)
            ? found : throw new InvalidDataException();
        static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var found)
            && found.ValueKind == JsonValueKind.String ? found.GetString() : null;
        var server = Required(root, "Server"); var distribution = Required(root, "ModelDistribution"); var features = Required(root, "Features");
        var apiText = Text(server, "BaseUrl");
        if (!Uri.TryCreate(apiText, UriKind.Absolute, out var api) || api.Scheme != Uri.UriSchemeHttps || api.UserInfo.Length != 0
            || api.Query.Length != 0 || api.Fragment.Length != 0 || api.AbsoluteUri.TrimEnd('/') != new Uri(settings.ApiBaseUrl).AbsoluteUri.TrimEnd('/')
            || Text(distribution, "Mode") != "StaticArchive" || Required(distribution, "Enabled").ValueKind != JsonValueKind.False
            || !string.IsNullOrWhiteSpace(Text(distribution, "BaseUrl")) || !string.IsNullOrWhiteSpace(Text(distribution, "CacheRoot"))
            || new[] { "VietsubEnabled", "VietsubOcrEnabled", "VietsubLocalTranslationEnabled", "VietsubLocalVoiceEnabled" }
                .Any(name => Required(features, name).ValueKind != JsonValueKind.True))
            throw Failure("desktop_preparation_profile_mismatch", "Cấu hình Publish không khớp API hoặc chế độ model bắt buộc. Hãy Publish với profile phát hành đúng hệ thống.");
    }

    private static void RejectAmbiguousSettingNames(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw Failure("desktop_preparation_profile_mismatch", "Cấu hình Publish có tên trường trùng hoặc chỉ khác chữ hoa/thường. Hãy sửa appsettings.json rồi nén lại thư mục Publish.");
                RejectAmbiguousSettingNames(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) RejectAmbiguousSettingNames(child);
    }

    private static async Task<T> ReadJsonAsync<T>(ZipArchiveEntry entry, int maximum, CancellationToken token)
    {
        if (entry.Length is <= 0 || entry.Length > maximum) throw new InvalidDataException();
        await using var input = entry.Open(); using var data = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        { if (data.Length + count > maximum) throw new InvalidDataException(); data.Write(buffer, 0, count); }
        if (data.Length != entry.Length) throw new InvalidDataException();
        var bytes = data.ToArray();
        var start = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        return JsonSerializer.Deserialize<T>(bytes.AsSpan(start), Json) ?? throw new InvalidDataException();
    }

    private sealed record PackageInventory(int SchemaVersion, IReadOnlyList<string>? Excludes, IReadOnlyList<PackageInventoryFile>? Files);
    private sealed record PackageInventoryFile(string Path, long SizeBytes, string Sha256);

    internal static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 260 || path.Contains('\\') || path.Contains(':')
            || path.Any(c => char.IsControl(c) || "<>\"|?*".Contains(c))
            || path.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || ReservedName().IsMatch(part.Split('.')[0])))
            throw new InvalidDataException();
        return path;
    }

    private static string ResolveFileName(HttpResponseMessage response, Uri url, Guid id)
    {
        var name = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? url.Segments.LastOrDefault();
        if (name is not null && name.Length <= DesktopArtifactDownloadPolicy.MaximumArtifactFileNameLength && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        { try { if (!NormalizePath(name).Contains('/')) return name; } catch (InvalidDataException) { } }
        return $"VideoMaker-{id:N}.zip";
    }

    private static void AssertNoReparse(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Unsafe scratch directory.");
    }
    private static void RequireFreeSpace(string path, long bytes)
    {
        var root = Path.GetPathRoot(path) ?? throw new IOException();
        if (new DriveInfo(root).AvailableFreeSpace < bytes) throw Failure("desktop_preparation_disk_full", "Không đủ dung lượng để kiểm tra ZIP.");
    }
    private static DesktopReleaseInspectionException Failure(string code, string message) => new(code, message);
    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedName();
}

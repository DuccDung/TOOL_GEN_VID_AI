using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SHARED.Distribution.Updates;

public sealed record ResolvedDesktopStandalonePackage(DesktopReleaseResponse Release, DesktopPackageMetadataDocument Metadata,
    DesktopStandaloneReleaseResponse Catalog);

/// <summary>Checks the administrator-declared archive on the installing machine. Never used by the catalog server.</summary>
public static partial class DesktopStandalonePackageResolver
{
    public static async Task<ResolvedDesktopStandalonePackage> ResolveAsync(string archivePath,
        DesktopStandaloneReleaseResponse catalog, CancellationToken token)
    {
        DesktopStandaloneCatalogPolicy.ValidateRelease(catalog);
        AssertSafeFile(archivePath);
        await using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (file.Length is <= 0 or > DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes
            || !Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(catalog.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ZIP tải về không khớp SHA-256 đã phát hành. Chưa cài ứng dụng.");
        file.Position = 0;
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        var layout = DesktopPackageMetadataPolicy.InspectLayout(archive);
        if (new[] { "update-manifest.json", "distribution-profile.json", "package-inventory.json" }.Any(layout.Files.ContainsKey))
            throw new InvalidDataException("Luồng trực tiếp cần ZIP Publish thông thường; hãy dùng công cụ đóng gói mới.");
        if (!layout.Files.TryGetValue("appsettings.json", out var settings) || !layout.Files.TryGetValue("TOOL-LOCAL.exe", out var application)
            || application.Length is <= 0 or > 512L * 1024 * 1024) throw new InvalidDataException("ZIP thiếu ứng dụng hoặc cấu hình phát hành.");
        await ValidateSettingsAsync(settings, catalog, token);
        var hashes = new List<DesktopPackageFileHash>(layout.Files.Count);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            foreach (var (name, entry) in layout.Files.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                if (name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("piper-offline.zip", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("components/piper/", StringComparison.OrdinalIgnoreCase)
                    || name.Split('/').Any(x => x.Equals(".venv", StringComparison.OrdinalIgnoreCase) || x.Equals("__pycache__", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("ZIP ứng dụng chứa model hoặc cache riêng; Setup dùng bộ model đã pin.");
                await using var input = entry.Open();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long count = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
                {
                    count += read;
                    if (count > entry.Length) throw new InvalidDataException("ZIP có kích thước không hợp lệ.");
                    hash.AppendData(buffer, 0, read);
                }
                if (count != entry.Length) throw new InvalidDataException("ZIP chưa đầy đủ.");
                hashes.Add(new(name, count, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }

        // Only inspect the PE version resource after the entire ZIP passed the
        // independently declared archive hash. Never load or execute it here.
        var pePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(archivePath))!, ".direct-pe-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(pePath)!);
            if (drive.AvailableFreeSpace < application.Length + 64L * 1024 * 1024) throw new IOException("Không đủ chỗ để kiểm phiên bản ứng dụng.");
            await using (var output = new FileStream(pePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            await using (var input = application.Open())
            {
                long written = 0;
                var copyBuffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(copyBuffer, token)) > 0)
                {
                    written += read;
                    if (written > application.Length) throw new InvalidDataException();
                    await output.WriteAsync(copyBuffer.AsMemory(0, read), token);
                }
                if (written != application.Length) throw new InvalidDataException();
            }
            AssertSafeFile(pePath);
            using (var pe = File.OpenRead(pePath))
                if (pe.Length < 64 || pe.ReadByte() != 'M' || pe.ReadByte() != 'Z') throw new InvalidDataException("Ứng dụng không phải Windows executable.");
            var productVersion = FileVersionInfo.GetVersionInfo(pePath).ProductVersion;
            var match = NativeVersion().Match(productVersion ?? "");
            if (!match.Success || match.Groups["version"].Value != catalog.Version
                || !int.TryParse(match.Groups["build"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var build)
                || build != catalog.DeclaredBuildNumber)
                throw new InvalidDataException("Phiên bản/build trong ZIP không khớp thông tin phát hành.");
        }
        finally
        {
            // Exact file created with CreateNew in the already checked private cache.
            if (File.Exists(pePath)) { AssertSafeFile(pePath); File.Delete(pePath); }
        }
        var managed = hashes.Select(x => x.Path).Concat(["update-manifest.json", "distribution-profile.json", "package-inventory.json"])
            .Order(StringComparer.Ordinal).ToArray();
        var manifest = new DesktopUpdateManifest("VideoMaker", catalog.Version, catalog.DeclaredBuildNumber, catalog.Platform, managed);
        var profile = new DesktopPackageDistributionProfile(1, DesktopDistributionProtocol.Shared, catalog.Profile,
            new Uri(catalog.ApiBaseUrl).AbsoluteUri.TrimEnd('/') + "/", catalog.Channel, catalog.Platform,
            catalog.ModelBundleId, catalog.MinimumSetupVersion);
        var document = new DesktopPackageMetadataDocument(1, catalog.ExpectedSha256, layout.PayloadRoot, manifest, profile, hashes,
            layout.ExpandedSizeBytes, layout.EntryCount);
        _ = DesktopPackageMetadataPolicy.SerializeDocument(document);
        var release = new DesktopReleaseResponse(catalog.ReleaseId, "VideoMaker", catalog.Version, catalog.DeclaredBuildNumber,
            catalog.Channel, catalog.Platform, null, catalog.ReleaseNotes, catalog.PublishedAtUtc, catalog.FileName,
            catalog.DownloadUrl, file.Length, catalog.ExpectedSha256, DesktopArtifactSourceKinds.ExternalVps,
            DesktopInstallModes.StaticArchive, DesktopDistributionProtocol.Shared, null, catalog.ModelBundleId,
            null, catalog.Profile, catalog.MinimumSetupVersion);
        DesktopStandaloneCatalogPolicy.ValidateMeasuredPackage(release, new Uri(catalog.ApiBaseUrl));
        return new(release, document, catalog);
    }

    public static Task ExtractAndMaterializeAsync(string archivePath, string stageRoot,
        ResolvedDesktopStandalonePackage package, CancellationToken token)
    {
        var catalog = package.Catalog;
        DesktopStandaloneCatalogPolicy.ValidateRelease(catalog);
        var release = package.Release;
        if (release.ReleaseId != catalog.ReleaseId || release.Version != catalog.Version || release.BuildNumber != catalog.DeclaredBuildNumber
            || release.Sha256 != catalog.ExpectedSha256 || release.DownloadUrl != catalog.DownloadUrl || release.FileName != catalog.FileName
            || release.Channel != catalog.Channel || release.Platform != catalog.Platform || release.Profile != catalog.Profile
            || release.ModelBundleId != catalog.ModelBundleId || release.MinimumBootstrapperVersion != catalog.MinimumSetupVersion
            || !DesktopSharedBootstrapperPolicy.SameApi(new Uri(package.Metadata.DistributionProfile.ServerBaseUrl), new Uri(catalog.ApiBaseUrl)))
            throw new InvalidDataException("Metadata cục bộ không khớp catalog đã kiểm tra.");
        return DesktopPackageMetadataPolicy.ExtractVerifiedStandaloneAsync(archivePath, stageRoot, release, package.Metadata, token);
    }

    private static async Task ValidateSettingsAsync(ZipArchiveEntry entry, DesktopStandaloneReleaseResponse catalog, CancellationToken token)
    {
        if (entry.Length is <= 0 or > 1024 * 1024) throw new InvalidDataException("Cấu hình phát hành không hợp lệ.");
        await using var input = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > entry.Length) throw new InvalidDataException();
            output.Write(buffer, 0, read);
        }
        if (output.Length != entry.Length) throw new InvalidDataException();
        var bytes = output.ToArray();
        var start = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        try
        {
            using var json = JsonDocument.Parse(bytes.AsMemory(start), new JsonDocumentOptions { MaxDepth = 32 });
            RejectDuplicates(json.RootElement);
            var root = json.RootElement;
            var server = root.GetProperty("Server"); var distribution = root.GetProperty("ModelDistribution"); var features = root.GetProperty("Features");
            var api = new Uri(server.GetProperty("BaseUrl").GetString()!);
            if (api.Scheme != "https" || api.UserInfo.Length != 0 || api.Query.Length != 0 || api.Fragment.Length != 0
                || !DesktopSharedBootstrapperPolicy.SameApi(api, new Uri(catalog.ApiBaseUrl))
                || distribution.GetProperty("Mode").GetString() != "StaticArchive" || distribution.GetProperty("Enabled").ValueKind != JsonValueKind.False
                || new[] { "BaseUrl", "CacheRoot" }.Any(x => distribution.TryGetProperty(x, out var item) && item.ValueKind != JsonValueKind.Null
                    && (item.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(item.GetString())))
                || new[] { "VietsubEnabled", "VietsubOcrEnabled", "VietsubLocalTranslationEnabled", "VietsubLocalVoiceEnabled" }
                    .Any(x => features.GetProperty(x).ValueKind != JsonValueKind.True)) throw new InvalidDataException();
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException or InvalidDataException)
        { throw new InvalidDataException("Cấu hình trong ZIP không khớp API hoặc các thành phần local bắt buộc."); }
    }

    private static void RejectDuplicates(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in item.EnumerateObject())
            { if (!names.Add(property.Name)) throw new InvalidDataException(); RejectDuplicates(property.Value); }
        }
        else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) RejectDuplicates(child);
    }

    private static void AssertSafeFile(string path)
    {
        for (FileSystemInfo? item = new FileInfo(Path.GetFullPath(path)); item is not null;
            item = item is DirectoryInfo directory ? directory.Parent : new DirectoryInfo(Path.GetDirectoryName(item.FullName)!))
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Đường dẫn cài đặt không an toàn.");
    }

    [GeneratedRegex(@"\A(?<version>\d+\.\d+\.\d+)\+desktopbuild\.(?<build>[1-9]\d*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex NativeVersion();
}

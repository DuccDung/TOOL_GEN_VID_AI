using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using TOOL_SHARED.Distribution;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_LOCAL.Updates;

internal sealed class DesktopPackageUpdateService(HttpClient httpClient, bool staticArchiveMode = false)
{
    private const string LauncherExecutableName = "TOOL-LOCAL.exe";
    private const string UpdaterExecutableName = "VideoMaker.Updater.exe";

    public Task StartStandaloneAsync(DesktopStandaloneReleaseResponse release, IProgress<DesktopUpdateProgress>? progress,
        CancellationToken token, string action = "update") =>
        new DesktopInstalledMaintenanceService(httpClient.BaseAddress ?? throw new InvalidOperationException("Thiếu API cập nhật."))
            .StartStandaloneAsync(release, action, progress, token);

    public Task StartDirectAsync(DesktopDirectReleaseResponse release, IProgress<DesktopUpdateProgress>? progress,
        CancellationToken token, string action = "update") =>
        new DesktopInstalledMaintenanceService(httpClient.BaseAddress ?? throw new InvalidOperationException("Thiếu API cập nhật."))
            .StartDirectAsync(release, action, progress, token);

    public async Task StartAsync(
        DesktopReleaseResponse release,
        IProgress<DesktopUpdateProgress>? progress,
        CancellationToken cancellationToken,
        string action = "update")
    {
        var api = httpClient.BaseAddress ?? throw new InvalidOperationException("Update API address is unavailable.");
        if (staticArchiveMode || release.InstallMode == DesktopInstallModes.StaticArchive ||
            release.SourceKind == DesktopArtifactSourceKinds.ExternalVps)
        {
            try { await new DesktopInstalledMaintenanceService(api).StartAsync(release, action, progress, cancellationToken); }
            catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
            {
                throw new InvalidOperationException("Chưa chuyển giao được cho bộ cài đúng phiên bản. Ứng dụng vẫn mở; hãy thử lại hoặc chạy đúng Setup để sửa chữa.", error);
            }
            return;
        }
        if (release.InstallMode != DesktopInstallModes.Legacy || release.DistributionProtocol is < 1 or > DesktopDistributionProtocol.Current ||
            release.SizeBytes is <= 0 or > DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes)
            throw new InvalidDataException("Desktop update delivery is incompatible with this client.");
        DesktopArtifactDownloadPolicy.Validate(api, release.DownloadUrl, release.SourceKind);
        CleanupOldUpdates();
        var updateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolGenPostVideo",
            "Updates");
        var updateDirectory = Path.Combine(
            updateRoot,
            $"{SafeSegment(release.Version)}-{release.BuildNumber}-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
        var packagePath = Path.Combine(updateDirectory, "package.zip");
        var payloadDirectory = Path.Combine(updateDirectory, "payload");
        Directory.CreateDirectory(payloadDirectory);

        try
        {
            progress?.Report(new DesktopUpdateProgress("download", 2, "Đang chuẩn bị tải bản cập nhật..."));
            await DownloadAndVerifyAsync(release, packagePath, progress, cancellationToken);
            progress?.Report(new DesktopUpdateProgress("extract", 64, "Đang kiểm tra và giải nén package..."));
            await ExtractSafelyAsync(packagePath, payloadDirectory, progress, cancellationToken);
            ValidatePayload(release, payloadDirectory);

            var installedUpdaterDirectory = Path.Combine(AppContext.BaseDirectory, "_updater");
            var installedUpdater = Path.Combine(installedUpdaterDirectory, UpdaterExecutableName);
            if (!File.Exists(installedUpdater))
            {
                throw new FileNotFoundException("Không tìm thấy trình cập nhật taphoatool trong bản cài đặt.", installedUpdater);
            }

            var stagedUpdaterDirectory = Path.Combine(updateDirectory, "updater-runtime");
            CopyDirectory(installedUpdaterDirectory, stagedUpdaterDirectory);
            var stagedUpdater = Path.Combine(stagedUpdaterDirectory, UpdaterExecutableName);
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolGenPostVideo",
                "UpdateLogs",
                $"update-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
            progress?.Report(new DesktopUpdateProgress("ready", 100, "Đã tải xong. taphoatool sẽ khởi động lại..."));
            var startInfo = new ProcessStartInfo
            {
                FileName = stagedUpdater,
                WorkingDirectory = stagedUpdaterDirectory,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("--pid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("--stage");
            startInfo.ArgumentList.Add(payloadDirectory);
            startInfo.ArgumentList.Add("--target");
            startInfo.ArgumentList.Add(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            startInfo.ArgumentList.Add("--restart");
            startInfo.ArgumentList.Add(LauncherExecutableName);
            startInfo.ArgumentList.Add("--log");
            startInfo.ArgumentList.Add(logPath);
            _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Không thể khởi động trình cập nhật taphoatool.");
        }
        catch
        {
            TryDeleteDirectory(updateDirectory);
            throw;
        }
    }

    private async Task DownloadAndVerifyAsync(
        DesktopReleaseResponse release,
        string packagePath,
        IProgress<DesktopUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var resource = DesktopArtifactDownloadPolicy.Validate(httpClient.BaseAddress!, release.DownloadUrl, release.SourceKind);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false,
            AutomaticDecompression = System.Net.DecompressionMethods.None };
        using var download = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        using var response = await download.GetAsync(
            resource,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != System.Net.HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Count != 0 ||
            response.Content.Headers.ContentLength is { } responseLength && responseLength != release.SizeBytes ||
            response.Content.Headers.ContentType?.MediaType is not ("application/zip" or "application/octet-stream" or "application/x-zip-compressed"))
            throw new InvalidDataException("Desktop package response is invalid.");
        var totalBytes = response.Content.Headers.ContentLength ?? release.SizeBytes;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        var partPath = packagePath + ".part";
        await using (var output = new FileStream(partPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long written = 0;
            while (true)
            {
                using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                inactivity.CancelAfter(TimeSpan.FromSeconds(60));
                var read = await input.ReadAsync(buffer, inactivity.Token);
                if (read == 0)
                {
                    break;
                }

                if (written + read > release.SizeBytes) throw new InvalidDataException("Desktop package exceeds approved length.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hash.AppendData(buffer, 0, read);
                written += read;
                var percent = totalBytes > 0 ? 3 + (int)Math.Min(57, written * 57 / totalBytes) : 30;
                progress?.Report(new DesktopUpdateProgress("download", percent, $"Đang tải {FormatBytes(written)} / {FormatBytes(totalBytes)}"));
            }

            await output.FlushAsync(cancellationToken);
            if (written != release.SizeBytes)
            {
                throw new InvalidDataException("Kích thước package tải về không khớp release.");
            }

            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!actualHash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("SHA-256 của package không hợp lệ. Cập nhật đã bị hủy.");
            }
        }
        File.Move(partPath, packagePath);
    }

    private static async Task ExtractSafelyAsync(
        string packagePath,
        string destination,
        IProgress<DesktopUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var files = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
        if (files.Length > 200_000 || files.Sum(entry => entry.Length) > 4L * 1024 * 1024 * 1024)
        {
            throw new InvalidDataException("Package có quá nhiều file hoặc kích thước giải nén quá lớn.");
        }
        var commonRoot = DetectCommonRoot(files);
        var root = Path.GetFullPath(destination);
        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = files[index];
            var relativePath = NormalizeArchivePath(entry.FullName, commonRoot);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(destination, relativePath));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Package chứa đường dẫn không an toàn.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true);
            await input.CopyToAsync(output, cancellationToken);
            var percent = 64 + (int)Math.Min(30, (long)(index + 1) * 30 / Math.Max(1, files.Length));
            progress?.Report(new DesktopUpdateProgress("extract", percent, $"Đang giải nén ({index + 1}/{files.Length})"));
        }
    }

    private static void ValidatePayload(DesktopReleaseResponse release, string payloadDirectory)
    {
        var executable = Path.Combine(payloadDirectory, LauncherExecutableName);
        var manifestPath = Path.Combine(payloadDirectory, "update-manifest.json");
        if (!File.Exists(executable) || !File.Exists(manifestPath))
        {
            throw new InvalidDataException("Package thiếu executable hoặc update manifest.");
        }

        var manifest = JsonSerializer.Deserialize<DesktopUpdateManifest>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Update manifest không hợp lệ.");
        if (!string.Equals(manifest.Product, "VideoMaker", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.Version, release.Version, StringComparison.OrdinalIgnoreCase) ||
            manifest.BuildNumber != release.BuildNumber ||
            !string.Equals(manifest.Platform, release.Platform, StringComparison.OrdinalIgnoreCase) ||
            manifest.ManagedFiles is null ||
            !manifest.ManagedFiles.Any(path => string.Equals(path.Replace('\\', '/'), LauncherExecutableName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Update manifest không khớp release đã phát hành.");
        }

        DesktopMediaBundleIntegrity.ValidatePackageRoot(payloadDirectory, manifest.ManagedFiles);
    }

    private static string? DetectCommonRoot(IReadOnlyCollection<ZipArchiveEntry> entries)
    {
        var paths = entries.Select(entry => entry.FullName.Replace('\\', '/').TrimStart('/')).ToArray();
        if (paths.Length == 0 || paths.Any(path => !path.Contains('/')))
        {
            return null;
        }

        var roots = paths.Select(path => path[..path.IndexOf('/')]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return roots.Length == 1 ? roots[0] : null;
    }

    private static string NormalizeArchivePath(string path, string? commonRoot)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (!string.IsNullOrWhiteSpace(commonRoot) && normalized.StartsWith(commonRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[(commonRoot.Length + 1)..];
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
        {
            throw new InvalidDataException("Package chứa đường dẫn không an toàn.");
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void CleanupOldUpdates()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolGenPostVideo", "Updates");
        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            // Per-operation maintenance files belong to the independent Setup.
            if (Path.GetFileName(directory).Equals("maintenance", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (Directory.GetCreationTimeUtc(directory) < DateTime.UtcNow.AddDays(-3))
                    Directory.Delete(directory, recursive: true);
            }
            catch { }
        }
    }

    private static string SafeSegment(string value) =>
        string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static string FormatBytes(long bytes) => bytes < 1024 * 1024
        ? $"{bytes / 1024d:0.0} KB"
        : $"{bytes / 1024d / 1024d:0.0} MB";

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}

using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using TOOL_SHARED.Distribution;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_SETUP;

internal sealed partial class LauncherInstallerService(InstallerOptions options,
    HttpMessageHandler? metadataHandler = null, HttpMessageHandler? artifactHandler = null) : IInstallerSession
{
    private const string ExecutableName = "TOOL-LOCAL.exe";
    private readonly HttpClient _httpClient = new(metadataHandler ?? new HttpClientHandler
        { AllowAutoRedirect = false, UseCookies = false }, disposeHandler: metadataHandler is null)
    {
        BaseAddress = new Uri(options.ServerBaseUrl),
        Timeout = TimeSpan.FromMinutes(30)
    };

    public async Task<IReadOnlyList<DesktopReleaseResponse>> GetVersionsAsync(CancellationToken cancellationToken)
    {
        var path = $"api/launcher-distribution/versions?channel={Uri.EscapeDataString(options.Channel)}&platform={Uri.EscapeDataString(options.Platform)}&distributionProtocol={options.DistributionProtocol}";
        if (options.IsSharedBootstrapper) path += $"&profile={Uri.EscapeDataString(options.Profile)}";
        var releases = (await _httpClient.GetFromJsonAsync<DesktopReleaseListResponse>(path, cancellationToken))?.Releases
            ?? throw new InvalidDataException("Server không trả về danh sách phiên bản hợp lệ.");
        if (releases.Count > 1000 || releases.Any(release => release is null))
            throw new InvalidDataException("Danh sách phiên bản vượt giới hạn hoặc có phần tử không hợp lệ.");
        if (options.IsSharedBootstrapper)
            return releases.Where(release => IsCompatibleSharedRelease(release, options))
                .OrderByDescending(release => Version.Parse(release.Version)).ThenByDescending(release => release.BuildNumber).ToArray();
        return options.IsStaticArchive ? releases.Where(release => release.Version == options.DesktopVersion &&
            release.BuildNumber == options.DesktopBuildNumber && string.Equals(release.Sha256, options.DesktopSha256, StringComparison.OrdinalIgnoreCase)
            && release.InstallMode == "StaticArchive" && release.DistributionProtocol == 2
            && release.ModelBundleId == TOOL_SHARED.Distribution.StaticModels.ApprovedStaticModelBundle.Current.BundleId).ToArray() :
            releases.Where(release => release.InstallMode != "StaticArchive").ToArray();
    }

    private static bool IsCompatibleSharedRelease(DesktopReleaseResponse release, InstallerOptions installerOptions)
    {
        try { ValidateStaticRelease(release, installerOptions); return true; }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { return false; }
    }

    public async Task<DesktopReleaseResponse?> GetReleaseAsync(DesktopReleaseResponse snapshot, CancellationToken cancellationToken)
    {
        if (!options.IsSharedBootstrapper)
            return (await GetVersionsAsync(cancellationToken)).SingleOrDefault(release => release.ReleaseId == snapshot.ReleaseId);
        ValidateStaticRelease(snapshot, options);
        var path = $"api/launcher-distribution/versions/{snapshot.ReleaseId:D}?distributionProtocol=3&profile={Uri.EscapeDataString(options.Profile)}" +
            $"&bootstrapperId={snapshot.SharedBootstrapper!.BootstrapperId:D}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var approved = await response.Content.ReadFromJsonAsync<DesktopReleaseResponse>(cancellationToken)
            ?? throw new InvalidDataException("Server không trả snapshot release hợp lệ.");
        SetupMaintenanceHandoff.ValidateReleaseSnapshot(snapshot, approved, options, allowMetadataAugmentation: true);
        return approved;
    }

    public async Task InstallAsync(
        DesktopReleaseResponse release,
        string installDirectory,
        bool createDesktopShortcut,
        bool launchAfterInstall,
        IProgress<DesktopUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (progress is not null) progress = new MonotonicSetupProgress(progress);
        if (options.IsStaticArchive)
        {
            await InstallStaticAsync(release, installDirectory, createDesktopShortcut, launchAfterInstall,
                progress, cancellationToken);
            return;
        }
        ValidateApplicationRelease(release, options);
        var targetRoot = Path.GetFullPath(installDirectory.Trim());
        EnsureNotRunning(Path.Combine(targetRoot, ExecutableName));
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "VideoMakerSetup", Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(temporaryRoot, "package.zip");
        var stageRoot = Path.Combine(temporaryRoot, "stage");
        var backupRoot = Path.Combine(temporaryRoot, "backup");
        Directory.CreateDirectory(stageRoot);
        try
        {
            await DownloadStaticPackageAsync(release, packagePath, progress, cancellationToken);
            await Task.Run(() => ValidateDesktopArchive(packagePath), cancellationToken);
            await ExtractAsync(packagePath, stageRoot, progress, cancellationToken);
            var manifest = ValidateStage(release, stageRoot);
            InstallWithRollback(stageRoot, targetRoot, backupRoot, manifest, progress, cancellationToken);
            CreateShortcuts(targetRoot, createDesktopShortcut);
            progress?.Report(new DesktopUpdateProgress("complete", 100, "Cài đặt VideoMaker hoàn tất."));
            if (launchAfterInstall)
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(targetRoot, ExecutableName),
                    WorkingDirectory = targetRoot,
                    UseShellExecute = true
                });
            }
        }
        finally
        {
            TryDeleteDirectory(temporaryRoot);
        }
    }

    private static async Task ExtractAsync(
        string packagePath,
        string destination,
        IProgress<DesktopUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
        if (entries.Length > 200_000 || entries.Sum(entry => entry.Length) > 4L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Package có quá nhiều file hoặc kích thước giải nén quá lớn.");
        var commonRoot = DetectCommonRoot(entries);
        var safeRoot = Path.GetFullPath(destination);
        for (var index = 0; index < entries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[index];
            var relative = NormalizeEntry(entry.FullName, commonRoot);
            if (string.IsNullOrEmpty(relative)) continue;
            var target = Path.GetFullPath(Path.Combine(destination, relative));
            if (!target.StartsWith(safeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package chứa đường dẫn không an toàn.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true);
            await input.CopyToAsync(output, cancellationToken);
            progress?.Report(new DesktopUpdateProgress("extract", 60 + (int)((long)(index + 1) * 20 / Math.Max(1, entries.Length)), "Đang giải nén package..."));
        }
    }

    private static DesktopUpdateManifest ValidateStage(DesktopReleaseResponse release, string stageRoot)
    {
        if (!File.Exists(Path.Combine(stageRoot, ExecutableName)))
            throw new InvalidDataException("Package không chứa TOOL-LOCAL.exe ở thư mục gốc.");
        var manifestPath = Path.Combine(stageRoot, "update-manifest.json");
        var manifest = File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<DesktopUpdateManifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            : null;
        if (manifest is null ||
            !string.Equals(manifest.Product, "VideoMaker", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.Version, release.Version, StringComparison.OrdinalIgnoreCase) ||
            manifest.BuildNumber != release.BuildNumber ||
            manifest.ManagedFiles is null ||
            !string.Equals(manifest.Platform, release.Platform, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update manifest không khớp release.");
        var managedFiles = NormalizePaths(manifest.ManagedFiles);
        DesktopMediaBundleIntegrity.ValidatePackageRoot(stageRoot, managedFiles);
        return manifest;
    }

    private static void InstallWithRollback(
        string stageRoot,
        string targetRoot,
        string backupRoot,
        DesktopUpdateManifest manifest,
        IProgress<DesktopUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetRoot);
        var previous = LoadManifest(targetRoot);
        var newFiles = NormalizePaths(manifest.ManagedFiles);
        var previousFiles = previous is null ? Array.Empty<string>() : NormalizePaths(previous.ManagedFiles);
        var backedUp = new List<(string Destination, string Backup)>();
        var created = new List<string>();
        try
        {
            foreach (var relative in previousFiles.Except(newFiles, StringComparer.OrdinalIgnoreCase).Where(IsReplaceable))
            {
                var target = SafeCombine(targetRoot, relative);
                if (!File.Exists(target)) continue;
                Backup(relative, target, backupRoot, backedUp);
                File.Delete(target);
            }

            for (var index = 0; index < newFiles.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = newFiles[index];
                var source = SafeCombine(stageRoot, relative);
                if (!File.Exists(source)) throw new InvalidDataException($"Manifest tham chiếu file không tồn tại: {relative}");
                var target = SafeCombine(targetRoot, relative);
                if (!IsReplaceable(relative) && File.Exists(target)) continue;
                if (File.Exists(target)) Backup(relative, target, backupRoot, backedUp); else created.Add(target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
                progress?.Report(new DesktopUpdateProgress("install", 82 + (int)((long)(index + 1) * 15 / Math.Max(1, newFiles.Length)), "Đang cài đặt file ứng dụng..."));
            }
        }
        catch
        {
            foreach (var path in created.AsEnumerable().Reverse()) try { if (File.Exists(path)) File.Delete(path); } catch { }
            foreach (var item in backedUp.AsEnumerable().Reverse()) try { Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!); File.Copy(item.Backup, item.Destination, true); } catch { }
            throw;
        }
    }

    private static void CreateShortcuts(string targetRoot, bool desktop)
    {
        var executable = Path.Combine(targetRoot, ExecutableName);
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs");
        Directory.CreateDirectory(startMenu);
        ShellShortcutHelper.Create(Path.Combine(startMenu, "VideoMaker.lnk"), executable, targetRoot, "VideoMaker");
        if (desktop)
            ShellShortcutHelper.Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VideoMaker.lnk"), executable, targetRoot, "VideoMaker");
    }

    private static void EnsureNotRunning(string expectedPath)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExecutableName)))
        {
            using (process)
            {
                string? path = null;
                try { path = process.MainModule?.FileName; } catch { }
                if (path is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Hãy đóng VideoMaker trước khi cài đặt hoặc repair.");
            }
        }
    }

    private static DesktopUpdateManifest? LoadManifest(string root)
    {
        var path = Path.Combine(root, "update-manifest.json");
        try { return File.Exists(path) ? JsonSerializer.Deserialize<DesktopUpdateManifest>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)) : null; }
        catch { return null; }
    }

    private static string[] NormalizePaths(IEnumerable<string> paths) => paths.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)) throw new InvalidDataException("Manifest chứa đường dẫn không hợp lệ.");
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".." || segment.Contains(':'))) throw new InvalidDataException("Manifest chứa đường dẫn không an toàn.");
        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private static bool IsReplaceable(string relative) => !relative.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase) && !relative.Equals("appsettings.user.json", StringComparison.OrdinalIgnoreCase);
    private static void Backup(string relative, string target, string root, ICollection<(string Destination, string Backup)> list) { var backup = SafeCombine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Copy(target, backup, true); list.Add((target, backup)); }
    private static string SafeCombine(string root, string relative) { var safeRoot = Path.GetFullPath(root); var path = Path.GetFullPath(Path.Combine(safeRoot, relative)); if (!path.StartsWith(safeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Đường dẫn nằm ngoài thư mục cho phép."); return path; }
    private static string? DetectCommonRoot(IEnumerable<ZipArchiveEntry> entries) { var paths = entries.Select(x => x.FullName.Replace('\\', '/').TrimStart('/')).ToArray(); if (paths.Length == 0 || paths.Any(x => !x.Contains('/'))) return null; var roots = paths.Select(x => x[..x.IndexOf('/')]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); return roots.Length == 1 ? roots[0] : null; }
    private static string NormalizeEntry(string path, string? root) { var normalized = path.Replace('\\', '/').TrimStart('/'); if (root is not null && normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) normalized = normalized[(root.Length + 1)..]; var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries); if (segments.Any(x => x is "." or ".." || x.Contains(':'))) throw new InvalidDataException("Package chứa đường dẫn không an toàn."); return string.Join(Path.DirectorySeparatorChar, segments); }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
    public void Dispose() => _httpClient.Dispose();
}

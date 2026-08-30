using System.Diagnostics;
using System.Text.Json;
using TOOL_SHARED.Distribution;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_UPDATER;

internal sealed class UpdaterService
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> ProtectedFiles = new(PathComparer)
    {
        "appsettings.json",
        "appsettings.user.json"
    };
    private readonly UpdaterOptions options;
    private readonly IUpdaterRuntime runtime;

    public UpdaterService(UpdaterOptions options) : this(options, new SystemUpdaterRuntime())
    {
    }

    internal UpdaterService(UpdaterOptions options, IUpdaterRuntime runtime)
    {
        this.options = options;
        this.runtime = runtime;
    }

    public int Apply()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.LogPath)!);
        Log("Updater started.");
        var newManifest = LoadAndValidateManifest(options.StageDirectory);
        var newFiles = NormalizeManagedFiles(newManifest.ManagedFiles);
        if (!newFiles.Contains(options.RestartExecutableName, PathComparer))
        {
            throw new InvalidDataException("Manifest không chứa executable VideoMaker.");
        }
        DesktopMediaBundleIntegrity.ValidatePackageRoot(options.StageDirectory, newFiles);

        WaitForLauncherExit();
        Directory.CreateDirectory(options.TargetDirectory);
        var previousManifest = TryLoadManifest(options.TargetDirectory);
        var previousFiles = previousManifest is null
            ? Array.Empty<string>()
            : NormalizeManagedFiles(previousManifest.ManagedFiles);
        var backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolGenPostVideo",
            "UpdateBackups",
            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
        var backedUp = new List<(string Destination, string Backup)>();
        var newlyCreated = new List<string>();

        try
        {
            foreach (var relativePath in previousFiles.Except(newFiles, PathComparer).Where(IsManagedUpdatePath))
            {
                var destination = SafeCombine(options.TargetDirectory, relativePath);
                if (!File.Exists(destination))
                {
                    continue;
                }

                BackupFile(relativePath, destination, backupRoot, backedUp);
                File.Delete(destination);
                Log($"Removed obsolete managed file: {relativePath}");
            }

            foreach (var relativePath in newFiles.Where(IsManagedUpdatePath))
            {
                var source = SafeCombine(options.StageDirectory, relativePath);
                if (!File.Exists(source))
                {
                    throw new FileNotFoundException($"Package thiếu file trong manifest: {relativePath}", source);
                }

                var destination = SafeCombine(options.TargetDirectory, relativePath);
                if (File.Exists(destination))
                {
                    BackupFile(relativePath, destination, backupRoot, backedUp);
                }
                else
                {
                    newlyCreated.Add(destination);
                }

                CopyWithRetry(source, destination);
            }

            var executablePath = SafeCombine(options.TargetDirectory, options.RestartExecutableName);
            runtime.Start(executablePath, options.TargetDirectory);
            Log("Update applied and launcher restarted.");
            TryDeleteDirectory(backupRoot);
            return 0;
        }
        catch (Exception exception)
        {
            Log($"Update failed: {exception}");
            Rollback(newlyCreated, backedUp);
            throw new InvalidOperationException("Cập nhật thất bại. Phiên bản trước đã được khôi phục.", exception);
        }
    }

    private void WaitForLauncherExit()
    {
        Log($"Waiting for process {options.LauncherProcessId} to exit.");
        runtime.WaitForExit(options.LauncherProcessId, TimeSpan.FromSeconds(90));
    }

    private static DesktopUpdateManifest LoadAndValidateManifest(string root)
    {
        var manifest = TryLoadManifest(root)
            ?? throw new InvalidDataException("Không tìm thấy update-manifest.json.");
        if (!string.Equals(manifest.Product, "VideoMaker", StringComparison.OrdinalIgnoreCase) ||
            manifest.BuildNumber <= 0 ||
            manifest.ManagedFiles is not { Count: > 0 })
        {
            throw new InvalidDataException("Update manifest không hợp lệ.");
        }

        return manifest;
    }

    private static DesktopUpdateManifest? TryLoadManifest(string root)
    {
        var path = Path.Combine(root, "update-manifest.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DesktopUpdateManifest>(
                File.ReadAllText(path),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string[] NormalizeManagedFiles(IEnumerable<string> paths) =>
        paths
            .Select(NormalizeRelativePath)
            .Distinct(PathComparer)
            .ToArray();

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            throw new InvalidDataException("Manifest chứa đường dẫn không hợp lệ.");
        }

        var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();
        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
        {
            throw new InvalidDataException("Manifest chứa đường dẫn không an toàn.");
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private static bool IsManagedUpdatePath(string relativePath) =>
        !ProtectedFiles.Contains(relativePath) &&
        !relativePath.StartsWith("workspace" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
        !relativePath.Contains(".WebView2" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string SafeCombine(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Đường dẫn package nằm ngoài thư mục cho phép.");
        }

        return fullPath;
    }

    private static void BackupFile(
        string relativePath,
        string destination,
        string backupRoot,
        ICollection<(string Destination, string Backup)> backedUp)
    {
        var backup = SafeCombine(backupRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(destination, backup, overwrite: true);
        backedUp.Add((destination, backup));
    }

    private void CopyWithRetry(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                Log($"Copied: {Path.GetRelativePath(options.TargetDirectory, destination)}");
                return;
            }
            catch (IOException exception)
            {
                lastError = exception;
                Thread.Sleep(attempt * 250);
            }
            catch (UnauthorizedAccessException exception)
            {
                lastError = exception;
                Thread.Sleep(attempt * 250);
            }
        }

        throw new IOException($"Không thể cập nhật file {destination}.", lastError);
    }

    private void Rollback(IEnumerable<string> newlyCreated, IEnumerable<(string Destination, string Backup)> backedUp)
    {
        foreach (var path in newlyCreated.Reverse())
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception error) { Log($"Rollback delete failed: {error.Message}"); }
        }

        foreach (var item in backedUp.Reverse())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
                File.Copy(item.Backup, item.Destination, overwrite: true);
            }
            catch (Exception error)
            {
                Log($"Rollback restore failed: {error.Message}");
            }
        }
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(options.LogPath, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must not make the update fail.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}

internal interface IUpdaterRuntime
{
    void WaitForExit(int processId, TimeSpan timeout);

    void Start(string executablePath, string workingDirectory);
}

internal sealed class SystemUpdaterRuntime : IUpdaterRuntime
{
    public void WaitForExit(int processId, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                throw new TimeoutException("VideoMaker không thể đóng trong thời gian cho phép.");
            }
        }
        catch (ArgumentException)
        {
            // The launcher already exited.
        }
    }

    public void Start(string executablePath, string workingDirectory)
    {
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Không thể khởi động lại VideoMaker.");
    }
}

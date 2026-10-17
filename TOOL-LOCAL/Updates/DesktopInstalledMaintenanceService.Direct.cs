using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using TOOL_LOCAL.SystemSetup;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_LOCAL.Updates;

internal sealed partial class DesktopInstalledMaintenanceService
{
    public async Task StartDirectAsync(DesktopDirectReleaseResponse release, string action,
        IProgress<DesktopUpdateProgress>? progress, CancellationToken token)
    {
        DesktopDirectCatalogPolicy.ValidateRelease(release, apiOrigin, DesktopBuildInfo.Profile);
        var setup = release.SharedBootstrapper;
        var setupUrl = DesktopArtifactDownloadPolicy.Validate(apiOrigin, setup.DownloadUrl, DesktopArtifactSourceKinds.Internal);
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        var installed = ReadOwnership(target, SystemSetupPaths.MetadataRoot);
        if (installed.Version != DesktopBuildInfo.Version || installed.BuildNumber != DesktopBuildInfo.BuildNumber || installed.BundleId != release.ModelBundleId)
            throw new InvalidDataException("Bản cài chưa có ownership tương thích. Hãy chạy Setup mới độc lập để sửa.");
        var operation = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolGenPostVideo", "Updates", "maintenance", operation);
        AssertSafePath(directory); Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "VideoMaker.Setup.exe");
        Process? process = null; var handedOff = false;
        try
        {
            progress?.Report(new("download", 2, "Đang tải Setup để kiểm tra và cài phiên bản từ VPS..."));
            await DownloadSetupAsync(setupUrl, new(setup.FileName, setup.SizeBytes, setup.Sha256), executable, progress, token);
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var pipeName = "VideoMaker.Setup." + Environment.ProcessId + "." + operation;
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var current = Process.GetCurrentProcess(); using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User?.Value ?? throw new InvalidOperationException("Không xác định được người dùng Windows.");
            var request = new DesktopDirectMaintenanceRequest(2, operation, nonce, action, release, target,
                installed.Version, installed.BuildNumber, installed.PackageSha256, current.Id, current.StartTime.ToUniversalTime().Ticks,
                user, true, DateTime.UtcNow);
            DesktopMaintenanceProtocol.ValidateRequest(request, DateTime.UtcNow);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = directory };
            foreach (var argument in new[] { "--maintenance-pipe", pipeName, "--maintenance-nonce", nonce }) start.ArgumentList.Add(argument);
            process = Process.Start(start) ?? throw new InvalidOperationException("Không thể mở Setup.");
            var started = process.StartTime.ToUniversalTime().Ticks;
            var connected = pipe.WaitForConnectionAsync(timeout.Token); var exited = process.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(connected, exited) != connected) throw new InvalidOperationException("Setup chưa nhận yêu cầu. Ứng dụng vẫn mở.");
            await connected;
            if (DesktopMaintenanceProtocol.GetClientProcessId(pipe) != process.Id) throw new InvalidDataException("IPC không thuộc tiến trình Setup.");
            await DesktopMaintenanceProtocol.WriteAsync(pipe, request, timeout.Token);
            var ack = await DesktopMaintenanceProtocol.ReadAsync<DesktopMaintenanceAck>(pipe, timeout.Token);
            DesktopMaintenanceProtocol.ValidateAck(request, ack, process.Id, started, DateTime.UtcNow);
            if (process.HasExited) throw new InvalidOperationException("Setup đã đóng trước khi nhận chuyển giao.");
            timeout.Cancel(); handedOff = true;
            progress?.Report(new("ready", 100, "Setup đã nhận chuyển giao. Ứng dụng sẽ đóng để kiểm tra và hoàn tất cài đặt."));
        }
        finally
        {
            if (!handedOff && process is { HasExited: false })
            { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            process?.Dispose();
            if (!handedOff && Directory.Exists(directory))
            {
                AssertSafePath(directory);
                if (Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
                    .Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)) throw new IOException("Unsafe maintenance cleanup path.");
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

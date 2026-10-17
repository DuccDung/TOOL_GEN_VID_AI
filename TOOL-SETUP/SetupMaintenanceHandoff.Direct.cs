using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_SETUP;

internal static partial class SetupMaintenanceHandoff
{
    private static async Task<PreparedMaintenance> AcceptDirectAsync(NamedPipeClientStream pipe, int serverPid,
        DesktopDirectMaintenanceRequest request, MaintenanceInvocation invocation, InstallerOptions options,
        IInstallerSession service, CancellationToken token)
    {
        var userSid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidDataException("Không xác định được người dùng Windows.");
        ValidateDirectRequest(request, invocation, options, userSid);
        if (serverPid != request.ParentProcessId) throw new InvalidDataException("Tiến trình sở hữu IPC không khớp.");
        using var parent = Process.GetProcessById(request.ParentProcessId);
        ValidateDirectParent(parent, request, userSid);
        ValidateOwnership(request.TargetRoot, request.CurrentVersion, request.CurrentBuildNumber, request.CurrentPackageSha256);
        var approved = await service.GetDirectReleaseAsync(request.Release, token)
            ?? throw new InvalidDataException("Catalog không còn đúng release được yêu cầu. Ứng dụng hiện tại được giữ nguyên.");
        LauncherInstallerService.ValidateDirectSnapshot(request.Release, approved, options);
        await LauncherInstallerService.ValidateDirectBootstrapperFileAsync(Environment.ProcessPath ?? throw new InvalidDataException("Không xác định được Setup."), approved, options, token);
        ValidateDirectRequest(request, invocation, options, userSid); ValidateDirectParent(parent, request, userSid);
        ValidateOwnership(request.TargetRoot, request.CurrentVersion, request.CurrentBuildNumber, request.CurrentPackageSha256);
        using var current = Process.GetCurrentProcess();
        var ack = new DesktopMaintenanceAck(2, request.OperationId, request.Nonce, approved.ReleaseId, approved.ExpectedSha256,
            request.TargetRoot, current.Id, current.StartTime.ToUniversalTime().Ticks, userSid, DateTime.UtcNow.AddSeconds(45));
        await DesktopMaintenanceProtocol.WriteAsync(pipe, ack, token);
        return new(null, null, request, approved);
    }

    internal static void ValidateDirectRequest(DesktopDirectMaintenanceRequest request, MaintenanceInvocation invocation,
        InstallerOptions options, string userSid)
    {
        DesktopMaintenanceProtocol.ValidateRequest(request, DateTime.UtcNow);
        LauncherInstallerService.ValidateDirectRelease(request.Release, options);
        if (request.Nonce != invocation.Nonce || request.UserSid != userSid || request.ParentProcessId == Environment.ProcessId)
            throw new InvalidDataException("Yêu cầu chuyển giao catalog sai người dùng hoặc nonce.");
        ValidateExistingTargetPath(request.TargetRoot);
    }

    private static void ValidateDirectParent(Process parent, DesktopDirectMaintenanceRequest request, string userSid)
    {
        if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != request.ParentStartTimeUtcTicks ||
            !PathEquals(parent.MainModule?.FileName, Path.Combine(request.TargetRoot, "TOOL-LOCAL.exe")))
            throw new InvalidDataException("Tiến trình desktop không khớp bản cài.");
        if (!OpenProcessToken(parent.Handle, 8, out var token)) throw new InvalidDataException("Không kiểm tra được chủ sở hữu desktop.");
        using (token) using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            if (identity.User?.Value != userSid) throw new InvalidDataException("Desktop và Setup phải cùng người dùng Windows.");
    }
}

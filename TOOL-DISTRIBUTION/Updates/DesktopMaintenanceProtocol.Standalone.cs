using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SHARED.Distribution.Updates;

public static partial class DesktopMaintenanceProtocol
{
    public static void ValidateRequest(DesktopStandaloneMaintenanceRequest request, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        DesktopStandaloneCatalogPolicy.ValidateRelease(request.Release);
        if (request.SchemaVersion != 3 || !IsHex(request.OperationId, 32) || !IsHex(request.Nonce, 64)
            || request.Action is not ("update" or "repair") || request.ParentProcessId <= 0 || request.ParentStartTimeUtcTicks <= 0
            || string.IsNullOrWhiteSpace(request.UserSid) || request.UserSid.Length > 200
            || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(request.CurrentVersion) || request.CurrentBuildNumber <= 0 || !IsHash(request.CurrentPackageSha256)
            || request.CreatedAtUtc is null || request.CreatedAtUtc.Value.Kind != DateTimeKind.Utc
            || request.CreatedAtUtc > nowUtc.AddSeconds(5) || request.CreatedAtUtc < nowUtc.AddMinutes(-1)
            || string.IsNullOrWhiteSpace(request.TargetRoot) || request.TargetRoot.Length > 160 || request.TargetRoot.Any(char.IsControl)
            || !Path.IsPathFullyQualified(request.TargetRoot) || request.TargetRoot.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("Standalone maintenance request is invalid or expired.");
        if (request.Action == "repair" && (request.CurrentVersion != request.Release.Version
            || request.CurrentBuildNumber != request.Release.DeclaredBuildNumber || request.CurrentPackageSha256 != request.Release.ExpectedSha256))
            throw new InvalidDataException("Repair must use the exact installed package.");
        if (request.Action == "update" && DesktopReleaseVersionPolicy.CompareVersions(request.Release.Version, request.CurrentVersion) <= 0)
            throw new InvalidDataException("A standalone catalog update requires a newer version.");
    }

    public static void ValidateAck(DesktopStandaloneMaintenanceRequest request, DesktopMaintenanceAck ack,
        int expectedSetupProcessId, long expectedSetupStartTicks, DateTime nowUtc)
    {
        if (ack.SchemaVersion != 3 || ack.OperationId != request.OperationId || ack.Nonce != request.Nonce
            || ack.ReleaseId != request.Release.ReleaseId || ack.PackageSha256 != request.Release.ExpectedSha256
            || !string.Equals(ack.TargetRoot, request.TargetRoot, StringComparison.OrdinalIgnoreCase) || ack.UserSid != request.UserSid
            || ack.SetupProcessId != expectedSetupProcessId || ack.SetupStartTimeUtcTicks != expectedSetupStartTicks
            || ack.ExpiresAtUtc.Kind != DateTimeKind.Utc || ack.ExpiresAtUtc <= nowUtc || ack.ExpiresAtUtc > nowUtc.AddMinutes(1))
            throw new InvalidDataException("Standalone maintenance acknowledgement does not match its owner.");
    }
}

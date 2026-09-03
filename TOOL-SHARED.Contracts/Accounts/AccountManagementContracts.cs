namespace TOOL_SHARED.Contracts.Accounts;

public sealed record RegisteredDeviceResponse(
    Guid DeviceId,
    string DeviceName,
    string? OperatingSystem,
    string? ApplicationVersion,
    bool IsTrusted,
    bool IsRevoked,
    bool IsCurrentDevice,
    DateTime FirstSeenAtUtc,
    DateTime LastSeenAtUtc);

public sealed record CurrentLicenseResponse(
    bool HasActiveLicense,
    Guid? UserLicenseId,
    string? PlanCode,
    string? PlanName,
    string? Status,
    DateTime? StartsAtUtc,
    DateTime? ExpiresAtUtc,
    int MaxActivatedDevices,
    int ActiveDeviceCount,
    int OfflineGraceHours,
    string? FeatureFlagsJson,
    bool CurrentDeviceActivated,
    DateTime ServerTimeUtc = default,
    DateTime? LeaseExpiresAtUtc = null,
    int HeartbeatIntervalSeconds = 300,
    string? AccessState = null,
    string? AccessReasonCode = null,
    string? AccessMessage = null);

public sealed record LicenseHeartbeatRequest(Guid SessionId);

public static class LicenseAccessStates
{
    public const string Active = "Active";
    public const string Missing = "Missing";
    public const string Expired = "Expired";
    public const string Suspended = "Suspended";
    public const string Revoked = "Revoked";
    public const string DeviceLimit = "DeviceLimit";
}

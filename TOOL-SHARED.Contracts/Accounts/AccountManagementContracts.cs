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
    int HeartbeatIntervalSeconds = 300);

public sealed record LicenseHeartbeatRequest(Guid SessionId);

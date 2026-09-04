namespace TOOL_SHARED.Contracts.Accounts;

public sealed record AdminLicenseOverviewResponse(
    int TotalUsers,
    int ActiveLicenses,
    int OnlineSessions,
    int ExpiringWithinSevenDays,
    int SuspendedOrRevokedLicenses);

public sealed record AdminLicensePlanResponse(
    Guid LicensePlanId,
    string PlanCode,
    string Name,
    string? Description,
    int MaxActivatedDevices,
    int MaxConcurrentSessions,
    int OfflineGraceHours,
    int? DefaultDurationDays,
    string? FeatureFlagsJson,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    decimal? SalePriceVnd = null,
    bool IsPublic = false,
    int DisplayOrder = 0,
    string? MarketingFeaturesJson = null);

public sealed record SaveLicensePlanRequest(
    string PlanCode,
    string Name,
    string? Description,
    int MaxActivatedDevices,
    int MaxConcurrentSessions,
    int OfflineGraceHours,
    int? DefaultDurationDays,
    string? FeatureFlagsJson,
    bool IsActive,
    decimal? SalePriceVnd = null,
    bool IsPublic = false,
    int DisplayOrder = 0,
    string? MarketingFeaturesJson = null);

public sealed record AdminUserLicenseResponse(
    Guid UserLicenseId,
    Guid LicensePlanId,
    string PlanCode,
    string PlanName,
    string Status,
    DateTime StartsAtUtc,
    DateTime? ExpiresAtUtc,
    int ActiveDeviceCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? RevokedAtUtc,
    string? RevokedReason);

public sealed record AdminUserSummaryResponse(
    string UserId,
    string Email,
    string? DisplayName,
    string AccountStatus,
    DateTime? LastLoginAtUtc,
    int RegisteredDeviceCount,
    int ActiveSessionCount,
    AdminUserLicenseResponse? CurrentLicense);

public sealed record AdminSessionResponse(
    Guid SessionId,
    Guid? DeviceId,
    string DeviceName,
    string Status,
    DateTime StartedAtUtc,
    DateTime LastSeenAtUtc,
    DateTime AbsoluteExpiresAtUtc,
    string? ApplicationVersion,
    string? IpAddress);

public sealed record AdminUserDetailResponse(
    AdminUserSummaryResponse User,
    IReadOnlyList<AdminUserLicenseResponse> Licenses,
    IReadOnlyList<RegisteredDeviceResponse> Devices,
    IReadOnlyList<AdminSessionResponse> Sessions);

public sealed record AdminLicensePaymentResponse(
    Guid LicensePaymentId,
    string UserId,
    string UserEmail,
    string OrderCode,
    string TransferCode,
    long? ProviderTransactionId,
    Guid LicensePlanId,
    string PlanCode,
    string PlanName,
    decimal AmountVnd,
    int DurationDays,
    string Status,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? PaidAtUtc,
    DateTime? FulfilledAtUtc,
    Guid? FulfilledUserLicenseId,
    Guid? AssignedOrganizationId = null,
    string? AssignedOrganizationName = null,
    string? ProvisioningStatus = null,
    string? FailureCode = null);

public sealed record GrantUserLicenseRequest(
    Guid LicensePlanId,
    DateTime? StartsAtUtc,
    DateTime? ExpiresAtUtc,
    int? DurationDays,
    bool IsTrial = false);

public sealed record ExtendUserLicenseRequest(int DurationDays);

public sealed record ChangeUserLicenseStatusRequest(string Status, string? Reason);

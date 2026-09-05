namespace TOOL_SHARED.Contracts.Organizations;

public sealed record OrganizationPoolSummaryResponse(
    Guid OrganizationPoolId,
    string Code,
    string Name,
    string AllocationStrategy,
    string Status,
    int OrganizationCount,
    int LicensePlanCount,
    int SeatCapacity,
    int ActiveSeats,
    int ReservedSeats,
    int AvailableSeats,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int AllocatableOrganizationCount = 0,
    int ActiveLicensePlanCount = 0,
    int AllocatableSeatCapacity = 0,
    int AllocatableAvailableSeats = 0);

public sealed record SaveOrganizationPoolRequest(
    string Code,
    string Name,
    string Status = "Active");

public sealed record OrganizationPoolOrganizationResponse(
    Guid OrganizationPoolId,
    Guid OrganizationId,
    string OrganizationCode,
    string OrganizationName,
    string OrganizationStatus,
    int SeatCapacity,
    int ActiveSeats,
    int ReservedSeats,
    int AvailableSeats,
    int Priority,
    bool IsAutoAssignmentEnabled,
    bool IsReady,
    string? ReadinessMessage,
    DateTime UpdatedAtUtc,
    bool CanReceiveCustomers = false,
    int AllocatableAvailableSeats = 0);

public sealed record SaveOrganizationPoolOrganizationRequest(
    Guid OrganizationId,
    int SeatCapacity,
    int Priority = 100,
    bool IsAutoAssignmentEnabled = false,
    bool IsReady = false,
    string? ReadinessMessage = null);

public sealed record LicensePlanOrganizationPoolResponse(
    Guid LicensePlanId,
    string PlanCode,
    string PlanName,
    Guid OrganizationPoolId,
    string OrganizationPoolCode,
    string OrganizationPoolName,
    decimal? DefaultMemberMonthlyBudgetLimit,
    bool IsActive,
    DateTime UpdatedAtUtc,
    bool PlanIsActive = false,
    bool PlanIsPublic = false,
    bool IsSellable = false);

public sealed record SaveLicensePlanOrganizationPoolRequest(
    Guid OrganizationPoolId,
    decimal? DefaultMemberMonthlyBudgetLimit = null,
    bool IsActive = true);

public sealed record OrganizationSeatAssignmentResponse(
    Guid OrganizationSeatAssignmentId,
    Guid OrganizationPoolId,
    string OrganizationPoolCode,
    Guid OrganizationId,
    string OrganizationCode,
    string OrganizationName,
    string UserId,
    string UserEmail,
    Guid LicensePlanId,
    string PlanCode,
    Guid LicensePaymentId,
    string OrderCode,
    Guid? UserLicenseId,
    string Status,
    bool ConsumesSeat,
    bool MembershipManaged,
    DateTime ReservedAtUtc,
    DateTime ReservationExpiresAtUtc,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    DateTime? ActivatedAtUtc,
    DateTime? ReleasedAtUtc,
    string? ReleaseReason,
    string? FailureCode,
    DateTime UpdatedAtUtc,
    string? PaymentStatus = null);

public sealed record OrganizationPoolDetailResponse(
    OrganizationPoolSummaryResponse Pool,
    IReadOnlyList<OrganizationPoolOrganizationResponse> Organizations,
    IReadOnlyList<LicensePlanOrganizationPoolResponse> LicensePlans,
    IReadOnlyList<OrganizationSeatAssignmentResponse> RecentAssignments);

public sealed record RetryOrganizationSeatAssignmentResponse(
    Guid LicensePaymentId,
    string PaymentStatus,
    OrganizationSeatAssignmentResponse? Assignment,
    string Message);

public static class OrganizationProvisioningErrorCodes
{
    public const string PlanPoolNotConfigured = "license_plan_pool_not_configured";
    public const string CapacityUnavailable = "organization_capacity_unavailable";
    public const string OrganizationNotReady = "organization_not_ready";
    public const string ProvisioningPending = "organization_provisioning_pending";
}

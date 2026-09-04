namespace TOOL_SERVER.Domain.Organizations;

public sealed class OrganizationPool
{
    public Guid OrganizationPoolId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string AllocationStrategy { get; set; } = OrganizationPoolAllocationStrategies.PriorityBalanced;
    public string Status { get; set; } = OrganizationPoolStatuses.Active;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class OrganizationPoolOrganization
{
    public Guid OrganizationPoolId { get; set; }
    public Guid OrganizationId { get; set; }
    public int SeatCapacity { get; set; }
    public int ActiveSeatCount { get; set; }
    public int ReservedSeatCount { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsAutoAssignmentEnabled { get; set; }
    public bool IsReady { get; set; }
    public string? ReadinessMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class LicensePlanOrganizationPool
{
    public Guid LicensePlanId { get; set; }
    public Guid OrganizationPoolId { get; set; }
    public decimal? DefaultMemberMonthlyBudgetLimit { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class OrganizationSeatAssignment
{
    public Guid OrganizationSeatAssignmentId { get; set; }
    public Guid OrganizationPoolId { get; set; }
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = null!;
    public Guid LicensePlanId { get; set; }
    public Guid LicensePaymentId { get; set; }
    public Guid? UserLicenseId { get; set; }
    public string Status { get; set; } = OrganizationSeatAssignmentStatuses.Reserved;
    public bool ConsumesSeat { get; set; } = true;
    public bool MembershipManaged { get; set; } = true;
    public DateTime ReservedAtUtc { get; set; }
    public DateTime ReservationExpiresAtUtc { get; set; }
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public string? ReleaseReason { get; set; }
    public string? FailureCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class OrganizationPoolStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
}

public static class OrganizationPoolAllocationStrategies
{
    public const string PriorityBalanced = "PriorityBalanced";
}

public static class OrganizationSeatAssignmentStatuses
{
    public const string Reserved = "Reserved";
    public const string Scheduled = "Scheduled";
    public const string Active = "Active";
    public const string Released = "Released";
    public const string Failed = "Failed";

    public static bool OccupiesCapacity(string status) =>
        status is Reserved or Scheduled or Active;
}

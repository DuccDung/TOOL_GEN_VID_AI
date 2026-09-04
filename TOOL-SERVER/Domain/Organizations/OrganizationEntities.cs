namespace TOOL_SERVER.Domain.Organizations;

public sealed class Organization
{
    public Guid OrganizationId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Status { get; set; } = OrganizationStatuses.Active;
    public decimal MonthlyBudgetLimit { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public string CreatedByUserId { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<OrganizationMember> Members { get; set; } = [];
}

public sealed class OrganizationMember
{
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = null!;
    public string Role { get; set; } = OrganizationMemberRoles.Member;
    public string Status { get; set; } = OrganizationMemberStatuses.Active;
    public bool IsProvisioningManaged { get; set; }
    public decimal? MonthlyBudgetLimit { get; set; }
    public DateTime JoinedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Organization Organization { get; set; } = null!;
}

public sealed class OrganizationProviderCredential
{
    public Guid OrganizationProviderCredentialId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProviderId { get; set; }
    public int Version { get; set; }
    public string Name { get; set; } = null!;
    public string EncryptedPayload { get; set; } = null!;
    public string SecretHint { get; set; } = null!;
    public string Status { get; set; } = ProviderCredentialStatuses.Active;
    public string CreatedByUserId { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class OrganizationVideoPolicy
{
    public Guid OrganizationId { get; set; }
    public string PolicyScope { get; set; } = "Default";
    public Guid ProviderId { get; set; }
    public Guid ProviderModelId { get; set; }
    public int PolicyVersion { get; set; }
    public string Resolution { get; set; } = "720p";
    public bool NativeAudio { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string UpdatedByUserId { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class OrganizationBudgetPeriod
{
    public Guid OrganizationBudgetPeriodId { get; set; }
    public Guid OrganizationId { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public decimal HardLimit { get; set; }
    public decimal ReservedCost { get; set; }
    public decimal ActualCost { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class AiBudgetReservation
{
    public Guid AiBudgetReservationId { get; set; }
    public Guid OrganizationBudgetPeriodId { get; set; }
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = null!;
    public Guid ProjectId { get; set; }
    public Guid ProviderRequestId { get; set; }
    public string OperationKey { get; set; } = null!;
    public string ProviderCode { get; set; } = null!;
    public string ModelCode { get; set; } = null!;
    public decimal ReservedAmount { get; set; }
    public decimal ActualAmount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public string Status { get; set; } = BudgetReservationStatuses.Reserved;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? SettledAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class AiUsageLedgerEntry
{
    public Guid AiUsageLedgerEntryId { get; set; }
    public Guid OrganizationBudgetPeriodId { get; set; }
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = null!;
    public Guid ProjectId { get; set; }
    public Guid? ProviderRequestId { get; set; }
    public Guid? OrganizationProviderCredentialId { get; set; }
    public string ProviderCode { get; set; } = null!;
    public string ModelCode { get; set; } = null!;
    public string EntryKind { get; set; } = null!;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public string? UsageJson { get; set; }
    public string? RateSnapshotJson { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class OrganizationAuditLog
{
    public long OrganizationAuditLogId { get; set; }
    public Guid OrganizationId { get; set; }
    public string? ActorUserId { get; set; }
    public string EventType { get; set; } = null!;
    public string? DataJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public static class OrganizationStatuses
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
    public const string Archived = "Archived";
}

public static class OrganizationMemberStatuses
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
    public const string Removed = "Removed";
}

public static class OrganizationMemberRoles
{
    public const string Owner = "Owner";
    public const string OrganizationAdmin = "OrganizationAdmin";
    public const string BillingManager = "BillingManager";
    public const string Member = "Member";
    public const string Viewer = "Viewer";

    public static bool CanManageMembers(string role) => role is Owner or OrganizationAdmin;
    public static bool CanManageBilling(string role) => role is Owner or OrganizationAdmin or BillingManager;
    public static bool CanManageCredentials(string role) => role is Owner or OrganizationAdmin;
    public static bool CanGenerate(string role) => role is Owner or OrganizationAdmin or BillingManager or Member;
}

public static class ProviderCredentialStatuses
{
    public const string Active = "Active";
    public const string Retiring = "Retiring";
    public const string Revoked = "Revoked";
}

public static class BudgetReservationStatuses
{
    public const string Reserved = "Reserved";
    public const string Settled = "Settled";
    public const string Released = "Released";
    public const string Expired = "Expired";
}

public static class UsageLedgerEntryKinds
{
    public const string Reservation = "Reservation";
    public const string Actual = "Actual";
    public const string Release = "Release";
    public const string Adjustment = "Adjustment";
    public const string Refund = "Refund";
}

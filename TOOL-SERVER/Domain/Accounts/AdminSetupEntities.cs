namespace TOOL_SERVER.Domain.Accounts;

public sealed class AdminSetupDraftEntity
{
    public Guid DraftId { get; set; }
    public string ActorUserId { get; set; } = null!;
    public Guid? OrganizationId { get; set; }
    public Guid? PoolId { get; set; }
    public string State { get; set; } = "Draft";
    public int Step { get; set; } = 1;
    public long Revision { get; set; } = 1;
    public string FormJson { get; set; } = "{}";
    public string? ConfigurationFingerprint { get; set; }
    public Guid? PlanId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class AdminSetupOperationEntity
{
    public Guid OperationId { get; set; }
    public Guid DraftId { get; set; }
    public string ActorUserId { get; set; } = null!;
    public string Command { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public string State { get; set; } = "Running";
    public Guid? ResultId { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

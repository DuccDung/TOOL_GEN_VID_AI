namespace TOOL_SERVER.Domain.Accounts;

public sealed class UserLicense
{
    public Guid UserLicenseId { get; set; }
    public string UserId { get; set; } = null!;
    public Guid LicensePlanId { get; set; }
    public byte[]? LicenseKeyHash { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime StartsAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? EntitlementSnapshotJson { get; set; }
    public string? GrantedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ApplicationUser User { get; set; } = null!;
    public ApplicationUser? GrantedByUser { get; set; }
    public LicensePlan LicensePlan { get; set; } = null!;
    public ICollection<LicenseActivation> Activations { get; set; } = [];
}

namespace TOOL_SERVER.Domain.Accounts;

public sealed class LicensePlan
{
    public Guid LicensePlanId { get; set; }
    public string PlanCode { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int MaxActivatedDevices { get; set; }
    public int OfflineGraceHours { get; set; }
    public int? DefaultDurationDays { get; set; }
    public string? FeatureFlagsJson { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<UserLicense> UserLicenses { get; set; } = [];
}

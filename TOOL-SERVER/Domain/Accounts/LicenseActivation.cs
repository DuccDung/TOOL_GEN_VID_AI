namespace TOOL_SERVER.Domain.Accounts;

public sealed class LicenseActivation
{
    public Guid LicenseActivationId { get; set; }
    public Guid UserLicenseId { get; set; }
    public Guid DeviceId { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime ActivatedAtUtc { get; set; }
    public DateTime LastVerifiedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public UserLicense UserLicense { get; set; } = null!;
    public RegisteredDevice Device { get; set; } = null!;
}

using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class UserLicense
{
    public Guid UserLicenseId { get; set; }

    public string UserId { get; set; } = null!;

    public Guid LicensePlanId { get; set; }

    public byte[]? LicenseKeyHash { get; set; }

    public string Status { get; set; } = null!;

    public DateTime StartsAtUtc { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public string? EntitlementSnapshotJson { get; set; }

    public string? GrantedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public string? RevokedReason { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual AspNetUser? GrantedByUser { get; set; }

    public virtual ICollection<LicenseActivation> LicenseActivations { get; set; } = new List<LicenseActivation>();

    public virtual LicensePlan LicensePlan { get; set; } = null!;

    public virtual AspNetUser User { get; set; } = null!;
}

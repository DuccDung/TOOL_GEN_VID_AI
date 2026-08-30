using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class LicenseActivation
{
    public Guid LicenseActivationId { get; set; }

    public Guid UserLicenseId { get; set; }

    public Guid DeviceId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime ActivatedAtUtc { get; set; }

    public DateTime LastVerifiedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public string? RevokedReason { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual RegisteredDevice Device { get; set; } = null!;

    public virtual UserLicense UserLicense { get; set; } = null!;
}

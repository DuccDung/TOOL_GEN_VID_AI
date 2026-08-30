using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class VwUserAccountSummary
{
    public string UserId { get; set; } = null!;

    public string? UserName { get; set; }

    public string? Email { get; set; }

    public bool EmailConfirmed { get; set; }

    public string? DisplayName { get; set; }

    public string AccountStatus { get; set; } = null!;

    public DateTime? LastLoginAtUtc { get; set; }

    public long RegisteredDeviceCount { get; set; }

    public long ActiveDeviceCount { get; set; }

    public long ActiveSessionCount { get; set; }

    public long ActiveLicenseCount { get; set; }

    public DateTime? NearestLicenseExpiryUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

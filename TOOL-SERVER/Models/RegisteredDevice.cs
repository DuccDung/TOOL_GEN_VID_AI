using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class RegisteredDevice
{
    public Guid DeviceId { get; set; }

    public string UserId { get; set; } = null!;

    public byte[] DeviceFingerprintHash { get; set; } = null!;

    public string DeviceName { get; set; } = null!;

    public string? OperatingSystem { get; set; }

    public string? ApplicationVersion { get; set; }

    public bool IsTrusted { get; set; }

    public bool IsRevoked { get; set; }

    public string? RevokedReason { get; set; }

    public DateTime FirstSeenAtUtc { get; set; }

    public DateTime LastSeenAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual ICollection<LicenseActivation> LicenseActivations { get; set; } = new List<LicenseActivation>();

    public virtual ICollection<Project> Projects { get; set; } = new List<Project>();

    public virtual AspNetUser User { get; set; } = null!;

    public virtual ICollection<UserSession> UserSessions { get; set; } = new List<UserSession>();
}

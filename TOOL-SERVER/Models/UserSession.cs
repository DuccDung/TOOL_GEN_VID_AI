using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class UserSession
{
    public Guid SessionId { get; set; }

    public string UserId { get; set; } = null!;

    public Guid? DeviceId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime StartedAtUtc { get; set; }

    public DateTime LastSeenAtUtc { get; set; }

    public DateTime AbsoluteExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public string? RevokedReason { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? ApplicationVersion { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual RegisteredDevice? Device { get; set; }

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public virtual AspNetUser User { get; set; } = null!;
}

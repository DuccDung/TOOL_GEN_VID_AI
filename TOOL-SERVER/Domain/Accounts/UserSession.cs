namespace TOOL_SERVER.Domain.Accounts;

public sealed class UserSession
{
    public Guid SessionId { get; set; }

    public string UserId { get; set; } = null!;

    public Guid? DeviceId { get; set; }

    public string Status { get; set; } = SessionStatuses.Active;

    public DateTime StartedAtUtc { get; set; }

    public DateTime LastSeenAtUtc { get; set; }

    public DateTime AbsoluteExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public string? RevokedReason { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? ApplicationVersion { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public ApplicationUser User { get; set; } = null!;

    public RegisteredDevice? Device { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}

public static class SessionStatuses
{
    public const string Active = "Active";
    public const string Revoked = "Revoked";
    public const string Expired = "Expired";
}

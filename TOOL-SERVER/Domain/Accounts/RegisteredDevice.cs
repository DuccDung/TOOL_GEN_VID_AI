namespace TOOL_SERVER.Domain.Accounts;

public sealed class RegisteredDevice
{
    public Guid DeviceId { get; set; }

    public string UserId { get; set; } = null!;

    public byte[] DeviceFingerprintHash { get; set; } = [];

    public string DeviceName { get; set; } = null!;

    public string? OperatingSystem { get; set; }

    public string? ApplicationVersion { get; set; }

    public bool IsTrusted { get; set; }

    public bool IsRevoked { get; set; }

    public string? RevokedReason { get; set; }

    public DateTime FirstSeenAtUtc { get; set; }

    public DateTime LastSeenAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public ApplicationUser User { get; set; } = null!;

    public ICollection<UserSession> Sessions { get; set; } = [];
}

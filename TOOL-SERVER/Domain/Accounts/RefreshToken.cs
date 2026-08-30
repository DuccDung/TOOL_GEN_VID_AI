namespace TOOL_SERVER.Domain.Accounts;

public sealed class RefreshToken
{
    public Guid RefreshTokenId { get; set; }

    public string UserId { get; set; } = null!;

    public Guid SessionId { get; set; }

    public Guid TokenFamilyId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public string? TokenPrefix { get; set; }

    public string? JwtId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? UsedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public string? RevokedReason { get; set; }

    public Guid? ReplacedByTokenId { get; set; }

    public string? CreatedByIpAddress { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public UserSession Session { get; set; } = null!;

    public RefreshToken? ReplacedByToken { get; set; }
}

namespace TOOL_SERVER.TikTok.Domain;

public sealed class TikTokConnection
{
    public Guid TikTokConnectionId { get; set; }
    public string UserId { get; set; } = null!;
    public string OpenId { get; set; } = null!;
    public string CreatorUsername { get; set; } = string.Empty;
    public string CreatorNickname { get; set; } = string.Empty;
    public string Scopes { get; set; } = null!;
    public string ProtectedAccessToken { get; set; } = null!;
    public string ProtectedRefreshToken { get; set; } = null!;
    public DateTime AccessTokenExpiresAtUtc { get; set; }
    public DateTime RefreshTokenExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class TikTokOAuthSession
{
    public Guid TikTokOAuthSessionId { get; set; }
    public Guid? TikTokAppCredentialId { get; set; }
    public string UserId { get; set; } = null!;
    public Guid DeviceId { get; set; }
    public byte[] StateHash { get; set; } = [];
    public string CodeChallenge { get; set; } = null!;
    public string RedirectUri { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class TikTokAppCredential
{
    public Guid TikTokAppCredentialId { get; set; }
    public int Version { get; set; }
    public string ProtectedPayload { get; set; } = null!;
    public string ClientKeyHint { get; set; } = null!;
    public string SecretHint { get; set; } = null!;
    public string Status { get; set; } = TikTokAppCredentialStatuses.Pending;
    public string CreatedByUserId { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? VerificationRequestedByUserId { get; set; }
    public DateTime? VerificationExpiresAtUtc { get; set; }
    public DateTime? LastTestedAtUtc { get; set; }
    public string? LastTestFailureCode { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class TikTokIntegrationSetting
{
    public byte TikTokIntegrationSettingId { get; set; } = 1;
    public bool Enabled { get; set; }
    public bool AuditedForPublicPosting { get; set; }
    public string? AuditEvidence { get; set; }
    public string? UpdatedByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class TikTokAppCredentialStatuses
{
    public const string Pending = "Pending";
    public const string Active = "Active";
    public const string Retiring = "Retiring";
    public const string Revoked = "Revoked";
}

public sealed class TikTokPublishJob
{
    public Guid TikTokPublishJobId { get; set; }
    public Guid TikTokConnectionId { get; set; }
    public string UserId { get; set; } = null!;
    public Guid ClientRequestId { get; set; }
    public string TikTokPublishId { get; set; } = null!;
    public string? ProtectedUploadUrl { get; set; }
    public DateTime UploadUrlExpiresAtUtc { get; set; }
    public long VideoSizeBytes { get; set; }
    public long ChunkSizeBytes { get; set; }
    public int TotalChunkCount { get; set; }
    public string Status { get; set; } = TikTokPublishStatuses.Uploading;
    public string? FailureReason { get; set; }
    public long UploadedBytes { get; set; }
    public string PublicPostIds { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class TikTokPublishStatuses
{
    public const string Uploading = "PROCESSING_UPLOAD";
    public const string Complete = "PUBLISH_COMPLETE";
    public const string Failed = "FAILED";

    public static bool IsTerminal(string status) =>
        status is Complete or Failed;
}

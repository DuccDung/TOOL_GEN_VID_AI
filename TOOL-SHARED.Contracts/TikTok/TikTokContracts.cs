namespace TOOL_SHARED.Contracts.TikTok;

public sealed record TikTokFeatureStateResponse(
    bool Enabled,
    bool Configured,
    TikTokConnectionSummary? Connection,
    TikTokPublishStatusResponse? ActivePublish = null,
    bool IsCredentialVerification = false,
    string? UnavailableReason = null,
    IReadOnlyList<TikTokConnectionSummary>? Connections = null,
    IReadOnlyList<TikTokPublishStatusResponse>? ActivePublishes = null,
    bool MultiAccountEnabled = false,
    Guid? ConnectedConnectionId = null,
    IReadOnlyList<TikTokPublishStatusResponse>? RecentPublishes = null,
    IReadOnlyList<TikTokPublishAttemptSummary>? UnresolvedAttempts = null);

public sealed record TikTokPublishAttemptSummary(Guid ClientRequestId, Guid ConnectionId, string Status, DateTime CreatedAtUtc);

public static class TikTokUnavailableReasons
{
    public const string EmergencyDisabled = "emergency_disabled";
    public const string VerificationOtherAccount = "verification_other_account";
    public const string VerificationExpired = "verification_expired";
    public const string SetupRequired = "setup_required";
    public const string IntegrationDisabled = "integration_disabled";
}

public sealed record TikTokConnectionSummary(
    Guid ConnectionId,
    string CreatorUsername,
    string CreatorNickname,
    IReadOnlyList<string> Scopes,
    DateTime AccessTokenExpiresAtUtc,
    DateTime RefreshTokenExpiresAtUtc,
    DateTime UpdatedAtUtc,
    string Status = "Connected",
    string? AvatarUrl = null);

public sealed record StartTikTokOAuthRequest(
    string RedirectUri,
    string CodeChallenge,
    Guid? TargetConnectionId = null,
    bool MultiAccount = false);

public sealed record StartTikTokOAuthResponse(
    Guid OAuthSessionId,
    string AuthorizationUrl,
    string State,
    DateTime ExpiresAtUtc);

public sealed record CompleteTikTokOAuthRequest(
    Guid OAuthSessionId,
    string Code,
    string State,
    string CodeVerifier);

public sealed record TikTokCreatorInfoResponse(
    string CreatorUsername,
    string CreatorNickname,
    IReadOnlyList<string> PrivacyLevelOptions,
    bool CommentDisabled,
    bool DuetDisabled,
    bool StitchDisabled,
    int MaximumVideoDurationSeconds,
    TikTokPublishingIssue? PublishingIssue = null,
    Guid? ConnectionId = null,
    string? AvatarUrl = null);

public sealed record TikTokPublishingIssue(string Code, string Message);

public sealed record InitializeTikTokPublishRequest(
    Guid ClientRequestId,
    string Title,
    string PrivacyLevel,
    bool DisableComment,
    bool DisableDuet,
    bool DisableStitch,
    bool BrandContent,
    bool BrandOrganic,
    bool IsAiGenerated,
    long VideoSizeBytes,
    decimal VideoDurationSeconds,
    string MimeType,
    Guid? ConnectionId = null);

// When BlockedCreator is present, no job or upload has been initialized;
// consumers must show its PublishingIssue and ignore the upload fields.
public sealed record InitializeTikTokPublishResponse(
    Guid PublishJobId,
    string UploadUrl,
    long ChunkSizeBytes,
    int TotalChunkCount,
    DateTime UploadUrlExpiresAtUtc,
    TikTokCreatorInfoResponse? BlockedCreator = null,
    Guid? ConnectionId = null);

public sealed record TikTokPublishStatusResponse(
    Guid PublishJobId,
    string Status,
    string? FailureReason,
    long UploadedBytes,
    IReadOnlyList<string> PublicPostIds,
    DateTime UpdatedAtUtc,
    bool IsTerminal,
    Guid? ConnectionId = null,
    string? CreatorUsername = null,
    string? CreatorNickname = null,
    DateTime? CreatedAtUtc = null);

public sealed record TikTokPublishHistoryResponse(
    IReadOnlyList<TikTokPublishStatusResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record TikTokAdminStateResponse(
    bool AdminManagementEnabled,
    bool IntegrationEnabled,
    bool AuditedForPublicPosting,
    string? AuditEvidence,
    string RequiredRedirectUri,
    IReadOnlyList<string> Scopes,
    int ConnectedUserCount,
    int PendingPublishJobCount,
    IReadOnlyList<TikTokAdminCredentialSummary> Credentials,
    int ConnectedAccountCount = 0);

public sealed record TikTokAdminCredentialSummary(
    Guid CredentialId,
    int Version,
    string ClientKeyHint,
    string SecretHint,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? VerificationExpiresAtUtc,
    bool VerificationRequestedByCurrentAdmin,
    DateTime? LastTestedAtUtc,
    string? LastTestFailureCode,
    DateTime? ActivatedAtUtc,
    DateTime? RevokedAtUtc);

public sealed record SaveTikTokAdminCredentialRequest(
    string ClientKey,
    string ClientSecret);

public sealed record UpdateTikTokAdminSettingsRequest(
    bool Enabled,
    bool AuditedForPublicPosting,
    bool ConfirmAuditApproval,
    string? AuditEvidence);

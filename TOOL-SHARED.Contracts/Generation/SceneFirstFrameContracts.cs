namespace TOOL_SHARED.Contracts.Generation;

public static class SceneFirstFrameStatuses
{
    public const string PendingReview = "PendingReview";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Superseded = "Superseded";
    public const string Invalidated = "Invalidated";
}

public sealed record SceneFirstFrameCharacterInput(
    Guid CharacterReferenceId,
    string MimeType,
    string Base64Data,
    string Sha256);

public sealed record GenerateSceneFirstFrameRequest(
    Guid ProjectId,
    Guid SceneId,
    int ScenePlanVersion,
    int ScenePromptVersion,
    string IdempotencyKey,
    Guid? OrganizationId = null,
    SceneFirstFrameCharacterInput? CharacterReference = null,
    int Attempt = 1);

public sealed record GenerateSceneFirstFrameResponse(
    Guid ProviderRequestId,
    string ProviderCode,
    string ModelCode,
    string ContentUrl,
    string MimeType,
    string Sha256,
    int Width,
    int Height,
    long SizeBytes,
    long InputTokens,
    long OutputTokens,
    decimal ActualCost,
    string CurrencyCode,
    DateTime ExpiresAtUtc);

public sealed record SceneFirstFrameQuoteResponse(
    string ProviderCode,
    string ModelCode,
    string AspectRatio,
    int Width,
    int Height,
    decimal EstimatedCost,
    string CurrencyCode,
    Guid? SourceCharacterReferenceId,
    string? SourceCharacterName,
    int ScenePlanVersion,
    Guid ScenePromptId,
    int ScenePromptVersion);

public sealed record MaterializeSceneFirstFrameRequest(
    Guid ProviderRequestId,
    string RelativePath,
    string MimeType,
    string Sha256,
    long SizeBytes,
    int Width,
    int Height,
    Guid? OrganizationId = null);

public sealed record ChangeSceneFirstFrameStatusRequest(
    string RowVersion,
    Guid? OrganizationId = null);

public sealed record SceneFirstFrameSummary(
    Guid SceneFirstFrameId,
    Guid SceneId,
    Guid MediaAssetId,
    Guid ProviderRequestId,
    int Version,
    string Status,
    Guid? SourceCharacterReferenceId,
    int ScenePlanVersion,
    Guid ScenePromptId,
    int ScenePromptVersion,
    string AspectRatio,
    string PromptTemplateVersion,
    string RelativePath,
    string MimeType,
    string Sha256,
    long SizeBytes,
    int Width,
    int Height,
    bool IsCurrent,
    string? StaleReason,
    string RowVersion,
    DateTime CreatedAtUtc,
    DateTime? ApprovedAtUtc,
    DateTime? InvalidatedAtUtc,
    string? PreviewUrl = null);

public sealed record SceneFirstFrameListResponse(
    Guid ProjectId,
    Guid SceneId,
    IReadOnlyList<SceneFirstFrameSummary> Frames);

public sealed record ProjectSceneFirstFrameListResponse(
    Guid ProjectId,
    IReadOnlyList<SceneFirstFrameSummary> Frames);

public sealed record SceneFirstFrameInput(
    Guid SceneFirstFrameId,
    string MimeType,
    string Base64Data,
    string Sha256);

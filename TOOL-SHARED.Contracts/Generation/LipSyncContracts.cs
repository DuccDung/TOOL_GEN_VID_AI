namespace TOOL_SHARED.Contracts.Generation;

public static class LipSyncInputKinds
{
    public const string Video = "Video";
    public const string Audio = "Audio";
}

public static class LipSyncStatuses
{
    public const string Uploading = "Uploading";
    public const string Ready = "Ready";
    public const string Submitting = "Submitting";
    public const string Submitted = "Submitted";
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Unknown = "Unknown";
    public const string Completed = "Completed";
    public const string Downloading = "Downloading";
    public const string ReviewRequired = "ReviewRequired";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Expired = "Expired";
}

public static class LipSyncErrorCodes
{
    public const string Disabled = "lip_sync_disabled";
    public const string NotConfigured = "lip_sync_not_configured";
    public const string NotApplicable = "lip_sync_not_applicable";
    public const string InputChanged = "lip_sync_input_changed";
    public const string InputNotReady = "lip_sync_input_not_ready";
    public const string InputExpired = "lip_sync_input_expired";
    public const string PricingNotConfigured = "pricing_not_configured";
    public const string ReviewRequired = "lip_sync_review_required";
}

public sealed record CreateLipSyncInputSessionRequest(
    Guid ProjectId,
    Guid SceneId,
    int ScenePlanVersion,
    Guid VideoGenerationId,
    Guid VoiceGenerationId,
    string VideoSha256,
    string AudioSha256,
    string PreparedVideoSha256,
    string PreparedAudioSha256,
    long DurationMs,
    Guid? OrganizationId = null);

public sealed record LipSyncInputSessionResponse(
    Guid LipSyncInputSessionId,
    Guid ProjectId,
    Guid SceneId,
    Guid VideoGenerationId,
    Guid VoiceGenerationId,
    string Status,
    string VideoUploadUrl,
    string AudioUploadUrl,
    string ProviderCode,
    string ModelCode,
    decimal EstimatedCost,
    string CurrencyCode,
    long DurationMs,
    DateTime ExpiresAtUtc,
    bool VideoUploaded,
    bool AudioUploaded);

public sealed record LipSyncInputUploadResponse(
    Guid LipSyncInputSessionId,
    string InputKind,
    string Status,
    string Sha256,
    long SizeBytes,
    bool VideoUploaded,
    bool AudioUploaded);

public sealed record SubmitLipSyncRequest(
    Guid ProjectId,
    Guid SceneId,
    Guid LipSyncInputSessionId,
    string IdempotencyKey,
    Guid? OrganizationId = null);

public sealed record LipSyncTaskResponse(
    Guid LipSyncGenerationId,
    Guid ProviderRequestId,
    Guid LipSyncInputSessionId,
    Guid VideoGenerationId,
    Guid VoiceGenerationId,
    string ProviderCode,
    string ModelCode,
    string Status,
    decimal ProgressPercent,
    string? OutputUrl,
    string? ErrorCode,
    string? ErrorMessage,
    decimal EstimatedCost,
    decimal ActualCost,
    string CurrencyCode,
    long DurationMs,
    string VideoSha256,
    string AudioSha256,
    string PreparedVideoSha256,
    string PreparedAudioSha256,
    string? RowVersion = null);

public sealed record MaterializeLipSyncOutputRequest(
    Guid ProjectId,
    Guid SceneId,
    Guid LipSyncGenerationId,
    Guid OutputMediaAssetId,
    string OutputSha256,
    long DurationMs,
    Guid? OrganizationId = null);

public sealed record ReviewLipSyncOutputRequest(
    Guid ProjectId,
    Guid SceneId,
    Guid LipSyncGenerationId,
    string ExpectedRowVersion,
    bool PlaybackConfirmed,
    Guid? OrganizationId = null,
    string? Reason = null);

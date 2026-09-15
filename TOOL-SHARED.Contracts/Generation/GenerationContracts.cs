namespace TOOL_SHARED.Contracts.Generation;

public static class KlingSpeechModes
{
    public const string None = "None";
    public const string OnCameraDialogue = "OnCameraDialogue";
    public const string NativeVoiceOver = "NativeVoiceOver";
}

public static class SpeechProductionPolicies
{
    public const string ProviderNativeVerified = "ProviderNativeVerified";
    public const string CanonicalVoice = "CanonicalVoice";

    public static bool IsSupported(string? value) =>
        value is ProviderNativeVerified or CanonicalVoice;
}

public static class SpeechVerificationStatuses
{
    public const string NotRequested = "NotRequested";
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Passed = "Passed";
    public const string NeedsReview = "NeedsReview";
    public const string Failed = "Failed";
}

public static class SceneSpeechStatuses
{
    public const string SpeechNotRequired = "SpeechNotRequired";
    public const string SpeechMissing = "SpeechMissing";
    public const string SpeechGenerating = "SpeechGenerating";
    public const string SpeechVerificationRequired = "SpeechVerificationRequired";
    public const string SpeechReviewRequired = "SpeechReviewRequired";
    public const string SpeechApproved = "SpeechApproved";
    // Read-only compatibility with projects saved before cloud lip-sync was removed.
    // Approval is always revalidated from the current VoiceGeneration, never from this status.
    public const string LegacySpeechReadyForLipSync = "SpeechReadyForLipSync";
    public const string SpeechInvalid = "SpeechInvalid";
}

public static class VoiceProfileScopes
{
    public const string ProjectNarrator = "ProjectNarrator";
    public const string Character = "Character";
}

public static class VoiceProfileVersionStatuses
{
    public const string Draft = "Draft";
    public const string Approved = "Approved";
    public const string Superseded = "Superseded";
    public const string Revoked = "Revoked";
}

public static class SpeechMixStrategies
{
    public const string ReplaceAllNativeAudio = "ReplaceAllNativeAudio";
    public const string MixWithVerifiedAmbience = "MixWithVerifiedAmbience";
}

public static class SpeechSynchronizationErrorCodes
{
    public const string SpeechNotRequired = "speech_not_required";
    public const string SceneSpeechChanged = "scene_speech_changed";
    public const string VoiceProfileMissing = "voice_profile_missing";
    public const string VoiceVersionNotApproved = "voice_version_not_approved";
    public const string VoicePreviewRequired = "voice_preview_required";
    public const string SceneVoiceNotApproved = "scene_voice_not_approved";
    public const string SpeechDurationOutOfRange = "speech_duration_out_of_range";
    public const string SpeechVerificationNotRequired = "speech_verification_not_required";
    public const string SpeechVerificationFailed = "speech_verification_failed";
    public const string SpeechVerificationReviewRequired = "speech_verification_review_required";
    public const string SpeechVerificationReviewInvalid = "speech_verification_review_invalid";
    public const string SpeechAudioInvalid = "speech_audio_invalid";
    public const string PricingNotConfigured = "pricing_not_configured";
}

public sealed record GenerateContentRequest(
    Guid ProjectId,
    string IdempotencyKey,
    Guid? OrganizationId = null);

public sealed record ContentLanguageViolation(
    string Field,
    string Reason,
    decimal? EstimatedDurationSeconds = null,
    decimal? TargetMinimumSeconds = null,
    decimal? TargetMaximumSeconds = null);

public sealed record ContentRepairQuoteRequest(
    Guid ProjectId,
    Guid FailedProviderRequestId,
    Guid? OrganizationId = null);

public sealed record ContentRepairQuoteResponse(
    Guid FailedProviderRequestId,
    string ProviderCode,
    string ModelCode,
    IReadOnlyList<ContentLanguageViolation> Violations,
    decimal EstimatedCost,
    string CurrencyCode);

public sealed record ContentLanguageFailureResponse(
    Guid FailedProviderRequestId,
    string ErrorCode,
    string Message,
    IReadOnlyList<ContentLanguageViolation> Violations,
    bool CanRepair);

public sealed record LatestContentLanguageFailureResponse(
    ContentLanguageFailureResponse? Failure);

public sealed record RepairContentRequest(
    Guid ProjectId,
    Guid FailedProviderRequestId,
    string IdempotencyKey,
    Guid? OrganizationId = null);

public sealed record GeneratedCharacterProfile(
    string CharacterKey,
    string Name,
    string Role,
    string Gender,
    int? Age,
    string Face,
    string Hair,
    string Skin,
    string Body,
    string Clothing,
    string Accessories,
    string VisualIdentity,
    IReadOnlyList<string> ImmutableTraits,
    IReadOnlyList<string> ForbiddenChanges);

public sealed record GeneratedContentScene(
    int SequenceNumber,
    string StoryPurpose,
    string Narration,
    string VisualPrompt,
    int DurationSeconds,
    IReadOnlyList<string> CharacterKeys,
    string? SpeechMode = null,
    string? SpeakerCharacterKey = null,
    string? VoiceStyle = null,
    string? AmbientAudio = null,
    string? SoundEffects = null,
    IReadOnlyList<string>? AssetKeys = null,
    int? GenerationDurationSeconds = null);

public sealed record GeneratedProjectAsset(
    string AssetKey,
    string AssetType,
    string Name,
    string CanonicalDescription,
    IReadOnlyList<int> SceneSequenceNumbers);

public sealed record GeneratedContentPlan(
    string Title,
    string Hook,
    string Angle,
    string Audience,
    string CallToAction,
    string ScriptFullText,
    string VisualStyle,
    string NegativePrompt,
    IReadOnlyList<GeneratedCharacterProfile> Characters,
    IReadOnlyList<GeneratedContentScene> Scenes,
    IReadOnlyList<GeneratedProjectAsset>? Assets = null);

public sealed record GeneratedContentResponse(
    Guid ProviderRequestId,
    string ProviderCode,
    string ModelCode,
    long InputTokens,
    long OutputTokens,
    GeneratedContentPlan Plan,
    string? EffectiveGenerationLanguageCode = null,
    string? GenerationLanguagePolicyVersion = null);

public sealed record GenerateCharacterReferenceImageRequest(
    Guid ProjectId,
    Guid CharacterId,
    string IdempotencyKey,
    Guid? OrganizationId = null);

public sealed record GenerateCharacterReferenceImageResponse(
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

public sealed record GenerateSceneVoiceRequest(
    Guid ProjectId,
    Guid SceneId,
    int ScenePlanVersion,
    string ExpectedNarrationHash,
    string IdempotencyKey,
    Guid? OrganizationId = null,
    string? ExpectedVoiceSnapshotHash = null,
    Guid? ExpectedVoiceProfileVersionId = null);

public sealed record SceneVoiceGenerationResponse(
    Guid ProviderRequestId,
    string ProviderCode,
    string ModelCode,
    string Status,
    string ContentUrl,
    string MimeType,
    string Sha256,
    long SizeBytes,
    long DurationMs,
    int SampleRate,
    int Channels,
    string VoiceCode,
    string ProviderVoiceCode,
    long InputTokens,
    long OutputTokens,
    decimal ActualCost,
    string CurrencyCode,
    DateTime ExpiresAtUtc,
    Guid? VoiceGenerationId = null,
    string? VoiceSnapshotHash = null,
    string VerificationStatus = SpeechVerificationStatuses.NotRequested,
    Guid? VoiceProfileVersionId = null,
    string? ExpectedSpeechHash = null);

public sealed record VoiceProfileVersionSummary(
    Guid VoiceProfileId,
    Guid VoiceProfileVersionId,
    string Scope,
    Guid? CharacterId,
    int Version,
    string ProviderCode,
    string ModelCode,
    string VoiceCode,
    string ProviderVoiceCode,
    string LanguageCode,
    decimal SpeakingRate,
    string VoiceInstructions,
    string SnapshotHash,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ApprovedAtUtc,
    Guid? PreviewProviderRequestId = null,
    string? PreviewContentUrl = null,
    string? PreviewSha256 = null,
    long? PreviewDurationMs = null,
    DateTime? PreviewExpiresAtUtc = null,
    string? RowVersion = null);

public sealed record VoiceProfileVersionListResponse(
    IReadOnlyList<VoiceProfileVersionSummary> Versions);

public sealed record CreateVoiceProfileDraftRequest(
    Guid ProjectId,
    string Scope,
    string VoiceCode,
    decimal SpeakingRate,
    Guid? CharacterId = null,
    Guid? OrganizationId = null);

public sealed record VoiceProfilePreviewQuoteRequest(
    Guid ProjectId,
    Guid VoiceProfileVersionId,
    string ExpectedVoiceSnapshotHash,
    Guid? OrganizationId = null);

public sealed record VoiceProfilePreviewQuoteResponse(
    Guid VoiceProfileVersionId,
    string ProviderCode,
    string ModelCode,
    decimal EstimatedCost,
    string CurrencyCode,
    int PreviewTextCharacters);

public sealed record GenerateVoiceProfilePreviewRequest(
    Guid ProjectId,
    Guid VoiceProfileVersionId,
    string ExpectedVoiceSnapshotHash,
    string IdempotencyKey,
    Guid? OrganizationId = null);

public sealed record VoiceProfilePreviewResponse(
    Guid VoiceProfileVersionId,
    Guid ProviderRequestId,
    string ProviderCode,
    string ModelCode,
    string ContentUrl,
    string MimeType,
    string Sha256,
    long SizeBytes,
    long DurationMs,
    int SampleRate,
    int Channels,
    decimal ActualCost,
    string CurrencyCode,
    DateTime ExpiresAtUtc);

public sealed record VoiceCatalogPreviewQuoteRequest(
    Guid ProjectId,
    string VoiceCode,
    decimal SpeakingRate,
    Guid? OrganizationId = null);

public sealed record VoiceCatalogPreviewContextQuoteRequest(
    string VoiceCode,
    decimal SpeakingRate,
    Guid? OrganizationId = null);

public sealed record VoiceCatalogPreviewQuoteResponse(
    string VoiceCode,
    decimal SpeakingRate,
    string ProviderCode,
    string ModelCode,
    decimal EstimatedCost,
    string CurrencyCode,
    int PreviewTextCharacters,
    Guid ContextProjectId);

public sealed record GenerateVoiceCatalogPreviewRequest(
    Guid ProjectId,
    string VoiceCode,
    decimal SpeakingRate,
    string IdempotencyKey,
    Guid? OrganizationId = null);

public sealed record VoiceCatalogPreviewResponse(
    string VoiceCode,
    decimal SpeakingRate,
    Guid ProviderRequestId,
    string ProviderCode,
    string ModelCode,
    string ContentUrl,
    string MimeType,
    string Sha256,
    long SizeBytes,
    long DurationMs,
    int SampleRate,
    int Channels,
    decimal ActualCost,
    string CurrencyCode,
    DateTime ExpiresAtUtc);

public sealed record ApproveVoiceProfileVersionRequest(
    Guid ProjectId,
    Guid VoiceProfileVersionId,
    string ExpectedVoiceSnapshotHash,
    Guid? OrganizationId = null,
    bool PlaybackConfirmed = false);

public sealed record SupersedeVoiceProfileVersionRequest(
    Guid ProjectId,
    Guid VoiceProfileVersionId,
    string ExpectedVoiceSnapshotHash,
    Guid? OrganizationId = null);

public sealed record SceneVoiceQuoteRequest(
    Guid ProjectId,
    Guid SceneId,
    int ScenePlanVersion,
    string ExpectedSpeechHash,
    Guid? OrganizationId = null,
    string? ExpectedVoiceSnapshotHash = null,
    Guid? ExpectedVoiceProfileVersionId = null);

public sealed record SceneVoiceQuoteResponse(
    Guid SceneId,
    Guid VoiceProfileVersionId,
    string VoiceSnapshotHash,
    string ProviderCode,
    string ModelCode,
    decimal EstimatedCost,
    string CurrencyCode,
    bool ReusesExistingGeneration);

public sealed record SceneSpeechVerificationQuoteRequest(
    Guid ProjectId,
    Guid SceneId,
    int ScenePlanVersion,
    string ExpectedSpeechHash,
    long DurationMs,
    Guid? OrganizationId = null);

public sealed record SceneSpeechVerificationQuoteResponse(
    string ProviderCode,
    string ModelCode,
    decimal EstimatedCost,
    string CurrencyCode,
    long BillableAudioSeconds);

public sealed record VerifySceneSpeechRequest(
    Guid ProjectId,
    Guid SceneId,
    int ScenePlanVersion,
    string ExpectedSpeechHash,
    string MediaSha256,
    long DurationMs,
    string IdempotencyKey,
    Guid? OrganizationId = null,
    Guid? SourceMediaAssetId = null);

public sealed record ApproveSpeechVerificationReviewRequest(
    Guid ProjectId,
    Guid SceneId,
    Guid SpeechVerificationReportId,
    string Reason,
    string ExpectedRowVersion,
    Guid? OrganizationId = null);

public sealed record SceneSpeechVerificationResponse(
    Guid SpeechVerificationReportId,
    Guid ProviderRequestId,
    string ProviderCode,
    string ModelCode,
    string Status,
    string Transcript,
    decimal WordErrorRate,
    decimal CharacterErrorRate,
    decimal RequiredTermRecall,
    long? SpeechStartMs,
    long? SpeechEndMs,
    string ExpectedSpeechHash,
    string MediaSha256,
    decimal ActualCost,
    string CurrencyCode,
    string? NormalizedTranscript = null,
    IReadOnlyList<string>? MissingRequiredTerms = null,
    IReadOnlyList<TranscribedWordTiming>? WordTimings = null,
    bool ReviewApproved = false,
    string? ReviewReason = null,
    DateTime? ReviewedAtUtc = null,
    string? RowVersion = null);

public sealed record TranscribedWordTiming(
    string Text,
    long StartMs,
    long EndMs);

public sealed record SubmitKlingVideoRequest(
    Guid ProjectId,
    Guid SceneId,
    string Prompt,
    int DurationSeconds,
    string AspectRatio,
    string Resolution,
    bool NativeAudio,
    string IdempotencyKey,
    Guid? OrganizationId = null,
    KlingReferenceImageInput? ReferenceImage = null,
    int? ScenePlanVersion = null,
    int? ScenePromptVersion = null);

/// <summary>
/// Yêu cầu tạo video trung lập provider. Provider/model, độ phân giải và chiến lược
/// âm thanh được TOOL-SERVER lấy từ snapshot của project; desktop không được phép
/// ghi đè các giá trị đó.
/// </summary>
public sealed record SubmitVideoRequest(
    Guid ProjectId,
    Guid SceneId,
    string IdempotencyKey,
    Guid? OrganizationId = null,
    VideoReferenceImageInput? ReferenceImage = null,
    int? ScenePlanVersion = null,
    int? ScenePromptVersion = null,
    SceneFirstFrameInput? FirstFrame = null);

public sealed record VideoReferenceImageInput(
    Guid CharacterReferenceId,
    string MimeType,
    string Base64Data,
    string Sha256);

public sealed record KlingReferenceImageInput(
    Guid CharacterReferenceId,
    string MimeType,
    string Base64Data,
    string Sha256);

public sealed record KlingVideoTaskResponse(
    Guid ProviderRequestId,
    string ProviderCode,
    string ModelCode,
    string ExternalRequestId,
    string Status,
    decimal ProgressPercent,
    string? OutputUrl,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record VideoTaskResponse(
    Guid ProviderRequestId,
    string ProviderCode,
    string ModelCode,
    string ExternalRequestId,
    string Status,
    decimal ProgressPercent,
    string? OutputUrl,
    string? ErrorCode,
    string? ErrorMessage,
    bool NativeAudio = true,
    string Resolution = "720p");

public sealed record GenerationProviderStatusResponse(
    bool OpenAiReady,
    string? OpenAiModel,
    bool KlingReady,
    string? KlingModel,
    Guid? OrganizationId = null,
    string? OrganizationName = null,
    decimal BudgetLimit = 0,
    decimal ReservedCost = 0,
    decimal ActualCost = 0,
    decimal RemainingBudget = 0,
    string CurrencyCode = "USD",
    bool OpenAiImageReady = false,
    string? OpenAiImageModel = null,
    string? OpenAiImageUnavailableCode = null,
    string? OpenAiImageUnavailableMessage = null,
    decimal? EstimatedCharacterImageCost = null,
    bool OpenAiVoiceReady = false,
    string? OpenAiVoiceModel = null,
    string? OpenAiVoiceUnavailableCode = null,
    string? OpenAiVoiceUnavailableMessage = null,
    decimal? EstimatedSceneVoiceCost = null,
    string? KlingUnavailableCode = null,
    string? KlingUnavailableMessage = null,
    decimal? EstimatedKlingCostPerSecond = null,
    bool VideoReady = false,
    string? VideoProviderCode = null,
    string? VideoProviderName = null,
    string? VideoModel = null,
    string? VideoUnavailableCode = null,
    string? VideoUnavailableMessage = null,
    decimal? EstimatedVideoCostPerSecond = null,
    bool VideoNativeAudio = true,
    string VideoResolution = "720p",
    bool OpenAiTranscriptionReady = false,
    string? OpenAiTranscriptionModel = null,
    string? OpenAiTranscriptionUnavailableCode = null,
    string? OpenAiTranscriptionUnavailableMessage = null,
    decimal? EstimatedSpeechVerificationCost = null,
    bool CanonicalVoiceEnabled = false,
    bool SpeechVerificationEnabled = false,
    bool CanonicalVoiceReady = false,
    string? CanonicalVoiceUnavailableCode = null,
    string? CanonicalVoiceUnavailableMessage = null,
    IReadOnlyList<OpenAiVoiceOption>? OpenAiVoiceOptions = null);

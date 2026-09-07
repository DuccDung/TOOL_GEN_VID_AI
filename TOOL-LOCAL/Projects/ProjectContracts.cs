using TOOL_SHARED.Contracts.Generation;

namespace TOOL_LOCAL.Projects;

public sealed record CreateProjectCommand(
    string Name,
    string Topic,
    string Platform,
    string AspectRatio,
    int TargetDurationSeconds,
    decimal? BudgetLimit,
    string LanguageCode = "vi-VN",
    Guid OrganizationId = default,
    string? VoiceCode = null,
    decimal? VoiceSpeakingRate = null,
    string SpeechProductionPolicy = "ProviderNativeVerified");

public sealed record CreateShortVideoCommand(
    string Content,
    string AspectRatio,
    int DurationSeconds,
    bool AudioEnabled,
    Guid OrganizationId);

public sealed record ShortVideoProjectResult(
    ProjectSummary Project,
    Guid SceneId);

public sealed record ProjectSummary(
    Guid ProjectId,
    Guid? OrganizationId,
    string Name,
    string Topic,
    string Platform,
    string AspectRatio,
    int TargetDurationSeconds,
    string Status,
    decimal ActualCost,
    decimal? BudgetLimit,
    DateTime UpdatedAtUtc);

public sealed record PipelineStageSummary(
    string Code,
    string Title,
    string Subtitle,
    string Status,
    decimal ProgressPercent,
    IReadOnlyList<string> DetailLines);

public sealed record RenderProgressSummary(
    string Status,
    decimal ProgressPercent,
    long CompletedScenes,
    long TotalScenes,
    int? EstimatedSecondsRemaining);

public sealed record VideoPreviewSummary(
    string? Url,
    long? DurationMs,
    string? MimeType);

public sealed record SceneSpeechVerificationSummary(
    Guid SpeechVerificationReportId,
    string Status,
    string Transcript,
    string NormalizedTranscript,
    decimal WordErrorRate,
    decimal CharacterErrorRate,
    decimal RequiredTermRecall,
    IReadOnlyList<string> MissingRequiredTerms,
    long? SpeechStartMs,
    long? SpeechEndMs,
    bool ReviewApproved = false,
    string? ReviewReason = null,
    DateTime? ReviewedAtUtc = null,
    string? RowVersion = null);

public sealed record VoiceProfileDashboardSummary(
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
    string SnapshotHash,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ApprovedAtUtc,
    VideoPreviewSummary? Preview,
    Guid? PreviewProviderRequestId = null);

public sealed record CanonicalVoiceQuoteSummary(
    decimal EstimatedCost,
    string CurrencyCode,
    int NewVoiceCount,
    int ReusedVoiceCount,
    IReadOnlyList<SceneVoiceQuoteResponse> Scenes);

public sealed record CharacterReferenceSummary(
    Guid CharacterReferenceId,
    string ReferenceType,
    bool IsPrimary,
    string ApprovalStatus,
    string? PreviewUrl,
    string? MimeType);

public sealed record CharacterDashboardSummary(
    Guid CharacterId,
    string CharacterKey,
    int Version,
    string Name,
    string? Role,
    string VisualIdentity,
    string Wardrobe,
    IReadOnlyList<string> ImmutableTraits,
    IReadOnlyList<string> ForbiddenChanges,
    string Status,
    long SceneCount,
    CharacterReferenceSummary? PrimaryReference,
    bool CanEdit,
    bool CanApprove,
    string? SetupMessage,
    string? VoiceCode = null,
    decimal? VoiceSpeakingRate = null);

public sealed record SceneCharacterSummary(
    Guid CharacterId,
    string Name,
    string Status,
    string? ReferencePreviewUrl);

public sealed record SceneSpeechPacingSummary(
    int SpeechUnitCount,
    decimal SpeakingRate,
    decimal EstimatedDurationSeconds,
    decimal EstimatedDurationRatio,
    decimal TargetMinimumSeconds,
    decimal TargetMaximumSeconds,
    string EstimatedStatus,
    decimal? ActualDurationSeconds = null,
    decimal? ActualDurationRatio = null,
    string? ActualStatus = null);

public sealed record SceneDashboardSummary(
    Guid SceneId,
    int SequenceNumber,
    long TimelineStartMs,
    long TimelineEndMs,
    long DurationMs,
    long GenerationDurationMs,
    string StoryPurpose,
    string? Narration,
    string VisualDescription,
    string Prompt,
    string Status,
    bool CanEdit,
    bool CanGenerate,
    IReadOnlyList<SceneCharacterSummary> Characters,
    string? CharacterSetupMessage,
    VideoPreviewSummary? Preview,
    string? LastErrorMessage,
    string? LastErrorCode = null,
    bool HasNarratedAudio = false,
    string SpeechMode = "None",
    bool NativeAudioPresent = false,
    bool NativeAudioAudible = false,
    bool RequiresAudioReview = false,
    bool CanApproveNativeAudio = false,
    string? SpeakerCharacterName = null,
    string? VoiceStyle = null,
    string? AmbientAudio = null,
    string? SoundEffects = null,
    string SpeechStatus = "SpeechNotRequired",
    bool HasCanonicalVoicePreview = false,
    VideoPreviewSummary? CanonicalVoicePreview = null,
    SceneSpeechVerificationSummary? SpeechVerification = null,
    Guid? VoiceProfileVersionId = null,
    string? VoiceSnapshotHash = null,
    SceneSpeechPacingSummary? SpeechPacing = null);

public sealed record UpdateSceneCommand(
    Guid SceneId,
    string? Narration,
    string VisualDescription,
    string Prompt,
    string SpeechMode = "None",
    string? VoiceStyle = null,
    string? AmbientAudio = null,
    string? SoundEffects = null);

public sealed record UpdateCharacterCommand(
    Guid CharacterId,
    string Name,
    string? Role,
    string VisualIdentity,
    string Wardrobe,
    IReadOnlyList<string> ImmutableTraits,
    IReadOnlyList<string> ForbiddenChanges,
    string? VoiceCode = null,
    decimal? VoiceSpeakingRate = null);

public sealed record ProjectContentSummary(
    int ScriptVersion,
    string Title,
    string ScriptFullText,
    string? Hook,
    string? Angle,
    string? Audience,
    string? CallToAction);

public sealed record ProjectDashboard(
    ProjectSummary Project,
    string LanguageCode,
    DateTime CreatedAtUtc,
    long TotalScenes,
    long ApprovedScenes,
    long FailedScenes,
    long PendingJobs,
    long RunningJobs,
    long FailedJobs,
    decimal OverallProgressPercent,
    IReadOnlyList<PipelineStageSummary> Pipeline,
    RenderProgressSummary Render,
    IReadOnlyList<CharacterDashboardSummary> Characters,
    IReadOnlyList<SceneDashboardSummary> Scenes,
    VideoPreviewSummary? Preview,
    string? LastErrorMessage,
    string? VoiceCode = null,
    decimal? VoiceSpeakingRate = null,
    string AudioStrategy = "ProviderNative",
    string SpeechProductionPolicy = "ProviderNativeVerified",
    string? VideoProviderCode = null,
    string? VideoModelCode = null,
    string? WorkflowStructureType = null,
    string? EffectiveGenerationLanguageCode = null,
    bool RequiresVietnameseContentRegeneration = false,
    ProjectContentSummary? Content = null,
    IReadOnlyList<VoiceProfileDashboardSummary>? VoiceProfiles = null);

public sealed record AiModelSummary(
    string ProviderCode,
    string ProviderName,
    string ModelCode,
    string DisplayName,
    string Modality,
    bool IsDefault);

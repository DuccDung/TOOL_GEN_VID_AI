namespace TOOL_SHARED.Contracts.Generation;

public static class KlingSpeechModes
{
    public const string None = "None";
    public const string OnCameraDialogue = "OnCameraDialogue";
    public const string NativeVoiceOver = "NativeVoiceOver";
}

public sealed record GenerateContentRequest(
    Guid ProjectId,
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
    IReadOnlyList<string>? AssetKeys = null);

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
    Guid? OrganizationId = null);

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
    DateTime ExpiresAtUtc);

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
    int? ScenePromptVersion = null);

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
    string VideoResolution = "720p");

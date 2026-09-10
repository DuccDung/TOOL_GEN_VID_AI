using System.Text.Json;
using TOOL_LOCAL.Projects;
using TOOL_SHARED.Contracts.Authentication;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Accounts;
using TOOL_LOCAL.Providers;
using TOOL_SHARED.Contracts.Organizations;
using TOOL_LOCAL.Media;
using TOOL_SHARED.Contracts.Projects;

namespace TOOL_LOCAL.WebView;

internal sealed record WebMessageRequest(
    string Type,
    string? RequestId,
    JsonElement Payload);

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
internal sealed record BilibiliScanRequest(string Url);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
internal sealed record BilibiliDownloadRequest(string ScanId, string[] EntryIds, string Quality);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
internal sealed record BilibiliJobRequest(string? JobId = null);

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
internal sealed record VietsubStartCloudTranslationRequest(Guid ExpectedTrackId, int ExpectedTrackRevision);

internal sealed record WebMessageError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? Errors = null);

internal sealed record WebMessageResponse(
    string Type,
    string? RequestId,
    object? Payload = null,
    WebMessageError? Error = null);

internal sealed record LicenseInvalidatedMessage(string Message, CurrentLicenseResponse? License);

internal sealed record DashboardStateResponse(
    UserProfileResponse Profile,
    IReadOnlyList<OrganizationSummaryResponse> Organizations,
    Guid SelectedOrganizationId,
    IReadOnlyList<ProjectSummary> Projects,
    ProjectDashboard? SelectedProject,
    ProjectAssetLibraryResponse? AssetLibrary,
    IReadOnlyList<AiModelSummary> Models,
    GenerationProviderStatusResponse ProviderStatus,
    MediaToolStatusSummary MediaTools,
    CurrentLicenseResponse? License,
    bool GenerationRunning,
    DashboardFeatureFlagsResponse Features,
    IReadOnlyList<SceneFirstFrameSummary> SceneFirstFrames,
    ContentLanguageFailureResponse? ContentLanguageFailure = null);

internal sealed record DashboardFeatureFlagsResponse(
    bool VietsubEnabled,
    bool SpeechSynchronizationEnabled = false,
    bool TikTokEnabled = false,
    bool ShortVideoCharacterOutfitEnabled = false);

internal sealed record TikTokPublishWebRequest(
    string Title,
    string PrivacyLevel,
    bool AllowComment,
    bool AllowDuet,
    bool AllowStitch,
    bool CommercialContent,
    bool BrandContent,
    bool BrandOrganic,
    bool IsAiGenerated,
    bool ConsentConfirmed,
    Guid ConnectionId = default,
    Guid ClientRequestId = default,
    Guid MediaId = default);

internal sealed record TikTokConnectionWebRequest(Guid ConnectionId);
internal sealed record TikTokConnectWebRequest(Guid? TargetConnectionId = null);
internal sealed record TikTokHistoryWebRequest(Guid? ConnectionId = null, int Page = 1);
internal sealed record TikTokCancelWebRequest(string? OperationRequestId);

internal sealed record TikTokPublishStatusWebRequest(Guid PublishJobId);

internal sealed record TikTokPolicyWebRequest(string Policy);

internal sealed record TikTokMediaWebResponse(
    Guid MediaId,
    string FileName,
    string MimeType,
    long SizeBytes,
    decimal DurationSeconds,
    int Width,
    int Height,
    decimal FramesPerSecond,
    string VideoCodec,
    string PreviewUrl);

internal sealed record TikTokUploadProgressWebResponse(
    Guid PublishJobId,
    long UploadedBytes,
    long TotalBytes,
    int Percent,
    int CompletedChunks,
    int TotalChunks,
    Guid? ConnectionId = null);

internal sealed record DesktopFeatureSettingsResponse(
    bool SpeechSynchronizationEnabled,
    bool ActiveSpeechSynchronizationEnabled,
    bool RestartRequired);

internal sealed record UpdateDesktopFeatureSettingsWebRequest(
    bool SpeechSynchronizationEnabled);

internal sealed record SelectProjectWebRequest(Guid ProjectId);

internal sealed record SelectOrganizationWebRequest(Guid OrganizationId);

internal sealed record CreateProjectWebRequest(
    string Topic,
    string AspectRatio,
    string LanguageCode,
    string? VoiceCode = null,
    decimal? VoiceSpeakingRate = null,
    string SpeechProductionPolicy = "ProviderNativeVerified",
    Guid? OrganizationId = null);

internal sealed record CreateShortVideoWebRequest(
    string Content,
    string AspectRatio,
    int DurationSeconds,
    bool AudioEnabled,
    Guid? OrganizationId = null,
    string Mode = ShortVideoModes.TextOnly);

internal sealed record ShortVideoOutfitAction(Guid ProjectId, Guid OrganizationId, int Revision = 0,
    string? Kind = null, Guid? QuoteId = null, Guid? CompositionId = null, bool Confirmed = false,
    ShortVideoImageInfo? Character = null, ShortVideoImageInfo? Outfit = null, string? Background = null, string? Motion = null,
    string? ExpectedProviderCode = null, int DurationSeconds = 8, string AspectRatio = "9:16");

// short-library.* uses ShortVideoLibraryAction / ShortVideoLibraryState /
// ShortVideoCreatedNotice from TOOL_SHARED.Contracts.Generation. File paths and image bytes are native-only.

internal sealed record GenerateVideoWebRequest(IReadOnlyList<Guid>? SceneIds);

internal sealed record CreateVoiceProfileDraftWebRequest(
    string Scope,
    Guid? CharacterId,
    string VoiceCode,
    decimal SpeakingRate);

internal sealed record VoiceProfileActionWebRequest(
    Guid VoiceProfileVersionId,
    string ExpectedVoiceSnapshotHash,
    bool PlaybackConfirmed = false);

internal sealed record VoiceCatalogPreviewWebRequest(
    string VoiceCode,
    decimal SpeakingRate,
    Guid? ContextProjectId = null);

internal sealed record ContentRepairWebRequest(Guid FailedProviderRequestId);

internal sealed record UpdateSceneWebRequest(
    Guid SceneId,
    string? Narration,
    string VisualDescription,
    string Prompt,
    string SpeechMode = KlingSpeechModes.None,
    string? VoiceStyle = null,
    string? AmbientAudio = null,
    string? SoundEffects = null);

internal sealed record SceneActionWebRequest(
    Guid SceneId,
    bool PlaybackConfirmed = false,
    Guid? SpeechVerificationReportId = null,
    string? SpeechReviewReason = null,
    string? SpeechVerificationRowVersion = null);

internal sealed record SceneFirstFrameActionWebRequest(
    Guid SceneId,
    int Attempt = 1,
    Guid? FrameId = null,
    string? RowVersion = null);

internal sealed record CharacterActionWebRequest(Guid CharacterId);

internal sealed record UpdateCharacterWebRequest(
    Guid CharacterId,
    string Name,
    string? Role,
    string VisualIdentity,
    string Wardrobe,
    IReadOnlyList<string> ImmutableTraits,
    IReadOnlyList<string> ForbiddenChanges,
    string? VoiceCode = null,
    decimal? VoiceSpeakingRate = null);

internal sealed record CreateProjectAssetWebRequest(
    string AssetType,
    string Name,
    string CanonicalDescription);

internal sealed record UpdateProjectAssetWebRequest(
    Guid ProjectAssetId,
    string AssetType,
    string Name,
    string CanonicalDescription,
    string ConcurrencyToken);

internal sealed record ProjectAssetActionWebRequest(
    Guid ProjectAssetId,
    string ConcurrencyToken);

internal sealed record ApproveAiProjectAssetsWebRequest(
    IReadOnlyList<ApproveProjectAssetInput> Assets);

internal sealed record UpdateSceneAssetsWebRequest(
    Guid SceneId,
    IReadOnlyList<Guid> ProjectAssetIds);

internal sealed record ConfirmSceneAssetsWebRequest(
    Guid SceneId,
    IReadOnlyList<ApproveProjectAssetInput> Assets);

internal sealed record TestProviderWebRequest(string ProviderCode);

internal sealed record CreateLicensePaymentWebRequest(
    Guid LicensePlanId,
    string IdempotencyKey);

internal sealed record LicensePaymentStatusWebRequest(string OrderCode);

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

internal sealed record WebMessageError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? Errors = null);

internal sealed record WebMessageResponse(
    string Type,
    string? RequestId,
    object? Payload = null,
    WebMessageError? Error = null);

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
    bool SpeechSynchronizationEnabled = false);

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
    string SpeechProductionPolicy = "ProviderNativeVerified");

internal sealed record CreateShortVideoWebRequest(
    string Content,
    string AspectRatio,
    int DurationSeconds,
    bool AudioEnabled);

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

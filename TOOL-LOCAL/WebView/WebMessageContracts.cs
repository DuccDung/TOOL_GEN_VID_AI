using System.Text.Json;
using TOOL_LOCAL.Projects;
using TOOL_SHARED.Contracts.Authentication;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Accounts;
using TOOL_LOCAL.Providers;
using TOOL_SHARED.Contracts.Organizations;
using TOOL_LOCAL.Media;

namespace TOOL_LOCAL.WebView;

internal sealed record WebMessageRequest(
    string Type,
    string? RequestId,
    JsonElement Payload);

internal sealed record WebMessageError(string Code, string Message);

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
    IReadOnlyList<AiModelSummary> Models,
    GenerationProviderStatusResponse ProviderStatus,
    MediaToolStatusSummary MediaTools,
    CurrentLicenseResponse? License,
    bool GenerationRunning);

internal sealed record SelectProjectWebRequest(Guid ProjectId);

internal sealed record SelectOrganizationWebRequest(Guid OrganizationId);

internal sealed record CreateProjectWebRequest(
    string Topic,
    string AspectRatio,
    string LanguageCode,
    string? VoiceCode = null,
    decimal? VoiceSpeakingRate = null);

internal sealed record CreateShortVideoWebRequest(
    string Content,
    string AspectRatio,
    int DurationSeconds,
    bool AudioEnabled);

internal sealed record GenerateVideoWebRequest(IReadOnlyList<Guid>? SceneIds);

internal sealed record UpdateSceneWebRequest(
    Guid SceneId,
    string? Narration,
    string VisualDescription,
    string Prompt,
    string SpeechMode = KlingSpeechModes.None,
    string? VoiceStyle = null,
    string? AmbientAudio = null,
    string? SoundEffects = null);

internal sealed record SceneActionWebRequest(Guid SceneId, bool PlaybackConfirmed = false);

internal sealed record CharacterActionWebRequest(Guid CharacterId);

internal sealed record UpdateCharacterWebRequest(
    Guid CharacterId,
    string Name,
    string? Role,
    string VisualIdentity,
    string Wardrobe,
    IReadOnlyList<string> ImmutableTraits,
    IReadOnlyList<string> ForbiddenChanges);

internal sealed record TestProviderWebRequest(string ProviderCode);

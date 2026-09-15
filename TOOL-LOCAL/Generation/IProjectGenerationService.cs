using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Projects;
using TOOL_LOCAL.Projects;

namespace TOOL_LOCAL.Generation;

internal sealed record VoiceCatalogPreviewPlayback(
    string VoiceCode,
    decimal SpeakingRate,
    string PreviewUrl,
    long DurationMs,
    decimal ActualCost,
    string CurrencyCode);

internal interface IProjectGenerationService
{
    Task<GenerationProviderStatusResponse> GetProviderStatusAsync(CancellationToken cancellationToken);

    Task<GeneratedContentResponse> GenerateContentAsync(
        Guid projectId,
        string remoteUserId,
        CancellationToken cancellationToken);

    Task<ContentLanguageFailureResponse?> GetLatestContentLanguageFailureAsync(
        Guid projectId,
        CancellationToken cancellationToken);

    Task<ContentRepairQuoteResponse> GetContentRepairQuoteAsync(
        Guid projectId,
        Guid failedProviderRequestId,
        CancellationToken cancellationToken);

    Task<GeneratedContentResponse> RepairContentAsync(
        Guid projectId,
        string remoteUserId,
        Guid failedProviderRequestId,
        CancellationToken cancellationToken);

    Task<MaterializeProjectAssetPlanResponse> SynchronizeProjectAssetPlanAsync(
        Guid projectId,
        string remoteUserId,
        CancellationToken cancellationToken);

    Task<GenerateCharacterReferenceImageResponse> GenerateCharacterReferenceImageAsync(
        Guid projectId,
        string remoteUserId,
        Guid characterId,
        CancellationToken cancellationToken);

    Task<SceneFirstFrameQuoteResponse> GetSceneFirstFrameQuoteAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken);

    Task<SceneFirstFrameListResponse> GetSceneFirstFramesAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken);

    Task<ProjectSceneFirstFrameListResponse> GetProjectSceneFirstFramesAsync(
        Guid projectId,
        CancellationToken cancellationToken);

    Task<SceneFirstFrameSummary> GenerateSceneFirstFrameAsync(
        Guid projectId,
        string remoteUserId,
        Guid sceneId,
        int attempt,
        CancellationToken cancellationToken);

    Task<SceneFirstFrameSummary> ApproveSceneFirstFrameAsync(
        Guid projectId,
        Guid sceneId,
        Guid frameId,
        string rowVersion,
        CancellationToken cancellationToken);

    Task<SceneFirstFrameSummary> RejectSceneFirstFrameAsync(
        Guid projectId,
        Guid sceneId,
        Guid frameId,
        string rowVersion,
        CancellationToken cancellationToken);

    Task<SceneFirstFrameSummary> RetrySceneFirstFrameDownloadAsync(
        Guid projectId,
        Guid sceneId,
        Guid frameId,
        CancellationToken cancellationToken);

    Task<SceneSpeechVerificationQuoteResponse> GetSceneSpeechVerificationQuoteAsync(
        Guid projectId,
        string remoteUserId,
        Guid sceneId,
        CancellationToken cancellationToken);

    Task<SceneSpeechVerificationResponse> VerifySceneSpeechAsync(
        Guid projectId,
        string remoteUserId,
        Guid sceneId,
        CancellationToken cancellationToken);

    Task<SceneSpeechVerificationResponse> ApproveSpeechVerificationReviewAsync(
        Guid projectId,
        string remoteUserId,
        Guid sceneId,
        Guid speechVerificationReportId,
        string reason,
        string expectedRowVersion,
        CancellationToken cancellationToken);

    Task<VoiceProfileVersionSummary> CreateVoiceProfileDraftAsync(
        Guid projectId,
        string remoteUserId,
        string scope,
        Guid? characterId,
        string voiceCode,
        decimal speakingRate,
        CancellationToken cancellationToken);

    Task<VoiceProfilePreviewQuoteResponse> GetVoiceProfilePreviewQuoteAsync(
        Guid projectId,
        string remoteUserId,
        Guid voiceProfileVersionId,
        string expectedVoiceSnapshotHash,
        CancellationToken cancellationToken);

    Task<VoiceProfilePreviewResponse> GenerateVoiceProfilePreviewAsync(
        Guid projectId,
        string remoteUserId,
        Guid voiceProfileVersionId,
        string expectedVoiceSnapshotHash,
        CancellationToken cancellationToken);

    Task<VoiceCatalogPreviewQuoteResponse> GetVoiceCatalogPreviewQuoteAsync(
        Guid projectId,
        string remoteUserId,
        string voiceCode,
        decimal speakingRate,
        CancellationToken cancellationToken);

    Task<VoiceCatalogPreviewQuoteResponse> GetVoiceCatalogPreviewContextQuoteAsync(
        string voiceCode,
        decimal speakingRate,
        CancellationToken cancellationToken);

    Task<VoiceCatalogPreviewPlayback> GenerateVoiceCatalogPreviewAsync(
        Guid projectId,
        string remoteUserId,
        string voiceCode,
        decimal speakingRate,
        string operationRequestId,
        CancellationToken cancellationToken);

    Task<VoiceProfileVersionSummary> ApproveVoiceProfileVersionAsync(
        Guid projectId,
        string remoteUserId,
        Guid voiceProfileVersionId,
        string expectedVoiceSnapshotHash,
        bool playbackConfirmed,
        CancellationToken cancellationToken);

    Task<VoiceProfileVersionSummary> SupersedeVoiceProfileVersionAsync(
        Guid projectId,
        string remoteUserId,
        Guid voiceProfileVersionId,
        string expectedVoiceSnapshotHash,
        CancellationToken cancellationToken);

    Task<CanonicalVoiceQuoteSummary> GetCanonicalVoiceQuoteAsync(
        Guid projectId,
        string remoteUserId,
        IReadOnlyCollection<Guid> sceneIds,
        CancellationToken cancellationToken);

    Task<int> GenerateVideosAsync(
        Guid projectId,
        string remoteUserId,
        IReadOnlyCollection<Guid>? sceneIds,
        Func<string, CancellationToken, Task>? reportProgress,
        CancellationToken cancellationToken,
        bool resumeOnly = false);
}

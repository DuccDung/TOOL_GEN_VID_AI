using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Projects;

namespace TOOL_LOCAL.Generation;

internal interface IProjectGenerationService
{
    Task<GenerationProviderStatusResponse> GetProviderStatusAsync(CancellationToken cancellationToken);

    Task<GeneratedContentResponse> GenerateContentAsync(
        Guid projectId,
        string remoteUserId,
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

    Task<int> GenerateVideosAsync(
        Guid projectId,
        string remoteUserId,
        IReadOnlyCollection<Guid>? sceneIds,
        Func<string, CancellationToken, Task>? reportProgress,
        CancellationToken cancellationToken);
}

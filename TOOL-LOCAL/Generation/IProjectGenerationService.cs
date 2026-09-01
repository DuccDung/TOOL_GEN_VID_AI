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

    Task<int> GenerateVideosAsync(
        Guid projectId,
        string remoteUserId,
        IReadOnlyCollection<Guid>? sceneIds,
        Func<string, CancellationToken, Task>? reportProgress,
        CancellationToken cancellationToken);
}

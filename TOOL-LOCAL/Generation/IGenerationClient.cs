using TOOL_LOCAL.Providers;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Organizations;
using TOOL_SHARED.Contracts.Projects;

namespace TOOL_LOCAL.Generation;

internal interface IGenerationClient
{
    Guid? SelectedOrganizationId { get; }
    Task<IReadOnlyList<OrganizationSummaryResponse>> GetOrganizationsAsync(CancellationToken cancellationToken);
    Task SelectOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<GenerationProviderStatusResponse> GetProviderStatusAsync(CancellationToken cancellationToken);
    Task<GeneratedContentResponse> GenerateContentAsync(GenerateContentRequest request, CancellationToken cancellationToken);
    Task<GenerateCharacterReferenceImageResponse> GenerateCharacterReferenceImageAsync(GenerateCharacterReferenceImageRequest request, CancellationToken cancellationToken);
    Task<SceneVoiceGenerationResponse> GenerateSceneVoiceAsync(GenerateSceneVoiceRequest request, CancellationToken cancellationToken);
    Task<VideoTaskResponse> SubmitVideoAsync(SubmitVideoRequest request, CancellationToken cancellationToken);
    Task<VideoTaskResponse> GetVideoStatusAsync(Guid providerRequestId, CancellationToken cancellationToken);
    Task<ProjectAssetLibraryResponse> GetProjectAssetLibraryAsync(Guid projectId, CancellationToken cancellationToken);
    Task<MaterializeProjectAssetPlanResponse> MaterializeProjectAssetPlanAsync(Guid projectId, MaterializeProjectAssetPlanRequest request, CancellationToken cancellationToken);
    Task<ProjectAssetSummary> CreateProjectAssetAsync(Guid projectId, CreateProjectAssetRequest request, CancellationToken cancellationToken);
    Task<ProjectAssetSummary> UpdateProjectAssetAsync(Guid projectId, Guid projectAssetId, UpdateProjectAssetRequest request, CancellationToken cancellationToken);
    Task<ProjectAssetSummary> LockProjectAssetAsync(Guid projectId, Guid projectAssetId, ChangeProjectAssetLockRequest request, CancellationToken cancellationToken);
    Task<ProjectAssetSummary> UnlockProjectAssetAsync(Guid projectId, Guid projectAssetId, ChangeProjectAssetLockRequest request, CancellationToken cancellationToken);
    Task<ApproveAiProjectAssetsResponse> ApproveAiProjectAssetsAsync(Guid projectId, ApproveAiProjectAssetsRequest request, CancellationToken cancellationToken);
    Task DeleteProjectAssetAsync(Guid projectId, Guid projectAssetId, DeleteProjectAssetRequest request, CancellationToken cancellationToken);
    Task<SceneAssetAssignmentSummary> UpdateSceneAssetAssignmentsAsync(Guid projectId, Guid sceneId, UpdateSceneAssetAssignmentsRequest request, CancellationToken cancellationToken);
    Task<ConfirmSceneProjectAssetsResponse> ConfirmSceneProjectAssetsAsync(Guid projectId, Guid sceneId, ConfirmSceneProjectAssetsRequest request, CancellationToken cancellationToken);
    Task DownloadVideoAsync(string outputUrl, string destinationPath, CancellationToken cancellationToken);
    Task DownloadCharacterImageAsync(GenerateCharacterReferenceImageResponse response, string destinationPath, CancellationToken cancellationToken);
    Task DownloadSceneVoiceAsync(SceneVoiceGenerationResponse response, string destinationPath, CancellationToken cancellationToken);
    Task<ProviderSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken);
    Task TestProviderAsync(string providerCode, CancellationToken cancellationToken);
}

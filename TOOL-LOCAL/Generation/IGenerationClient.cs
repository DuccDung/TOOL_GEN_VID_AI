using TOOL_LOCAL.Providers;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Organizations;
using TOOL_SHARED.Contracts.Projects;

namespace TOOL_LOCAL.Generation;

internal interface ILocalVoiceAccessClient
{
    Task<LocalVoiceAccessResponse> AuthorizeLocalVoiceAsync(Guid projectId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Local voice authorization is unavailable.");
}

internal interface IGenerationClient : ILocalVoiceAccessClient, IShortVideoOutfitClient
{
    Guid? SelectedOrganizationId { get; }
    Task<IReadOnlyList<OrganizationSummaryResponse>> GetOrganizationsAsync(CancellationToken cancellationToken);
    Task SelectOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<GenerationProviderStatusResponse> GetProviderStatusAsync(CancellationToken cancellationToken);
    Task<GeneratedContentResponse> GenerateContentAsync(GenerateContentRequest request, CancellationToken cancellationToken);
    Task<ContentLanguageFailureResponse?> GetLatestContentLanguageFailureAsync(Guid projectId, CancellationToken cancellationToken);
    Task<ContentRepairQuoteResponse> GetContentRepairQuoteAsync(ContentRepairQuoteRequest request, CancellationToken cancellationToken);
    Task<GeneratedContentResponse> RepairContentAsync(RepairContentRequest request, CancellationToken cancellationToken);
    Task<GenerateCharacterReferenceImageResponse> GenerateCharacterReferenceImageAsync(GenerateCharacterReferenceImageRequest request, CancellationToken cancellationToken);
    Task<SceneFirstFrameQuoteResponse> GetSceneFirstFrameQuoteAsync(Guid projectId, Guid sceneId, CancellationToken cancellationToken);
    Task<GenerateSceneFirstFrameResponse> GenerateSceneFirstFrameAsync(GenerateSceneFirstFrameRequest request, CancellationToken cancellationToken);
    Task<SceneFirstFrameListResponse> GetSceneFirstFramesAsync(Guid projectId, Guid sceneId, CancellationToken cancellationToken);
    Task<ProjectSceneFirstFrameListResponse> GetProjectSceneFirstFramesAsync(Guid projectId, CancellationToken cancellationToken);
    Task<SceneFirstFrameSummary> MaterializeSceneFirstFrameAsync(Guid projectId, Guid sceneId, MaterializeSceneFirstFrameRequest request, CancellationToken cancellationToken);
    Task<SceneFirstFrameSummary> ApproveSceneFirstFrameAsync(Guid projectId, Guid sceneId, Guid frameId, ChangeSceneFirstFrameStatusRequest request, CancellationToken cancellationToken);
    Task<SceneFirstFrameSummary> RejectSceneFirstFrameAsync(Guid projectId, Guid sceneId, Guid frameId, ChangeSceneFirstFrameStatusRequest request, CancellationToken cancellationToken);
    Task<SceneVoiceGenerationResponse> GenerateSceneVoiceAsync(GenerateSceneVoiceRequest request, CancellationToken cancellationToken);
    Task<VoiceProfileVersionListResponse> GetVoiceProfileVersionsAsync(Guid projectId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Voice profile listing is not supported by this client.");
    Task<VoiceProfileVersionSummary> CreateVoiceProfileDraftAsync(CreateVoiceProfileDraftRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Voice profile drafts are not supported by this client.");
    Task<VoiceProfilePreviewQuoteResponse> GetVoiceProfilePreviewQuoteAsync(VoiceProfilePreviewQuoteRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Voice profile preview quote is not supported by this client.");
    Task<VoiceProfilePreviewResponse> GenerateVoiceProfilePreviewAsync(GenerateVoiceProfilePreviewRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Voice profile preview is not supported by this client.");
    Task<VoiceCatalogPreviewQuoteResponse> GetVoiceCatalogPreviewQuoteAsync(VoiceCatalogPreviewQuoteRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Voice catalog preview quote is not supported by this client.");
    Task<VoiceCatalogPreviewQuoteResponse> GetVoiceCatalogPreviewContextQuoteAsync(VoiceCatalogPreviewContextQuoteRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Voice catalog preview context quote is not supported by this client.");
    Task<VoiceCatalogPreviewResponse> GenerateVoiceCatalogPreviewAsync(GenerateVoiceCatalogPreviewRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Voice catalog preview is not supported by this client.");
    Task<VoiceProfileVersionSummary> ApproveVoiceProfileVersionAsync(ApproveVoiceProfileVersionRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Voice profile approval is not supported by this client.");
    Task<VoiceProfileVersionSummary> SupersedeVoiceProfileVersionAsync(SupersedeVoiceProfileVersionRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Voice profile supersede is not supported by this client.");
    Task<SceneVoiceQuoteResponse> GetSceneVoiceQuoteAsync(SceneVoiceQuoteRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Scene voice quote is not supported by this client.");
    Task<SceneSpeechVerificationQuoteResponse> GetSceneSpeechVerificationQuoteAsync(SceneSpeechVerificationQuoteRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Speech verification quote is not supported by this client.");
    Task<SceneSpeechVerificationResponse> VerifySceneSpeechAsync(VerifySceneSpeechRequest request, string wavPath, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Speech verification is not supported by this client.");
    Task<SceneSpeechVerificationResponse> ApproveSpeechVerificationReviewAsync(ApproveSpeechVerificationReviewRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Speech verification review is not supported by this client.");
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
    Task DownloadSceneFirstFrameAsync(GenerateSceneFirstFrameResponse response, string destinationPath, CancellationToken cancellationToken);
    Task DownloadSceneVoiceAsync(SceneVoiceGenerationResponse response, string destinationPath, CancellationToken cancellationToken);
    Task DownloadVoiceProfilePreviewAsync(VoiceProfilePreviewResponse response, string destinationPath, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Voice profile preview download is not supported by this client.");
    Task DownloadVoiceCatalogPreviewAsync(VoiceCatalogPreviewResponse response, string destinationPath, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Voice catalog preview download is not supported by this client.");
    Task<ProviderSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken);
    Task TestProviderAsync(string providerCode, CancellationToken cancellationToken);
}

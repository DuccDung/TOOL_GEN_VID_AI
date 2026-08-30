using TOOL_LOCAL.Providers;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Organizations;

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
    Task DownloadVideoAsync(string outputUrl, string destinationPath, CancellationToken cancellationToken);
    Task DownloadCharacterImageAsync(GenerateCharacterReferenceImageResponse response, string destinationPath, CancellationToken cancellationToken);
    Task DownloadSceneVoiceAsync(SceneVoiceGenerationResponse response, string destinationPath, CancellationToken cancellationToken);
    Task<ProviderSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken);
    Task TestProviderAsync(string providerCode, CancellationToken cancellationToken);
}

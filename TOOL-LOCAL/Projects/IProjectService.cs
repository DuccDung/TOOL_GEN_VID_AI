using TOOL_SHARED.Contracts.Authentication;

namespace TOOL_LOCAL.Projects;

public interface IProjectService
{
    Task<IReadOnlyList<ProjectSummary>> ListAsync(string remoteUserId, CancellationToken cancellationToken = default);

    Task<ProjectDashboard?> GetDashboardAsync(
        Guid projectId,
        string remoteUserId,
        CancellationToken cancellationToken = default);

    Task UpdateSceneAsync(
        Guid projectId,
        string remoteUserId,
        UpdateSceneCommand command,
        CancellationToken cancellationToken = default);

    Task ApproveSceneNativeAudioAsync(
        Guid projectId,
        string remoteUserId,
        Guid sceneId,
        bool playbackConfirmed,
        CancellationToken cancellationToken = default);

    Task UpdateCharacterAsync(
        Guid projectId,
        string remoteUserId,
        UpdateCharacterCommand command,
        CancellationToken cancellationToken = default);

    Task ImportCharacterReferenceAsync(
        Guid projectId,
        string remoteUserId,
        Guid characterId,
        string sourcePath,
        CancellationToken cancellationToken = default);

    Task ApproveCharacterAsync(
        Guid projectId,
        string remoteUserId,
        Guid characterId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiModelSummary>> ListAvailableModelsAsync(
        CancellationToken cancellationToken = default);

    Task<ProjectSummary> CreateAsync(
        CreateProjectCommand command,
        UserProfileResponse owner,
        Guid remoteDeviceId,
        CancellationToken cancellationToken = default);
}

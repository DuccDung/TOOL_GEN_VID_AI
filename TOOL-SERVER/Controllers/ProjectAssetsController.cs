using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Projects;
using TOOL_SHARED.Contracts.Projects;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/assets")]
public sealed class ProjectAssetsController(IProjectAssetService projectAssetService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ProjectAssetLibraryResponse>(StatusCodes.Status200OK)]
    public Task<ProjectAssetLibraryResponse> GetLibrary(
        Guid projectId,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken) =>
        projectAssetService.GetLibraryAsync(
            projectId,
            organizationId,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

    [HttpPost]
    [ProducesResponseType<ProjectAssetSummary>(StatusCodes.Status200OK)]
    public Task<ProjectAssetSummary> Create(
        Guid projectId,
        [FromBody] CreateProjectAssetRequest request,
        CancellationToken cancellationToken) =>
        projectAssetService.CreateAsync(
            projectId,
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

    [HttpPost("materialize")]
    [ProducesResponseType<MaterializeProjectAssetPlanResponse>(StatusCodes.Status200OK)]
    public Task<MaterializeProjectAssetPlanResponse> Materialize(
        Guid projectId,
        [FromBody] MaterializeProjectAssetPlanRequest request,
        CancellationToken cancellationToken) =>
        projectAssetService.MaterializeAsync(
            projectId,
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

    [HttpPut("{projectAssetId:guid}")]
    [ProducesResponseType<ProjectAssetSummary>(StatusCodes.Status200OK)]
    public Task<ProjectAssetSummary> Update(
        Guid projectId,
        Guid projectAssetId,
        [FromBody] UpdateProjectAssetRequest request,
        CancellationToken cancellationToken) =>
        projectAssetService.UpdateAsync(
            projectId,
            projectAssetId,
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

    [HttpPost("{projectAssetId:guid}/lock")]
    [ProducesResponseType<ProjectAssetSummary>(StatusCodes.Status200OK)]
    public Task<ProjectAssetSummary> Lock(
        Guid projectId,
        Guid projectAssetId,
        [FromBody] ChangeProjectAssetLockRequest request,
        CancellationToken cancellationToken) =>
        projectAssetService.LockAsync(
            projectId,
            projectAssetId,
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

    [HttpPost("{projectAssetId:guid}/unlock")]
    [ProducesResponseType<ProjectAssetSummary>(StatusCodes.Status200OK)]
    public Task<ProjectAssetSummary> Unlock(
        Guid projectId,
        Guid projectAssetId,
        [FromBody] ChangeProjectAssetLockRequest request,
        CancellationToken cancellationToken) =>
        projectAssetService.UnlockAsync(
            projectId,
            projectAssetId,
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

    [HttpPost("approve-ai")]
    [ProducesResponseType<ApproveAiProjectAssetsResponse>(StatusCodes.Status200OK)]
    public Task<ApproveAiProjectAssetsResponse> ApproveAiAssets(
        Guid projectId,
        [FromBody] ApproveAiProjectAssetsRequest request,
        CancellationToken cancellationToken) =>
        projectAssetService.ApproveAiAssetsAsync(
            projectId,
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

    [HttpDelete("{projectAssetId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        Guid projectId,
        Guid projectAssetId,
        [FromBody] DeleteProjectAssetRequest request,
        CancellationToken cancellationToken)
    {
        await projectAssetService.DeleteAsync(
            projectId,
            projectAssetId,
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);
        return NoContent();
    }

    [HttpPut("scenes/{sceneId:guid}")]
    [ProducesResponseType<SceneAssetAssignmentSummary>(StatusCodes.Status200OK)]
    public Task<SceneAssetAssignmentSummary> UpdateSceneAssignments(
        Guid projectId,
        Guid sceneId,
        [FromBody] UpdateSceneAssetAssignmentsRequest request,
        CancellationToken cancellationToken) =>
        projectAssetService.UpdateSceneAssignmentsAsync(
            projectId,
            sceneId,
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

    [HttpPost("scenes/{sceneId:guid}/confirm")]
    [ProducesResponseType<ConfirmSceneProjectAssetsResponse>(StatusCodes.Status200OK)]
    public Task<ConfirmSceneProjectAssetsResponse> ConfirmSceneAssets(
        Guid projectId,
        Guid sceneId,
        [FromBody] ConfirmSceneProjectAssetsRequest request,
        CancellationToken cancellationToken) =>
        projectAssetService.ConfirmSceneAssetsAsync(
            projectId,
            sceneId,
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

    private Guid RequireDeviceId() =>
        Guid.TryParse(User.FindFirstValue(AuthClaimTypes.DeviceId), out var deviceId)
            ? deviceId
            : throw new AccountApiException(
                StatusCodes.Status401Unauthorized,
                "missing_device_claim",
                "Phiên đăng nhập không có thông tin thiết bị hợp lệ.");

    private string RequireUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new AccountApiException(
            StatusCodes.Status401Unauthorized,
            "missing_user_claim",
            "Phiên đăng nhập không hợp lệ.");
}

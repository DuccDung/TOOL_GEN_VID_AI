using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Generation;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("ai-gateway")]
[Route("api/projects/{projectId:guid}/scenes/{sceneId:guid}/first-frames")]
public sealed class SceneFirstFramesController(ISceneFirstFrameService service) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting("ai-status")]
    [ProducesResponseType<SceneFirstFrameListResponse>(StatusCodes.Status200OK)]
    public Task<SceneFirstFrameListResponse> List(
        Guid projectId,
        Guid sceneId,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken) =>
        service.ListAsync(projectId, sceneId, organizationId, RequireUserId(), RequireDeviceId(), cancellationToken);

    [HttpGet("/api/projects/{projectId:guid}/scene-first-frames")]
    [EnableRateLimiting("ai-status")]
    [ProducesResponseType<ProjectSceneFirstFrameListResponse>(StatusCodes.Status200OK)]
    public Task<ProjectSceneFirstFrameListResponse> ListProject(
        Guid projectId,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken) =>
        service.ListProjectAsync(projectId, organizationId, RequireUserId(), RequireDeviceId(), cancellationToken);

    [HttpGet("quote")]
    [ProducesResponseType<SceneFirstFrameQuoteResponse>(StatusCodes.Status200OK)]
    public Task<SceneFirstFrameQuoteResponse> GetQuote(
        Guid projectId,
        Guid sceneId,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken) =>
        service.GetQuoteAsync(projectId, sceneId, organizationId, RequireUserId(), RequireDeviceId(), cancellationToken);

    [HttpPost("materialize")]
    [ProducesResponseType<SceneFirstFrameSummary>(StatusCodes.Status200OK)]
    public Task<SceneFirstFrameSummary> Materialize(
        Guid projectId,
        Guid sceneId,
        [FromBody] MaterializeSceneFirstFrameRequest request,
        CancellationToken cancellationToken) =>
        service.MaterializeAsync(projectId, sceneId, request, RequireUserId(), RequireDeviceId(), cancellationToken);

    [HttpPost("{frameId:guid}/approve")]
    [ProducesResponseType<SceneFirstFrameSummary>(StatusCodes.Status200OK)]
    public Task<SceneFirstFrameSummary> Approve(
        Guid projectId,
        Guid sceneId,
        Guid frameId,
        [FromBody] ChangeSceneFirstFrameStatusRequest request,
        CancellationToken cancellationToken) =>
        service.ApproveAsync(projectId, sceneId, frameId, request, RequireUserId(), RequireDeviceId(), cancellationToken);

    [HttpPost("{frameId:guid}/reject")]
    [ProducesResponseType<SceneFirstFrameSummary>(StatusCodes.Status200OK)]
    public Task<SceneFirstFrameSummary> Reject(
        Guid projectId,
        Guid sceneId,
        Guid frameId,
        [FromBody] ChangeSceneFirstFrameStatusRequest request,
        CancellationToken cancellationToken) =>
        service.RejectAsync(projectId, sceneId, frameId, request, RequireUserId(), RequireDeviceId(), cancellationToken);

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

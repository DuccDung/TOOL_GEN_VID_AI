using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Vietsub;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize]
[Route("api/vietsub/projects")]
public sealed class VietsubProjectsController(IVietsubProjectService projectService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<VietsubProjectResponse>> List(
        [FromQuery] Guid organizationId,
        CancellationToken cancellationToken) =>
        projectService.ListAsync(organizationId, RequestContext(), cancellationToken);

    [HttpGet("{projectId:guid}")]
    public Task<VietsubProjectResponse> Get(
        Guid projectId,
        [FromQuery] Guid organizationId,
        CancellationToken cancellationToken) =>
        projectService.GetAsync(projectId, organizationId, RequestContext(), cancellationToken);

    [HttpPost]
    [ProducesResponseType<VietsubProjectResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<VietsubProjectResponse>> Create(
        CreateVietsubProjectRequest request,
        CancellationToken cancellationToken)
    {
        var project = await projectService.CreateAsync(request, RequestContext(), cancellationToken);
        return Created($"/api/vietsub/projects/{project.ProjectId:D}?organizationId={project.OrganizationId:D}", project);
    }

    [HttpPut("{projectId:guid}")]
    public Task<VietsubProjectResponse> Rename(
        Guid projectId,
        RenameVietsubProjectRequest request,
        CancellationToken cancellationToken) =>
        projectService.RenameAsync(projectId, request, RequestContext(), cancellationToken);

    [HttpDelete("{projectId:guid}")]
    public Task<VietsubProjectResponse> Archive(
        Guid projectId,
        [FromQuery] Guid organizationId,
        CancellationToken cancellationToken) =>
        projectService.ArchiveAsync(projectId, organizationId, RequestContext(), cancellationToken);

    private VietsubProjectRequestContext RequestContext() =>
        new(
            User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new AccountApiException(
                    StatusCodes.Status401Unauthorized,
                    "missing_user_claim",
                    "Phiên đăng nhập không hợp lệ."),
            Guid.TryParse(User.FindFirstValue(AuthClaimTypes.DeviceId), out var deviceId)
                ? deviceId
                : throw new AccountApiException(
                    StatusCodes.Status401Unauthorized,
                    "missing_device_claim",
                    "Phiên đăng nhập không có thông tin thiết bị hợp lệ."),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            HttpContext.TraceIdentifier);
}

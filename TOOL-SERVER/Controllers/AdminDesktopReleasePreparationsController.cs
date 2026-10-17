using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/desktop-release-preparations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminDesktopReleasePreparationsController(DesktopReleasePreparationService service) : ControllerBase
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    [HttpPost]
    [RequestSizeLimit(32768)]
    public async Task<ActionResult<DesktopReleasePreparationResponse>> Create(DesktopReleasePreparationRequest request, CancellationToken token)
    {
        var result = await service.CreateAsync(Actor, request, token);
        return Accepted($"/api/admin/desktop-release-preparations/{result.PreparationId:D}", result);
    }
    [HttpGet]
    [HttpGet("page")]
    public Task<PagedResponse<DesktopReleasePreparationResponse>> Page([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken token = default) =>
        service.PageAsync(page, pageSize, token);
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DesktopReleasePreparationResponse>> Get(Guid id, CancellationToken token) =>
        await service.GetAsync(id, token) is { } item ? Ok(item) : NotFound();
    [HttpGet("by-release/{releaseId:guid}")]
    public async Task<ActionResult<DesktopReleasePreparationResponse>> ByRelease(Guid releaseId, CancellationToken token) =>
        await service.ByReleaseAsync(releaseId, token) is { } item ? Ok(item) : NotFound();
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(32768)]
    public Task<DesktopReleasePreparationResponse> Update(Guid id, DesktopReleasePreparationRequest request, CancellationToken token) => service.UpdateAsync(id, Actor, request, token);
    [HttpPost("{id:guid}/retry")]
    public Task<DesktopReleasePreparationResponse> Retry(Guid id, DesktopReleasePreparationActionRequest request, CancellationToken token) => service.RetryAsync(id, request.Revision, token);
    [HttpPost("{id:guid}/cancel")]
    public Task<DesktopReleasePreparationResponse> Cancel(Guid id, DesktopReleasePreparationActionRequest request, CancellationToken token) => service.CancelAsync(id, request.Revision, token);
    [HttpPost("{id:guid}/publish")]
    public Task<DesktopReleasePreparationResponse> Publish(Guid id, DesktopReleasePreparationActionRequest request, CancellationToken token) => service.PublishAsync(id, request.Revision, token);
    [HttpPost("{id:guid}/deactivate")]
    public Task<DesktopReleasePreparationResponse> Deactivate(Guid id, DesktopReleasePreparationActionRequest request, CancellationToken token) => service.DeactivateAsync(id, request.Revision, token);
}

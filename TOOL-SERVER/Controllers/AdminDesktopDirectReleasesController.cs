using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/desktop-direct-releases")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminDesktopDirectReleasesController(DesktopDirectCatalogService service) : ControllerBase
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    [HttpGet]
    [HttpGet("page")]
    public Task<PagedResponse<AdminDesktopDirectReleaseResponse>> Page([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken token = default) => service.PageAsync(page, pageSize, token);
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminDesktopDirectReleaseResponse>> Get(Guid id, CancellationToken token) => await service.GetAsync(id, token) is { } item ? Ok(item) : NotFound();
    [HttpPost]
    [RequestSizeLimit(32768)]
    public async Task<ActionResult<AdminDesktopDirectReleaseResponse>> Create(DesktopDirectReleaseRequest request, CancellationToken token)
    {
        var item = await service.CreateAsync(Actor, request, token);
        return Created($"/api/admin/desktop-direct-releases/{item.ReleaseId:D}", item);
    }
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(32768)]
    public Task<AdminDesktopDirectReleaseResponse> Update(Guid id, DesktopDirectReleaseRequest request, CancellationToken token) => service.UpdateAsync(id, Actor, request, token);
    [HttpPost("{id:guid}/publish")]
    public Task<AdminDesktopDirectReleaseResponse> Publish(Guid id, DesktopDirectReleaseActionRequest request, CancellationToken token) => service.PublishAsync(id, request.Revision, token);
    [HttpPost("{id:guid}/deactivate")]
    public Task<AdminDesktopDirectReleaseResponse> Deactivate(Guid id, DesktopDirectReleaseActionRequest request, CancellationToken token) => service.DeactivateAsync(id, request.Revision, token);
}

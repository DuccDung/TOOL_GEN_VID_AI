using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/desktop-standalone-releases")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminDesktopStandaloneReleasesController(DesktopStandaloneCatalogService service) : ControllerBase
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    [HttpGet]
    [HttpGet("page")]
    public Task<PagedResponse<AdminDesktopStandaloneReleaseResponse>> Page([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken token = default) => service.PageAsync(page, pageSize, token);
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminDesktopStandaloneReleaseResponse>> Get(Guid id, CancellationToken token) => await service.GetAsync(id, token) is { } item ? Ok(item) : NotFound();
    [HttpPost]
    [RequestSizeLimit(32768)]
    public async Task<ActionResult<AdminDesktopStandaloneReleaseResponse>> Create(DesktopStandaloneReleaseRequest request, CancellationToken token)
    {
        var item = await service.CreateAsync(Actor, request, token);
        return Created($"/api/admin/desktop-standalone-releases/{item.ReleaseId:D}", item);
    }
    [HttpPost("{id:guid}/save")]
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(32768)]
    public Task<AdminDesktopStandaloneReleaseResponse> Save(Guid id, DesktopStandaloneReleaseRequest request, CancellationToken token) => service.UpdateAsync(id, Actor, request, token);
    [HttpPost("{id:guid}/publish")]
    public Task<AdminDesktopStandaloneReleaseResponse> Publish(Guid id, DesktopStandaloneActionRequest request, CancellationToken token) => service.PublishAsync(id, request.Revision, token);
    [HttpPost("{id:guid}/deactivate")]
    public Task<AdminDesktopStandaloneReleaseResponse> Deactivate(Guid id, DesktopStandaloneActionRequest request, CancellationToken token) => service.DeactivateAsync(id, request.Revision, token);
}

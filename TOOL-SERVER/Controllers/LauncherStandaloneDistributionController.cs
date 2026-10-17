using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SERVER.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/launcher-distribution/standalone")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LauncherStandaloneDistributionController(DesktopStandaloneCatalogService service) : ControllerBase
{
    [HttpGet("versions")]
    public async Task<DesktopStandaloneReleaseListResponse> Versions([FromQuery] string profile = "Production", [FromQuery] string channel = "Stable",
        [FromQuery] string platform = "win-x64", CancellationToken token = default, [FromQuery] string? setupVersion = null) =>
        new(await service.VisibleAsync(profile, channel, platform, token, setupVersion));
    [HttpGet("versions/{id:guid}")]
    public async Task<ActionResult<DesktopStandaloneReleaseResponse>> Exact(Guid id, [FromQuery] string profile = "Production", CancellationToken token = default) =>
        await service.ExactAsync(id, profile, token) is { } item ? Ok(item) : NotFound();
    [HttpGet("latest")]
    public async Task<ActionResult<DesktopStandaloneReleaseResponse>> Latest([FromQuery] string profile = "Production", [FromQuery] string channel = "Stable",
        [FromQuery] string platform = "win-x64", CancellationToken token = default, [FromQuery] string? setupVersion = null) =>
        (await service.VisibleAsync(profile, channel, platform, token, setupVersion)).FirstOrDefault() is { } item ? Ok(item) : NotFound();
}

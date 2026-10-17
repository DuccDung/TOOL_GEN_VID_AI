using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SERVER.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/launcher-distribution/direct")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LauncherDirectDistributionController(DesktopDirectCatalogService service) : ControllerBase
{
    [HttpGet("versions")]
    public async Task<DesktopDirectReleaseListResponse> Versions([FromQuery] string profile = "Production", [FromQuery] string channel = "Stable",
        [FromQuery] string platform = "win-x64", [FromQuery] Guid? bootstrapperId = null, CancellationToken token = default,
        [FromQuery] string? bootstrapperVersion = null) =>
        new(await service.VisibleAsync(profile, channel, platform, bootstrapperId, token, bootstrapperVersion));
    [HttpGet("versions/{id:guid}")]
    public async Task<ActionResult<DesktopDirectReleaseResponse>> Exact(Guid id, [FromQuery] string profile = "Production", [FromQuery] Guid? bootstrapperId = null,
        CancellationToken token = default) => await service.ExactAsync(id, profile, bootstrapperId, token) is { } item ? Ok(item) : NotFound();
    [HttpGet("latest")]
    public async Task<ActionResult<DesktopDirectReleaseResponse>> Latest([FromQuery] string profile = "Production", [FromQuery] string channel = "Stable",
        [FromQuery] string platform = "win-x64", [FromQuery] Guid? bootstrapperId = null, CancellationToken token = default,
        [FromQuery] string? bootstrapperVersion = null) =>
        (await service.VisibleAsync(profile, channel, platform, bootstrapperId, token, bootstrapperVersion)).FirstOrDefault() is { } item ? Ok(item) : NotFound();
}

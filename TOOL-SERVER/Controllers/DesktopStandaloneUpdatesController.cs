using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize]
[Route("api/desktop-updates/standalone")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DesktopStandaloneUpdatesController(DesktopStandaloneCatalogService service) : ControllerBase
{
    [HttpGet("check")]
    public async Task<DesktopStandaloneUpdateCheckResponse> Check([FromQuery] string version, [FromQuery] int buildNumber = 0,
        [FromQuery] string profile = "Production", [FromQuery] string channel = "Stable", [FromQuery] string platform = "win-x64", CancellationToken token = default)
    {
        if (!DesktopReleaseVersionPolicy.IsSupportedStableVersion(version)) throw DesktopSharedReleaseValidation.Invalid("desktop_standalone_version_invalid", "Phiên bản desktop không hợp lệ.");
        var latest = (await service.VisibleAsync(profile, channel, platform, token)).FirstOrDefault();
        return latest is not null && DesktopReleaseVersionPolicy.CompareVersions(latest.Version, version) > 0
            ? new(true, latest.IsMandatory, latest) : new(false, false, null);
    }
    [HttpGet("repair")]
    public async Task<ActionResult<DesktopStandaloneReleaseResponse>> Repair([FromQuery] string version, [FromQuery] int buildNumber,
        [FromQuery] string profile = "Production", [FromQuery] string channel = "Stable", [FromQuery] string platform = "win-x64", CancellationToken token = default)
    {
        if (!DesktopReleaseVersionPolicy.IsSupportedStableVersion(version) || buildNumber <= 0) return BadRequest();
        var item = (await service.VisibleAsync(profile, channel, platform, token)).SingleOrDefault(x => x.Version == version && x.DeclaredBuildNumber == buildNumber);
        return item is null ? NotFound() : Ok(item);
    }
    [HttpGet("versions/{id:guid}")]
    public async Task<ActionResult<DesktopStandaloneReleaseResponse>> Exact(Guid id, [FromQuery] string profile = "Production", CancellationToken token = default) =>
        await service.ExactAsync(id, profile, token) is { } item ? Ok(item) : NotFound();
}

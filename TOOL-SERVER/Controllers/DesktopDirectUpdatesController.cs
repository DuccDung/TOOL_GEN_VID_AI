using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize]
[Route("api/desktop-updates/direct")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DesktopDirectUpdatesController(DesktopDirectCatalogService service) : ControllerBase
{
    [HttpGet("check")]
    public async Task<DesktopDirectUpdateCheckResponse> Check([FromQuery] string version, [FromQuery] int buildNumber = 0,
        [FromQuery] string profile = "Production", [FromQuery] string channel = "Stable", [FromQuery] string platform = "win-x64", CancellationToken token = default)
    {
        if (!DesktopReleaseVersionPolicy.IsSupportedStableVersion(version)) throw DesktopSharedReleaseValidation.Invalid("desktop_direct_version_invalid", "Phiên bản desktop không hợp lệ.");
        var latest = (await service.VisibleAsync(profile, channel, platform, null, token)).FirstOrDefault();
        return latest is not null && DesktopReleaseVersionPolicy.CompareVersions(latest.Version, version) > 0
            ? new(true, latest.IsMandatory, latest) : new(false, false, null);
    }
    [HttpGet("repair")]
    public async Task<ActionResult<DesktopDirectReleaseResponse>> Repair([FromQuery] string version, [FromQuery] int buildNumber,
        [FromQuery] string profile = "Production", [FromQuery] string channel = "Stable", [FromQuery] string platform = "win-x64", CancellationToken token = default)
    {
        if (!DesktopReleaseVersionPolicy.IsSupportedStableVersion(version) || buildNumber <= 0) return BadRequest();
        var item = (await service.VisibleAsync(profile, channel, platform, null, token)).SingleOrDefault(x => x.Version == version && x.DeclaredBuildNumber == buildNumber);
        return item is null ? NotFound() : Ok(item);
    }
    [HttpGet("versions/{id:guid}")]
    public async Task<ActionResult<DesktopDirectReleaseResponse>> Exact(Guid id, [FromQuery] string profile = "Production", [FromQuery] Guid? bootstrapperId = null,
        CancellationToken token = default) => await service.ExactAsync(id, profile, bootstrapperId, token) is { } item ? Ok(item) : NotFound();
}

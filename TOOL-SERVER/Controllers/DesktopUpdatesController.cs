using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize]
[Route("api/desktop-updates")]
public sealed class DesktopUpdatesController(IDesktopReleaseService releaseService) : ControllerBase
{
    [HttpGet("check")]
    public async Task<DesktopUpdateCheckResponse> Check(
        [FromQuery] string version,
        [FromQuery] int buildNumber,
        [FromQuery] string channel = "Stable",
        [FromQuery] string platform = "win-x64",
        CancellationToken cancellationToken = default,
        [FromQuery] int distributionProtocol = DesktopDistributionProtocol.Legacy,
        [FromQuery] string profile = "Production")
    {
        if (distributionProtocol == DesktopDistributionProtocol.Shared && !releaseService.SupportsSharedProfile(profile)) return new(false, false, null);
        var package = await releaseService.GetLatestPackageAsync(platform, channel, distributionProtocol, cancellationToken);
        if (package is null ||
            !(distributionProtocol == DesktopDistributionProtocol.Shared
                ? DesktopReleaseVersionPolicy.IsReleaseNewerVersionFirst(package.Release.Version, package.Release.BuildNumber, version, buildNumber)
                : DesktopVersionComparer.IsReleaseNewer(package.Release.Version, package.Release.BuildNumber, version, buildNumber)))
        {
            return new DesktopUpdateCheckResponse(false, false, null);
        }

        var mandatory = package.Release.IsMandatory ||
                        (!string.IsNullOrWhiteSpace(package.Release.MinimumSupportedDesktopVersion) &&
                         DesktopVersionComparer.CompareVersions(version, package.Release.MinimumSupportedDesktopVersion) < 0);
        return new DesktopUpdateCheckResponse(true, mandatory, Map(package));
    }

    [HttpGet("repair")]
    public async Task<ActionResult<DesktopReleaseResponse>> Repair(
        [FromQuery] string version,
        [FromQuery] int buildNumber,
        [FromQuery] string channel = "Stable",
        [FromQuery] string platform = "win-x64",
        CancellationToken cancellationToken = default,
        [FromQuery] int distributionProtocol = DesktopDistributionProtocol.Legacy,
        [FromQuery] string profile = "Production")
    {
        if (distributionProtocol == DesktopDistributionProtocol.Shared && !releaseService.SupportsSharedProfile(profile)) return NotFound();
        if (string.IsNullOrWhiteSpace(version) || buildNumber <= 0)
        {
            return BadRequest();
        }

        var packages = await releaseService.GetVisiblePackagesAsync(platform, channel, distributionProtocol, cancellationToken);
        var package = packages.FirstOrDefault(candidate =>
            candidate.Release.BuildNumber == buildNumber &&
            string.Equals(candidate.Release.Version, version.Trim(), StringComparison.OrdinalIgnoreCase));
        return package is null
            ? NotFound(new ApiErrorResponse(DesktopRepairErrorCodes.PackageNotFound,
                "Chưa có package sửa chữa đang phát hành cho đúng phiên bản desktop này."))
            : Ok(Map(package));
    }

    private static DesktopReleaseResponse Map(DesktopReleasePackage package) =>
        LauncherDistributionController.MapPackage(package);
}

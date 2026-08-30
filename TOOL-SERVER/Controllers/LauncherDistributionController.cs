using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Domain.Updates;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SERVER.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/launcher-distribution")]
public sealed class LauncherDistributionController(
    IDesktopReleaseService releaseService,
    IDesktopReleaseStorage storage) : ControllerBase
{
    [HttpGet("latest")]
    public async Task<ActionResult<DesktopReleaseResponse>> Latest(
        [FromQuery] string channel = "Stable",
        [FromQuery] string platform = "win-x64",
        CancellationToken cancellationToken = default)
    {
        var package = await releaseService.GetLatestPackageAsync(platform, channel, cancellationToken);
        return package is null ? NotFound() : Ok(MapPackage(package));
    }

    [HttpGet("versions")]
    public async Task<DesktopReleaseListResponse> Versions(
        [FromQuery] string channel = "Stable",
        [FromQuery] string platform = "win-x64",
        CancellationToken cancellationToken = default)
    {
        var packages = await releaseService.GetVisiblePackagesAsync(platform, channel, cancellationToken);
        return new DesktopReleaseListResponse(packages.Select(MapPackage).ToArray());
    }

    [HttpGet("versions/{releaseId:guid}/download")]
    public async Task<IActionResult> Download(Guid releaseId, CancellationToken cancellationToken)
    {
        var package = await releaseService.GetVisiblePackageAsync(releaseId, cancellationToken);
        return package is null ? NotFound() : PhysicalArtifact(package, "application/zip");
    }

    [HttpGet("setup/latest/download")]
    public async Task<IActionResult> DownloadLatestSetup(
        [FromQuery] string channel = "Stable",
        [FromQuery] string platform = "win-x64",
        CancellationToken cancellationToken = default)
    {
        var package = await releaseService.GetLatestArtifactAsync(
            platform,
            channel,
            DesktopArtifactKinds.Setup,
            cancellationToken);
        return package is null ? NotFound() : PhysicalArtifact(package, "application/vnd.microsoft.portable-executable");
    }

    internal static DesktopReleaseResponse MapPackage(DesktopReleasePackage package) =>
        new(
            package.Release.AppReleaseId,
            "VideoMaker",
            package.Release.Version,
            package.Release.BuildNumber,
            package.Release.Channel,
            package.Release.Platform,
            package.Release.MinimumSupportedDesktopVersion,
            package.Release.ReleaseNotes,
            package.Release.PublishedAtUtc,
            package.Artifact.FileName,
            $"/api/launcher-distribution/versions/{package.Release.AppReleaseId:D}/download",
            package.Artifact.SizeBytes,
            package.Artifact.Sha256);

    private IActionResult PhysicalArtifact(DesktopReleasePackage package, string contentType)
    {
        var path = storage.ResolvePath(package.Artifact.RelativePath);
        return System.IO.File.Exists(path)
            ? PhysicalFile(path, contentType, package.Artifact.FileName, enableRangeProcessing: true)
            : NotFound();
    }
}

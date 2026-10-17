using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Domain.Updates;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Distribution.Updates;
using System.Security.Cryptography;

namespace TOOL_SERVER.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/launcher-distribution")]
public sealed class LauncherDistributionController(
    IDesktopReleaseService releaseService,
    IDesktopReleaseStorage storage,
    DesktopBootstrapperService? bootstrappers = null,
    DesktopDownloadSettingsService? downloadSettings = null) : ControllerBase
{
    [HttpGet("bootstrapper")]
    public async Task<ActionResult<DesktopSharedBootstrapperResponse>> Bootstrapper([FromQuery] string profile = "Production",
        [FromQuery] int distributionProtocol = DesktopDistributionProtocol.Shared, CancellationToken token = default)
    {
        if (distributionProtocol != DesktopDistributionProtocol.Shared || bootstrappers is null || profile != bootstrappers.Status().Profile) return NotFound();
        var item = await bootstrappers.ActiveAsync(null, token);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpGet("bootstrappers/{bootstrapperId:guid}/download")]
    public async Task<IActionResult> DownloadBootstrapper(Guid bootstrapperId, CancellationToken token)
    {
        if (bootstrappers is null || await bootstrappers.DownloadAsync(bootstrapperId, token) is not { } item) return NotFound();
        return PhysicalFile(item.Path, "application/vnd.microsoft.portable-executable", item.FileName, enableRangeProcessing: true);
    }

    [HttpGet("versions/{releaseId:guid}")]
    public async Task<ActionResult<DesktopReleaseResponse>> Exact(Guid releaseId,
        [FromQuery] int distributionProtocol = DesktopDistributionProtocol.Shared, [FromQuery] string profile = "Production",
        [FromQuery] Guid? bootstrapperId = null, CancellationToken token = default)
    {
        if (distributionProtocol != DesktopDistributionProtocol.Shared) return NotFound();
        var item = await releaseService.GetSharedPackageAsync(releaseId, bootstrapperId, profile, token);
        return item is null ? NotFound() : Ok(MapPackage(item));
    }

    [HttpGet("versions/{releaseId:guid}/bootstrappers/{bootstrapperId:guid}/package-metadata/{sha256}")]
    public async Task<IActionResult> PackageMetadata(Guid releaseId, Guid bootstrapperId, string sha256, CancellationToken token = default)
    {
        if (bootstrappers is null || sha256.Length != 64 || sha256.Any(c => !char.IsAsciiHexDigit(c))) return NotFound();
        var package = await releaseService.GetSharedPackageAsync(releaseId, bootstrapperId, bootstrappers.Status().Profile, token);
        if (package?.PackageMetadata is not { } metadata) return NotFound();
        var bytes = DesktopPackageMetadataPolicy.SerializeDocument(metadata);
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!actualHash.Equals(sha256, StringComparison.Ordinal)) return NotFound();
        return File(bytes, "application/json");
    }
    [HttpGet("latest")]
    public async Task<ActionResult<DesktopReleaseResponse>> Latest(
        [FromQuery] string channel = "Stable",
        [FromQuery] string platform = "win-x64",
        CancellationToken cancellationToken = default,
        [FromQuery] int distributionProtocol = DesktopDistributionProtocol.Legacy,
        [FromQuery] string profile = "Production")
    {
        if (distributionProtocol == DesktopDistributionProtocol.Shared && !releaseService.SupportsSharedProfile(profile)) return NotFound();
        var package = await releaseService.GetLatestPackageAsync(platform, channel, distributionProtocol, cancellationToken);
        return package is null ? NotFound() : Ok(MapPackage(package));
    }

    [HttpGet("versions")]
    public async Task<DesktopReleaseListResponse> Versions(
        [FromQuery] string channel = "Stable",
        [FromQuery] string platform = "win-x64",
        CancellationToken cancellationToken = default,
        [FromQuery] int distributionProtocol = DesktopDistributionProtocol.Legacy,
        [FromQuery] string profile = "Production")
    {
        if (distributionProtocol == DesktopDistributionProtocol.Shared && !releaseService.SupportsSharedProfile(profile)) return new([]);
        var packages = await releaseService.GetVisiblePackagesAsync(platform, channel, distributionProtocol, cancellationToken);
        return new DesktopReleaseListResponse(packages.Select(MapPackage).ToArray());
    }

    [HttpGet("versions/{releaseId:guid}/download")]
    public async Task<IActionResult> Download(Guid releaseId, CancellationToken cancellationToken)
    {
        var package = await releaseService.GetVisiblePackageAsync(releaseId, cancellationToken);
        if (package is null) return NotFound();
        if (package.Artifact.SourceKind == DesktopArtifactSourceKinds.ExternalVps)
            return StatusCode(StatusCodes.Status410Gone, new ApiErrorResponse("desktop_external_direct_download_required",
                "Dùng URL tải trực tiếp trong metadata distribution protocol 2."));
        return PhysicalArtifact(package, "application/zip");
    }

    [HttpGet("versions/{releaseId:guid}/setup")]
    public async Task<ActionResult<DesktopSetupArtifactResponse>> Setup(Guid releaseId,
        [FromQuery] int distributionProtocol = DesktopDistributionProtocol.Legacy, CancellationToken cancellationToken = default)
    {
        var setup = await releaseService.GetVisibleSetupAsync(releaseId, distributionProtocol, cancellationToken);
        return setup is null ? NotFound() : Ok(MapSetup(setup.Release));
    }

    [HttpGet("versions/{releaseId:guid}/setup/download")]
    public async Task<IActionResult> DownloadSetup(Guid releaseId, CancellationToken cancellationToken)
    {
        var setup = await releaseService.GetVisibleSetupAsync(releaseId, DesktopDistributionProtocol.Current, cancellationToken);
        return setup is null ? NotFound() : PhysicalArtifact(setup, "application/vnd.microsoft.portable-executable");
    }

    [HttpGet("setup/latest/download")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DownloadLatestSetup(
        [FromQuery] string channel = "Stable",
        [FromQuery] string platform = "win-x64",
        CancellationToken cancellationToken = default)
    {
        if (HttpContext is not null) Response.Headers.CacheControl = "no-store";
        if (channel != "Stable" || platform != "win-x64") return NotFound();
        var url = downloadSettings is null ? null : await downloadSettings.GetPublicDownloadUrlAsync(cancellationToken);
        return url is null
            ? new ContentResult
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable, ContentType = "text/plain; charset=utf-8",
                Content = "Bộ cài Windows chưa có link tải. Vui lòng thử lại sau."
            }
            : Redirect(url);
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
            package.Artifact.SourceKind == DesktopArtifactSourceKinds.ExternalVps ? package.Artifact.ExternalDownloadUrl!
                : $"/api/launcher-distribution/versions/{package.Release.AppReleaseId:D}/download",
            package.Artifact.SizeBytes,
            package.Artifact.Sha256, package.Artifact.SourceKind, package.Artifact.InstallMode, package.Artifact.DistributionProtocol,
            package.Artifact.InstallMode == DesktopInstallModes.StaticArchive && package.Artifact.DistributionProtocol == DesktopDistributionProtocol.Current ? MapSetup(package.Release) : null,
            package.Artifact.ModelBundleId, package.SharedBootstrapper, package.Profile, package.MinimumBootstrapperVersion, MapMetadata(package));

    private static DesktopPackageMetadataArtifactResponse? MapMetadata(DesktopReleasePackage package)
    {
        if (package.PackageMetadata is not { } document || package.SharedBootstrapper is not { } bootstrapper) return null;
        var bytes = DesktopPackageMetadataPolicy.SerializeDocument(document);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new(1,
            DesktopPackageMetadataPolicy.MetadataPath(package.Release.AppReleaseId, bootstrapper.BootstrapperId, hash),
            bytes.LongLength, hash, package.Artifact.Sha256);
    }

    internal static DesktopSetupArtifactResponse MapSetup(AppRelease release)
    {
        var package = release.Artifacts.Single(x => x.Kind == DesktopArtifactKinds.DesktopPackage);
        var setup = release.Artifacts.Single(x => x.Kind == DesktopArtifactKinds.Setup);
        return new(release.AppReleaseId, release.Version, release.BuildNumber, release.Channel, release.Platform,
            package.Sha256, setup.FileName, $"/api/launcher-distribution/versions/{release.AppReleaseId:D}/setup/download",
            setup.SizeBytes, setup.Sha256, setup.InstallMode, setup.DistributionProtocol, setup.ModelBundleId);
    }

    private IActionResult PhysicalArtifact(DesktopReleasePackage package, string contentType)
    {
        if (package.Artifact.SourceKind != DesktopArtifactSourceKinds.Internal || string.IsNullOrWhiteSpace(package.Artifact.RelativePath))
            return NotFound();
        var path = storage.ResolvePath(package.Artifact.RelativePath);
        return System.IO.File.Exists(path)
            ? PhysicalFile(path, contentType, package.Artifact.FileName, enableRangeProcessing: true)
            : NotFound();
    }
}

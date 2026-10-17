using System.Text.RegularExpressions;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;

namespace TOOL_SHARED.Distribution.Updates;

/// <summary>Administrator-declared file identities; no registry, download, or measured remote size.</summary>
public static partial class DesktopStandaloneCatalogPolicy
{
    public static DesktopStandaloneSetupResponse ParseSetupUrl(Uri apiOrigin, string downloadUrl,
        IReadOnlyList<string>? approvedPrefixes = null)
    {
        _ = DesktopArtifactDownloadPolicy.Validate(apiOrigin, downloadUrl, DesktopArtifactSourceKinds.ExternalVps);
        var uri = DesktopArtifactDownloadPolicy.Validate(apiOrigin, downloadUrl, DesktopArtifactSourceKinds.ExternalVps, approvedPrefixes);
        var name = uri.Segments[^1];
        if (!DesktopMaintenanceProtocol.IsFileName(name, ".exe")) throw Invalid();
        var match = SetupName().Match(name);
        if (!match.Success || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(match.Groups["version"].Value)
            || DesktopReleaseVersionPolicy.CompareVersions(match.Groups["version"].Value, DesktopStandaloneCatalogProtocol.MinimumSetupVersion) < 0)
            throw Invalid();
        return new(match.Groups["version"].Value, name, uri.AbsoluteUri, match.Groups["sha"].Value);
    }

    public static void ValidateSetup(DesktopStandaloneSetupResponse setup, Uri apiOrigin, string? minimumVersion = null)
    {
        if (setup is null || setup != ParseSetupUrl(apiOrigin, setup.DownloadUrl)
            || minimumVersion is not null && (!DesktopReleaseVersionPolicy.IsSupportedStableVersion(minimumVersion)
                || DesktopReleaseVersionPolicy.CompareVersions(setup.Version, minimumVersion) < 0)) throw Invalid();
    }

    public static void ValidateRelease(DesktopStandaloneReleaseResponse release, Uri? apiOrigin = null, string? profile = null)
    {
        if (release is null || release.ReleaseId == Guid.Empty || release.Revision == Guid.Empty
            || release.CatalogProtocol != DesktopStandaloneCatalogProtocol.Version
            || release.ApplicationDistributionProtocol != DesktopDistributionProtocol.Shared
            || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(release.Version) || release.DeclaredBuildNumber <= 0
            || release.Channel != "Stable" || release.Platform != "win-x64"
            || string.IsNullOrWhiteSpace(release.Profile) || release.Profile.Length > 50
            || profile is not null && release.Profile != profile || release.ModelBundleId != ApprovedStaticModelBundle.Current.BundleId
            || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(release.MinimumSetupVersion)
            || DesktopReleaseVersionPolicy.CompareVersions(release.MinimumSetupVersion, DesktopStandaloneCatalogProtocol.MinimumSetupVersion) < 0
            || !Uri.TryCreate(release.ApiBaseUrl, UriKind.Absolute, out var api) || api.Scheme != "https" || api.IsLoopback
            || api.UserInfo.Length != 0 || api.Query.Length != 0 || api.Fragment.Length != 0
            || apiOrigin is not null && !DesktopSharedBootstrapperPolicy.SameApi(apiOrigin, api)) throw Invalid();
        var name = DesktopDirectCatalogPolicy.ParseUrl(apiOrigin ?? api, release.DownloadUrl);
        if (name.Version != release.Version || name.BuildNumber != release.DeclaredBuildNumber || name.Sha256 != release.ExpectedSha256
            || new Uri(release.DownloadUrl).Segments[^1] != release.FileName) throw Invalid();
        if (release.Setup is not null) ValidateSetup(release.Setup, apiOrigin ?? api, release.MinimumSetupVersion);
    }

    internal static void ValidateMeasuredPackage(DesktopReleaseResponse release, Uri api)
    {
        if (release.ReleaseId == Guid.Empty || release.ProductName != "VideoMaker" || release.SharedBootstrapper is not null
            || release.SetupArtifact is not null || release.PackageMetadata is not null
            || release.DistributionProtocol != DesktopDistributionProtocol.Shared || release.InstallMode != DesktopInstallModes.StaticArchive
            || release.SourceKind != DesktopArtifactSourceKinds.ExternalVps || release.SizeBytes is <= 0 or > DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes
            || !DesktopMaintenanceProtocol.IsHash(release.Sha256) || release.Channel != "Stable" || release.Platform != "win-x64"
            || string.IsNullOrWhiteSpace(release.Profile) || release.ModelBundleId != ApprovedStaticModelBundle.Current.BundleId
            || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(release.MinimumBootstrapperVersion)
            || DesktopReleaseVersionPolicy.CompareVersions(release.MinimumBootstrapperVersion, DesktopStandaloneCatalogProtocol.MinimumSetupVersion) < 0)
            throw Invalid();
        var name = DesktopDirectCatalogPolicy.ParseUrl(api, release.DownloadUrl);
        if (name.Version != release.Version || name.BuildNumber != release.BuildNumber || name.Sha256 != release.Sha256
            || new Uri(release.DownloadUrl).Segments[^1] != release.FileName) throw Invalid();
    }

    private static InvalidDataException Invalid() => new("Catalog hoặc link Setup không hợp lệ. Tự cập nhật cần link Setup do công cụ đóng gói tạo, có phiên bản và SHA-256 trong tên file.");
    [GeneratedRegex(@"\ASetup-(?<version>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))-win-x64-(?<sha>[0-9a-f]{64})\.exe\z", RegexOptions.CultureInvariant)]
    private static partial Regex SetupName();
}

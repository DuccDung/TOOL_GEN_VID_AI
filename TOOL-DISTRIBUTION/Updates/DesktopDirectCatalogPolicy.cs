using System.Globalization;
using System.Text.RegularExpressions;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;

namespace TOOL_SHARED.Distribution.Updates;

public sealed record DesktopDirectPackageName(string Version, int BuildNumber, string Sha256);

/// <summary>Pure validation: a declared catalog never downloads or resolves a VPS address.</summary>
public static partial class DesktopDirectCatalogPolicy
{
    public static DesktopDirectPackageName ParseFileName(string fileName)
    {
        if (!DesktopMaintenanceProtocol.IsFileName(fileName, ".zip")) throw Invalid();
        var match = Name().Match(fileName);
        if (!match.Success || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(match.Groups["version"].Value)
            || !int.TryParse(match.Groups["build"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var build) || build <= 0)
            throw Invalid();
        return new(match.Groups["version"].Value, build, match.Groups["sha"].Value);
    }

    public static DesktopDirectPackageName ParseUrl(Uri apiOrigin, string downloadUrl, IReadOnlyList<string>? approvedPrefixes = null)
    {
        _ = DesktopArtifactDownloadPolicy.Validate(apiOrigin, downloadUrl, DesktopArtifactSourceKinds.ExternalVps);
        var uri = DesktopArtifactDownloadPolicy.Validate(apiOrigin, downloadUrl, DesktopArtifactSourceKinds.ExternalVps, approvedPrefixes);
        return ParseFileName(uri.Segments[^1]);
    }

    public static string FileName(string version, int buildNumber, string sha256)
    {
        var value = $"VideoMaker-{version}-{buildNumber.ToString(CultureInfo.InvariantCulture)}-win-x64-{sha256}.zip";
        _ = ParseFileName(value);
        return value;
    }

    public static bool SupportsCatalog(DesktopSharedBootstrapperResponse bootstrapper, int catalogProtocol = DesktopDirectCatalogProtocol.Version) =>
        bootstrapper is not null && ValidCapabilities(bootstrapper.SupportedCatalogProtocols)
        && bootstrapper.SupportedCatalogProtocols.Split(',').Contains(catalogProtocol.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal)
        && (catalogProtocol != DesktopDirectCatalogProtocol.Version ||
            DesktopReleaseVersionPolicy.IsSupportedStableVersion(bootstrapper.Version) &&
            DesktopReleaseVersionPolicy.CompareVersions(bootstrapper.Version, DesktopDirectCatalogProtocol.MinimumBootstrapperVersion) >= 0);

    public static bool ValidCapabilities(string? value) => value is "3" or "3,4" or "3,4,5";

    public static void ValidateRelease(DesktopDirectReleaseResponse release, Uri? apiOrigin = null, string? profile = null)
    {
        if (release is null || release.ReleaseId == Guid.Empty || release.Revision == Guid.Empty
            || release.CatalogProtocol != DesktopDirectCatalogProtocol.Version
            || release.ApplicationDistributionProtocol != DesktopDistributionProtocol.Shared
            || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(release.Version) || release.DeclaredBuildNumber <= 0
            || release.Channel != "Stable" || release.Platform != "win-x64"
            || string.IsNullOrWhiteSpace(release.Profile) || release.Profile.Length > 50
            || profile is not null && release.Profile != profile
            || release.ModelBundleId != ApprovedStaticModelBundle.Current.BundleId
            || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(release.MinimumBootstrapperVersion)
            || DesktopReleaseVersionPolicy.CompareVersions(release.MinimumBootstrapperVersion, DesktopDirectCatalogProtocol.MinimumBootstrapperVersion) < 0
            || release.SharedBootstrapper is not { } bootstrapper || !SupportsCatalog(bootstrapper)) throw Invalid();
        DesktopSharedBootstrapperPolicy.ValidateBootstrapper(bootstrapper, apiOrigin, profile);
        var name = ParseUrl(apiOrigin ?? new Uri(bootstrapper.ApiBaseUrl), release.DownloadUrl);
        if (name.Version != release.Version || name.BuildNumber != release.DeclaredBuildNumber
            || name.Sha256 != release.ExpectedSha256 || new Uri(release.DownloadUrl).Segments[^1] != release.FileName
            || bootstrapper.Profile != release.Profile || bootstrapper.Channel != release.Channel
            || bootstrapper.Platform != release.Platform || bootstrapper.ModelBundleId != release.ModelBundleId
            || DesktopReleaseVersionPolicy.CompareVersions(bootstrapper.Version, release.MinimumBootstrapperVersion) < 0) throw Invalid();
    }

    private static InvalidDataException Invalid() => new("Thông tin phát hành trực tiếp không hợp lệ hoặc Setup chưa hỗ trợ. Tên ZIP cần chứa đúng phiên bản, build và SHA-256 do công cụ đóng gói tạo.");

    [GeneratedRegex(@"\AVideoMaker-(?<version>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))-(?<build>[1-9][0-9]*)-win-x64-(?<sha>[0-9a-f]{64})\.zip\z", RegexOptions.CultureInvariant)]
    private static partial Regex Name();
}

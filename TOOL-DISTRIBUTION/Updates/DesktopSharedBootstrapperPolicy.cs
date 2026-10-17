using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SHARED.Distribution.Updates;

/// <summary>Runtime binding for a reusable bootstrapper. Package identity remains operation-specific.</summary>
public static class DesktopSharedBootstrapperPolicy
{
    public static void ValidateRelease(DesktopReleaseResponse release, Uri? apiOrigin = null, string? profile = null)
    {
        ArgumentNullException.ThrowIfNull(release);
        var bootstrapper = release.SharedBootstrapper ?? throw new InvalidDataException("Release thiếu Setup dùng chung.");
        if (release.ReleaseId == Guid.Empty || release.ProductName != "VideoMaker" ||
            release.DistributionProtocol != DesktopDistributionProtocol.Shared || release.InstallMode != DesktopInstallModes.StaticArchive ||
            release.SetupArtifact is not null || release.SourceKind != DesktopArtifactSourceKinds.ExternalVps ||
            !DesktopReleaseVersionPolicy.IsSupportedStableVersion(release.Version) || release.BuildNumber <= 0 ||
            release.Platform != "win-x64" || string.IsNullOrWhiteSpace(release.Channel) ||
            !DesktopMaintenanceProtocol.IsHash(release.Sha256) ||
            release.SizeBytes is <= 0 or > DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes ||
            !DesktopMaintenanceProtocol.IsFileName(release.FileName, ".zip") ||
            string.IsNullOrWhiteSpace(release.ModelBundleId) || string.IsNullOrWhiteSpace(release.Profile) ||
            release.Profile.Length > 50 || profile is not null && release.Profile != profile ||
            !DesktopReleaseVersionPolicy.IsSupportedStableVersion(release.MinimumBootstrapperVersion))
            throw new InvalidDataException("Gói ứng dụng không có snapshot tương thích Setup dùng chung.");
        ValidateBootstrapper(bootstrapper, apiOrigin, profile);
        if (bootstrapper.Profile != release.Profile || bootstrapper.Channel != release.Channel ||
            bootstrapper.Platform != release.Platform || bootstrapper.ModelBundleId != release.ModelBundleId ||
            DesktopReleaseVersionPolicy.CompareVersions(bootstrapper.Version, release.MinimumBootstrapperVersion) < 0)
            throw new InvalidDataException("Setup hệ thống không hỗ trợ profile, giao thức hoặc model của gói ứng dụng.");
        DesktopArtifactDownloadPolicy.Validate(apiOrigin ?? new Uri(bootstrapper.ApiBaseUrl), release.DownloadUrl, release.SourceKind,
            DesktopArtifactDownloadPolicy.ApprovedExternalPrefixes);
        DesktopPackageMetadataPolicy.ValidateReference(release, apiOrigin ?? new Uri(bootstrapper.ApiBaseUrl));
    }

    public static void ValidateBootstrapper(DesktopSharedBootstrapperResponse bootstrapper, Uri? apiOrigin = null, string? profile = null)
    {
        if (bootstrapper is null || bootstrapper.BootstrapperId == Guid.Empty ||
            !DesktopReleaseVersionPolicy.IsSupportedStableVersion(bootstrapper.Version) ||
            bootstrapper.DistributionProtocol != DesktopDistributionProtocol.Shared || bootstrapper.Platform != "win-x64" ||
            string.IsNullOrWhiteSpace(bootstrapper.Profile) || bootstrapper.Profile.Length > 50 ||
            profile is not null && bootstrapper.Profile != profile || string.IsNullOrWhiteSpace(bootstrapper.Channel) ||
            string.IsNullOrWhiteSpace(bootstrapper.ModelBundleId) ||
            !DesktopMaintenanceProtocol.IsHash(bootstrapper.Sha256) ||
            bootstrapper.SizeBytes is <= 0 or > DesktopArtifactDownloadPolicy.MaximumSetupArtifactBytes ||
            !DesktopMaintenanceProtocol.IsFileName(bootstrapper.FileName, ".exe") ||
            !Uri.TryCreate(bootstrapper.ApiBaseUrl, UriKind.Absolute, out var declaredApi) ||
            declaredApi.Scheme != Uri.UriSchemeHttps || declaredApi.UserInfo.Length != 0 || declaredApi.Query.Length != 0 ||
            declaredApi.Fragment.Length != 0 || apiOrigin is not null && !SameApi(apiOrigin, declaredApi))
            throw new InvalidDataException("Metadata Setup hệ thống không hợp lệ hoặc khác API/profile của ứng dụng.");
        DesktopArtifactDownloadPolicy.Validate(apiOrigin ?? declaredApi, bootstrapper.DownloadUrl, DesktopArtifactSourceKinds.Internal);
    }

    public static bool SameApi(Uri left, Uri right) => DesktopArtifactDownloadPolicy.SameOrigin(left, right) &&
        left.AbsolutePath.TrimEnd('/').Equals(right.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal);
}

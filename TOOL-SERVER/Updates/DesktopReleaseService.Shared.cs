using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Domain.Updates;
using TOOL_SHARED.Contracts.Updates;
using static TOOL_SERVER.Updates.DesktopSharedReleaseValidation;

namespace TOOL_SERVER.Updates;

public sealed partial class DesktopReleaseService
{
    public bool SupportsSharedProfile(string profile) => _options.SharedBootstrapperEnabled && IsConfigured(_options) && profile == _options.Profile;
    public async Task<DesktopReleasePackage?> GetSharedPackageAsync(Guid releaseId, Guid? bootstrapperId, string profile, CancellationToken cancellationToken)
    {
        if (!_options.SharedBootstrapperEnabled || !IsConfigured(_options) || profile != _options.Profile) return null;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var release = await dbContext.AppReleases.AsNoTracking().Include(x => x.Artifacts)
            .SingleOrDefaultAsync(x => x.AppReleaseId == releaseId && x.IsActive && x.PublishedAtUtc <= now, cancellationToken);
        return release is null ? null : await TryCreateSharedPackageAsync(release, bootstrapperId, cancellationToken);
    }

    private async Task<IReadOnlyList<DesktopReleasePackage>> GetSharedVisiblePackagesAsync(string platform, string channel, CancellationToken token)
    {
        if (!_options.SharedBootstrapperEnabled || !IsConfigured(_options) || NormalizePlatform(platform) != "win-x64" || NormalizeChannel(channel) != "Stable") return [];
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var releases = await dbContext.AppReleases.AsNoTracking().Include(x => x.Artifacts)
            .Where(x => x.IsActive && x.Platform == "win-x64" && x.Channel == "Stable" && x.PublishedAtUtc <= now).ToListAsync(token);
        var packages = new List<DesktopReleasePackage>();
        foreach (var release in releases)
            if (await TryCreateSharedPackageAsync(release, null, token) is { } package) packages.Add(package);
        packages.Sort((a, b) =>
        {
            var version = DesktopReleaseVersionPolicy.CompareVersions(b.Release.Version, a.Release.Version);
            return version != 0 ? version : b.Release.BuildNumber.CompareTo(a.Release.BuildNumber);
        });
        return packages;
    }

    private async Task<DesktopReleasePackage?> TryCreateSharedPackageAsync(AppRelease release, Guid? bootstrapperId, CancellationToken token)
    {
        if (_sharedBootstrappers is null || release.FirstPublishedAtUtc is null) return null;
        var artifact = release.Artifacts.SingleOrDefault(x => x.Kind == DesktopArtifactKinds.DesktopPackage);
        if (artifact?.DistributionProtocol != DesktopDistributionProtocol.Shared || artifact.VerifiedPreparationId is not { } id) return null;
        var preparation = await dbContext.DesktopReleasePreparations.AsNoTracking().SingleOrDefaultAsync(x => x.PreparationId == id && x.ReleaseId == release.AppReleaseId, token);
        if (preparation is null || preparation.State != DesktopReleasePreparationStatuses.Published || preparation.VerifiedAtUtc is null
            || preparation.InputFingerprint != Fingerprint(preparation.Version, preparation.DownloadUrl, _options)) return null;
        try
        {
            var evidence = DesktopReleasePreparationService.ReadEvidence(preparation);
            ValidateInspection(evidence, release.Version, _options);
            _ = ValidateUrl(preparation.DownloadUrl, _options);
            DesktopReleasePreparationService.EnsureArtifactMatches(preparation, artifact, evidence, release);
            var bootstrapper = bootstrapperId is { } selected
                ? await _sharedBootstrappers.CompatibleAsync(selected, evidence.DistributionProfile.MinimumBootstrapperVersion, token)
                : await _sharedBootstrappers.ActiveAsync(evidence.DistributionProfile.MinimumBootstrapperVersion, token);
            return bootstrapper is null ? null : new(release, artifact, bootstrapper, evidence.DistributionProfile.Profile,
                evidence.DistributionProfile.MinimumBootstrapperVersion, evidence.PackageMetadata);
        }
        catch (Exception error) when (error is AccountApiException or ArgumentException or InvalidDataException) { return null; }
    }

    private async Task<AdminDesktopReleaseResponse> UpdateSharedReleaseAsync(AppRelease release, AdminDesktopReleaseRequest values, CancellationToken token)
    {
        if (values.Revision is null || values.Revision != release.Revision) throw Conflict("desktop_release_changed", "Release đã đổi. Tải lại để tiếp tục.");
        if (values.Version != release.Version || values.BuildNumber != release.BuildNumber || values.Channel != release.Channel || values.Platform != release.Platform)
            throw Conflict("desktop_release_immutable", "Sửa phiên bản/link của nháp trong form tối giản; release đã phát hành giữ nguyên identity.");
        if (values.IsActive && (release.FirstPublishedAtUtc is null || await TryCreateSharedPackageAsync(release, null, token) is null))
            throw Conflict("desktop_preparation_publish_required", "Bản này phải có bằng chứng và Setup hệ thống hợp lệ trước khi bật phân phối.");
        if (values.IsActive)
        {
            var evidencePreparation = await dbContext.DesktopReleasePreparations.AsNoTracking().SingleAsync(x => x.ReleaseId == release.AppReleaseId, token);
            var evidence = DesktopReleasePreparationService.ReadEvidence(evidencePreparation);
            if (_sharedBootstrappers is null || await _sharedBootstrappers.VerifyActiveAsync(evidence.DistributionProfile.MinimumBootstrapperVersion, token) is null)
                throw Conflict("desktop_preparation_publish_required", "Chưa có file Setup hệ thống đã kiểm tra đạt.");
        }
        var preparation = await dbContext.DesktopReleasePreparations.SingleAsync(x => x.ReleaseId == release.AppReleaseId, token);
        preparation.ReleaseNotes = values.ReleaseNotes; preparation.IsMandatory = values.IsMandatory;
        preparation.Revision = Guid.NewGuid(); preparation.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        release.ReleaseNotes = values.ReleaseNotes; release.IsMandatory = values.IsMandatory; release.IsActive = values.IsActive;
        release.MinimumSupportedDesktopVersion = values.MinimumSupportedDesktopVersion;
        release.PublishedAtUtc = values.PublishedAtUtc ?? release.PublishedAtUtc;
        await SaveReleaseAsync(release, token);
        return MapAdmin(release);
    }
}

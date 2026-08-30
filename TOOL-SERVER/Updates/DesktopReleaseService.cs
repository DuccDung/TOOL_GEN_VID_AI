using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Updates;
using System.IO.Compression;
using System.Text.Json;
using TOOL_SHARED.Distribution;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SERVER.Updates;

public interface IDesktopReleaseService
{
    Task<DesktopReleasePackage?> GetLatestPackageAsync(string platform, string channel, CancellationToken cancellationToken);

    Task<IReadOnlyList<DesktopReleasePackage>> GetVisiblePackagesAsync(string platform, string channel, CancellationToken cancellationToken);

    Task<DesktopReleasePackage?> GetVisiblePackageAsync(Guid releaseId, CancellationToken cancellationToken);

    Task<DesktopReleasePackage?> GetLatestArtifactAsync(string platform, string channel, string kind, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminDesktopReleaseResponse>> GetAdminReleasesAsync(CancellationToken cancellationToken);

    Task<AdminDesktopReleaseResponse> CreateAsync(AdminDesktopReleaseRequest request, CancellationToken cancellationToken);

    Task<AdminDesktopReleaseResponse> UpdateAsync(Guid releaseId, AdminDesktopReleaseRequest request, CancellationToken cancellationToken);

    Task<AdminDesktopArtifactResponse> SaveArtifactAsync(
        Guid releaseId,
        string kind,
        string fileName,
        Stream stream,
        long length,
        CancellationToken cancellationToken);

    Task DeleteAsync(Guid releaseId, CancellationToken cancellationToken);
}

public sealed class DesktopReleaseService(
    AccountDbContext dbContext,
    IDesktopReleaseStorage storage,
    TimeProvider timeProvider) : IDesktopReleaseService
{
    public Task<DesktopReleasePackage?> GetLatestPackageAsync(
        string platform,
        string channel,
        CancellationToken cancellationToken) =>
        GetLatestArtifactAsync(platform, channel, DesktopArtifactKinds.DesktopPackage, cancellationToken);

    public async Task<IReadOnlyList<DesktopReleasePackage>> GetVisiblePackagesAsync(
        string platform,
        string channel,
        CancellationToken cancellationToken)
    {
        var normalizedPlatform = NormalizePlatform(platform);
        var normalizedChannel = NormalizeChannel(channel);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var releases = await dbContext.AppReleases
            .AsNoTracking()
            .Include(x => x.Artifacts)
            .Where(x => x.IsActive &&
                        x.Platform == normalizedPlatform &&
                        x.Channel == normalizedChannel &&
                        x.PublishedAtUtc <= now)
            .OrderByDescending(x => x.BuildNumber)
            .ThenByDescending(x => x.PublishedAtUtc)
            .ToListAsync(cancellationToken);

        return releases
            .Select(release => CreatePackage(release, DesktopArtifactKinds.DesktopPackage))
            .Where(package => package is not null)
            .Cast<DesktopReleasePackage>()
            .ToArray();
    }

    public async Task<DesktopReleasePackage?> GetVisiblePackageAsync(
        Guid releaseId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var release = await dbContext.AppReleases
            .AsNoTracking()
            .Include(x => x.Artifacts)
            .SingleOrDefaultAsync(
                x => x.AppReleaseId == releaseId && x.IsActive && x.PublishedAtUtc <= now,
                cancellationToken);
        return release is null ? null : CreatePackage(release, DesktopArtifactKinds.DesktopPackage);
    }

    public async Task<DesktopReleasePackage?> GetLatestArtifactAsync(
        string platform,
        string channel,
        string kind,
        CancellationToken cancellationToken)
    {
        var normalizedPlatform = NormalizePlatform(platform);
        var normalizedChannel = NormalizeChannel(channel);
        var normalizedKind = NormalizeKind(kind);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var release = await dbContext.AppReleases
            .AsNoTracking()
            .Include(x => x.Artifacts)
            .Where(x => x.IsActive &&
                        x.Platform == normalizedPlatform &&
                        x.Channel == normalizedChannel &&
                        x.PublishedAtUtc <= now &&
                        x.Artifacts.Any(artifact => artifact.Kind == normalizedKind))
            .OrderByDescending(x => x.BuildNumber)
            .ThenByDescending(x => x.PublishedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return release is null ? null : CreatePackage(release, normalizedKind);
    }

    public async Task<IReadOnlyList<AdminDesktopReleaseResponse>> GetAdminReleasesAsync(
        CancellationToken cancellationToken)
    {
        var releases = await dbContext.AppReleases
            .AsNoTracking()
            .Include(x => x.Artifacts)
            .OrderByDescending(x => x.BuildNumber)
            .ThenByDescending(x => x.PublishedAtUtc)
            .ToListAsync(cancellationToken);
        return releases.Select(MapAdmin).ToArray();
    }

    public async Task<AdminDesktopReleaseResponse> CreateAsync(
        AdminDesktopReleaseRequest request,
        CancellationToken cancellationToken)
    {
        var values = Validate(request);
        var duplicate = await dbContext.AppReleases.AnyAsync(
            x => x.Version == values.Version &&
                 x.BuildNumber == values.BuildNumber &&
                 x.Channel == values.Channel &&
                 x.Platform == values.Platform,
            cancellationToken);
        if (duplicate)
        {
            throw Conflict("desktop_release_exists", "Phiên bản và build này đã tồn tại trong channel.");
        }

        var release = new AppRelease
        {
            Version = values.Version,
            BuildNumber = values.BuildNumber,
            Channel = values.Channel,
            Platform = values.Platform,
            MinimumSupportedDesktopVersion = values.MinimumSupportedDesktopVersion,
            ReleaseNotes = values.ReleaseNotes,
            IsMandatory = values.IsMandatory,
            IsActive = values.IsActive,
            PublishedAtUtc = values.PublishedAtUtc ?? timeProvider.GetUtcNow().UtcDateTime
        };
        dbContext.AppReleases.Add(release);
        await dbContext.SaveChangesAsync(cancellationToken);
        return MapAdmin(release);
    }

    public async Task<AdminDesktopReleaseResponse> UpdateAsync(
        Guid releaseId,
        AdminDesktopReleaseRequest request,
        CancellationToken cancellationToken)
    {
        var values = Validate(request);
        var release = await dbContext.AppReleases
            .Include(x => x.Artifacts)
            .SingleOrDefaultAsync(x => x.AppReleaseId == releaseId, cancellationToken)
            ?? throw NotFound("desktop_release_not_found", "Không tìm thấy desktop release.");
        var duplicate = await dbContext.AppReleases.AnyAsync(
            x => x.AppReleaseId != releaseId &&
                 x.Version == values.Version &&
                 x.BuildNumber == values.BuildNumber &&
                 x.Channel == values.Channel &&
                 x.Platform == values.Platform,
            cancellationToken);
        if (duplicate)
        {
            throw Conflict("desktop_release_exists", "Phiên bản và build này đã tồn tại trong channel.");
        }

        release.Version = values.Version;
        release.BuildNumber = values.BuildNumber;
        release.Channel = values.Channel;
        release.Platform = values.Platform;
        release.MinimumSupportedDesktopVersion = values.MinimumSupportedDesktopVersion;
        release.ReleaseNotes = values.ReleaseNotes;
        release.IsMandatory = values.IsMandatory;
        release.IsActive = values.IsActive;
        release.PublishedAtUtc = values.PublishedAtUtc ?? release.PublishedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
        return MapAdmin(release);
    }

    public async Task<AdminDesktopArtifactResponse> SaveArtifactAsync(
        Guid releaseId,
        string kind,
        string fileName,
        Stream stream,
        long length,
        CancellationToken cancellationToken)
    {
        var normalizedKind = NormalizeKind(kind);
        var release = await dbContext.AppReleases
            .Include(x => x.Artifacts)
            .SingleOrDefaultAsync(x => x.AppReleaseId == releaseId, cancellationToken)
            ?? throw NotFound("desktop_release_not_found", "Không tìm thấy desktop release.");
        var stored = await storage.SaveAsync(
            releaseId,
            normalizedKind,
            fileName,
            stream,
            length,
            cancellationToken);
        if (normalizedKind == DesktopArtifactKinds.DesktopPackage)
        {
            try
            {
                ValidatePackageManifest(storage.ResolvePath(stored.RelativePath), release);
            }
            catch
            {
                storage.DeleteFile(stored.RelativePath);
                throw;
            }
        }
        var artifact = release.Artifacts.SingleOrDefault(x => x.Kind == normalizedKind);
        var previousPath = artifact?.RelativePath;
        try
        {
            if (artifact is null)
            {
                artifact = new AppReleaseArtifact
                {
                    AppReleaseId = releaseId,
                    Kind = normalizedKind,
                    CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
                };
                release.Artifacts.Add(artifact);
            }

            artifact.FileName = stored.FileName;
            artifact.RelativePath = stored.RelativePath;
            artifact.SizeBytes = stored.SizeBytes;
            artifact.Sha256 = stored.Sha256;
            artifact.CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            storage.DeleteFile(stored.RelativePath);
            throw;
        }

        storage.DeleteFile(previousPath);
        return MapArtifact(artifact);
    }

    public async Task DeleteAsync(Guid releaseId, CancellationToken cancellationToken)
    {
        var release = await dbContext.AppReleases
            .SingleOrDefaultAsync(x => x.AppReleaseId == releaseId, cancellationToken)
            ?? throw NotFound("desktop_release_not_found", "Không tìm thấy desktop release.");
        dbContext.AppReleases.Remove(release);
        await dbContext.SaveChangesAsync(cancellationToken);
        try
        {
            storage.DeleteRelease(releaseId);
        }
        catch
        {
            // Database state is authoritative; orphan cleanup can be retried later.
        }
    }

    private static DesktopReleasePackage? CreatePackage(AppRelease release, string kind)
    {
        var artifact = release.Artifacts.FirstOrDefault(x => x.Kind == kind);
        return artifact is null ? null : new DesktopReleasePackage(release, artifact);
    }

    private static void ValidatePackageManifest(string packagePath, AppRelease release)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = archive.Entries.FirstOrDefault(entry =>
            entry.FullName.Replace('\\', '/').EndsWith("update-manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null)
        {
            throw Validation("desktop_package_manifest_missing", "Package không chứa update manifest.");
        }

        using var stream = manifestEntry.Open();
        DesktopUpdateManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<DesktopUpdateManifest>(
                stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            manifest = null;
        }

        if (manifest is null ||
            !string.Equals(manifest.Product, "VideoMaker", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.Version, release.Version, StringComparison.OrdinalIgnoreCase) ||
            manifest.BuildNumber != release.BuildNumber ||
            !string.Equals(manifest.Platform, release.Platform, StringComparison.OrdinalIgnoreCase) ||
            manifest.ManagedFiles is null ||
            !manifest.ManagedFiles.Any(path => string.Equals(path.Replace('\\', '/'), "TOOL-LOCAL.exe", StringComparison.OrdinalIgnoreCase)))
        {
            throw Validation("desktop_package_manifest_mismatch", "Manifest trong package không khớp release.");
        }

        var managedFiles = manifest.ManagedFiles
            .Select(path => path.Replace('\\', '/').TrimStart('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (DesktopMediaBundleIntegrity.RequiredRelativePaths.Any(path => !managedFiles.Contains(path)))
        {
            throw Validation(
                "desktop_package_media_bundle_missing",
                "Manifest trong package thiếu hồ sơ FFmpeg bắt buộc.");
        }

        var manifestEntryPath = manifestEntry.FullName.Replace('\\', '/').TrimStart('/');
        var manifestDirectory = manifestEntryPath[..^"update-manifest.json".Length];
        var archiveFiles = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => entry.FullName.Replace('\\', '/').TrimStart('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (DesktopMediaBundleIntegrity.RequiredRelativePaths.Any(
                path => !archiveFiles.Contains(manifestDirectory + path)))
        {
            throw Validation(
                "desktop_package_media_bundle_missing",
                "Package thiếu file FFmpeg hoặc hồ sơ checksum bắt buộc.");
        }
    }

    private static AdminDesktopReleaseResponse MapAdmin(AppRelease release) =>
        new(
            release.AppReleaseId,
            release.Version,
            release.BuildNumber,
            release.Channel,
            release.Platform,
            release.MinimumSupportedDesktopVersion,
            release.ReleaseNotes,
            release.IsMandatory,
            release.IsActive,
            release.PublishedAtUtc,
            release.Artifacts.OrderBy(x => x.Kind).Select(MapArtifact).ToArray());

    private static AdminDesktopArtifactResponse MapArtifact(AppReleaseArtifact artifact) =>
        new(
            artifact.AppReleaseArtifactId,
            artifact.Kind,
            artifact.FileName,
            artifact.SizeBytes,
            artifact.Sha256,
            artifact.CreatedAtUtc);

    private static AdminDesktopReleaseRequest Validate(AdminDesktopReleaseRequest request)
    {
        var version = NormalizeRequired(request.Version, 50, "Version");
        if (DesktopVersionComparer.CompareVersions(version, "0") <= 0)
        {
            throw Validation("desktop_release_invalid", "Version phải chứa phiên bản số lớn hơn 0.");
        }

        if (request.BuildNumber <= 0)
        {
            throw Validation("desktop_release_invalid", "Build number phải lớn hơn 0.");
        }

        var minimum = NormalizeOptional(request.MinimumSupportedDesktopVersion, 50);
        if (minimum is not null && DesktopVersionComparer.CompareVersions(minimum, "0") <= 0)
        {
            throw Validation("desktop_release_invalid", "Minimum supported version không hợp lệ.");
        }

        return request with
        {
            Version = version,
            Channel = NormalizeChannel(request.Channel),
            Platform = NormalizePlatform(request.Platform),
            MinimumSupportedDesktopVersion = minimum,
            ReleaseNotes = NormalizeOptional(request.ReleaseNotes, 100_000),
            PublishedAtUtc = request.PublishedAtUtc?.ToUniversalTime()
        };
    }

    private static string NormalizeChannel(string channel) =>
        DesktopReleaseChannels.All.FirstOrDefault(value =>
            string.Equals(value, channel?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw Validation("desktop_release_channel_invalid", "Release channel không được hỗ trợ.");

    private static string NormalizePlatform(string platform)
    {
        var normalized = platform?.Trim().ToLowerInvariant();
        return normalized == DesktopReleasePlatforms.WindowsX64
            ? normalized
            : throw Validation("desktop_release_platform_invalid", "Hiện tại chỉ hỗ trợ platform win-x64.");
    }

    private static string NormalizeKind(string kind) =>
        DesktopArtifactKinds.All.FirstOrDefault(value =>
            string.Equals(value, kind?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw Validation("desktop_artifact_kind_invalid", "Loại artifact không được hỗ trợ.");

    private static string NormalizeRequired(string? value, int maximumLength, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumLength)
        {
            throw Validation("desktop_release_invalid", $"{field} không hợp lệ.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > maximumLength)
        {
            throw Validation("desktop_release_invalid", "Dữ liệu release vượt quá độ dài cho phép.");
        }

        return normalized;
    }

    private static AccountApiException Validation(string code, string message) =>
        new(StatusCodes.Status400BadRequest, code, message);

    private static AccountApiException NotFound(string code, string message) =>
        new(StatusCodes.Status404NotFound, code, message);

    private static AccountApiException Conflict(string code, string message) =>
        new(StatusCodes.Status409Conflict, code, message);
}

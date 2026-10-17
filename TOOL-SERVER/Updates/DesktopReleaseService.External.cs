using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Domain.Updates;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_SERVER.Updates;

public sealed partial class DesktopReleaseService
{
    private static readonly JsonSerializerOptions RegistrationJson = new(JsonSerializerDefaults.Web);

    public async Task<AdminDesktopArtifactResponse> RegisterExternalAsync(Guid releaseId,
        RegisterExternalDesktopArtifactRequest request, CancellationToken cancellationToken)
    {
        RequireExternalEnabled();
        var release = await LoadReleaseAsync(releaseId, cancellationToken);
        var normalized = ValidateExternalRegistration(request, release);
        var evidence = JsonSerializer.Serialize(normalized, RegistrationJson);
        if (System.Text.Encoding.UTF8.GetByteCount(evidence) > 4 * 1024 * 1024)
            throw Validation("desktop_external_metadata_invalid", "Metadata đăng ký vượt giới hạn.");
        var artifact = release.Artifacts.SingleOrDefault(x => x.Kind == DesktopArtifactKinds.DesktopPackage);
        if (artifact?.DistributionProtocol == DesktopDistributionProtocol.Shared)
            throw Conflict("desktop_preparation_managed_release", "Bản này do luồng kiểm ZIP quản lý. Hãy chỉnh nháp phát hành tương ứng.");
        if (artifact?.SourceKind == DesktopArtifactSourceKinds.ExternalVps && artifact.RegistrationEvidenceJson == evidence)
            return MapArtifact(artifact); // An identical retry never changes publication or Setup.
        if (artifact is not null && artifact.SourceKind != DesktopArtifactSourceKinds.ExternalVps)
            throw Conflict("desktop_artifact_source_conflict", "Giữ artifact nội bộ của release cũ. Dùng release/build mới cho nguồn VPS.");
        RequireMutableExternal(release);
        if (release.IsActive)
            throw Conflict("desktop_release_draft_required", "Đăng ký nguồn ngoài trên release nháp chưa phát hành.");
        var previousPath = artifact?.RelativePath;
        if (artifact is null)
        {
            artifact = new AppReleaseArtifact { AppReleaseId = releaseId, Kind = DesktopArtifactKinds.DesktopPackage };
            release.Artifacts.Add(artifact);
        }
        artifact.SourceKind = DesktopArtifactSourceKinds.ExternalVps;
        artifact.RelativePath = null;
        artifact.ExternalDownloadUrl = normalized.DownloadUrl;
        artifact.FileName = normalized.FileName;
        artifact.SizeBytes = normalized.SizeBytes;
        artifact.Sha256 = normalized.Sha256;
        artifact.InstallMode = normalized.InstallMode;
        artifact.DistributionProtocol = normalized.DistributionProtocol;
        artifact.ModelBundleId = normalized.ModelBundleId;
        artifact.BoundPackageSha256 = normalized.Sha256;
        artifact.RegistrationEvidenceJson = evidence;
        artifact.CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        // A draft can be re-registered, but an earlier uploaded Setup must not
        // authorize a different package. It is retained only as unbound draft data.
        var setup = release.Artifacts.SingleOrDefault(x => x.Kind == DesktopArtifactKinds.Setup);
        if (setup is not null && !SetupMatches(setup, normalized))
        {
            setup.BoundPackageSha256 = null;
            setup.InstallMode = DesktopInstallModes.Legacy;
            setup.DistributionProtocol = DesktopDistributionProtocol.Legacy;
            setup.ModelBundleId = null;
        }
        else if (setup is not null)
        {
            setup.BoundPackageSha256 = normalized.Sha256;
            setup.InstallMode = normalized.InstallMode;
            setup.DistributionProtocol = normalized.DistributionProtocol;
            setup.ModelBundleId = normalized.ModelBundleId;
        }
        await SaveReleaseAsync(release, cancellationToken);
        storage.DeleteFile(previousPath); // Only an old INTERNAL path; never a URL.
        return MapArtifact(artifact);
    }

    public async Task<AdminDesktopReleaseResponse> PublishAsync(Guid releaseId, Guid revision, CancellationToken cancellationToken)
    {
        var release = await LoadReleaseAsync(releaseId, cancellationToken);
        if (revision == Guid.Empty || revision != release.Revision)
            throw Conflict("desktop_release_changed", "Release đã đổi. Tải lại trước khi phát hành.");
        if (release.Artifacts.Any(x => x.DistributionProtocol == DesktopDistributionProtocol.Shared))
            throw Conflict("desktop_preparation_publish_required", "Phát hành bản này từ nháp đã kiểm tra để xác minh đúng revision/bằng chứng.");
        if (HasExternalPackage(release))
        {
            EnsureExternalReady(release);
            release.FirstPublishedAtUtc ??= timeProvider.GetUtcNow().UtcDateTime;
        }
        else
        {
            var package = release.Artifacts.SingleOrDefault(x => x.Kind == DesktopArtifactKinds.DesktopPackage);
            if (package?.SourceKind != DesktopArtifactSourceKinds.Internal || string.IsNullOrWhiteSpace(package.RelativePath))
                throw Validation("desktop_package_required", "Thiếu package nội bộ để phát hành.");
            var path = storage.ResolvePath(package.RelativePath);
            if (!File.Exists(path) || new FileInfo(path).Length != package.SizeBytes)
                throw Validation("desktop_package_required", "File package nội bộ không còn đầy đủ.");
            ValidatePackageManifest(path, release);
        }
        release.IsActive = true;
        await SaveReleaseAsync(release, cancellationToken);
        return MapAdmin(release);
    }

    public async Task<IReadOnlyList<DesktopReleasePackage>> GetVisiblePackagesAsync(string platform, string channel,
        int distributionProtocol, CancellationToken cancellationToken)
    {
        ValidateProtocol(distributionProtocol);
        if (distributionProtocol == DesktopDistributionProtocol.Shared)
            return await GetSharedVisiblePackagesAsync(platform, channel, cancellationToken);
        var normalizedPlatform = NormalizePlatform(platform);
        var normalizedChannel = NormalizeChannel(channel);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var releases = await dbContext.AppReleases.AsNoTracking().Include(x => x.Artifacts)
            .Where(x => x.IsActive && x.Platform == normalizedPlatform && x.Channel == normalizedChannel && x.PublishedAtUtc <= now)
            .OrderByDescending(x => x.BuildNumber).ThenByDescending(x => x.PublishedAtUtc).ToListAsync(cancellationToken);
        // Filter capability and readiness BEFORE choosing latest, so an unsupported
        // or incomplete release cannot hide the previous compatible package.
        return releases.Where(x => CanDistribute(x, distributionProtocol))
            .Select(x => CreatePackage(x, DesktopArtifactKinds.DesktopPackage)).OfType<DesktopReleasePackage>().ToArray();
    }

    public async Task<DesktopReleasePackage?> GetLatestPackageAsync(string platform, string channel,
        int distributionProtocol, CancellationToken cancellationToken) =>
        (await GetVisiblePackagesAsync(platform, channel, distributionProtocol, cancellationToken)).FirstOrDefault();

    public async Task<DesktopReleasePackage?> GetVisibleSetupAsync(Guid releaseId, int distributionProtocol, CancellationToken cancellationToken)
    {
        ValidateProtocol(distributionProtocol);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var release = await dbContext.AppReleases.AsNoTracking().Include(x => x.Artifacts)
            .SingleOrDefaultAsync(x => x.AppReleaseId == releaseId && x.IsActive && x.PublishedAtUtc <= now, cancellationToken);
        return release is not null && HasExternalPackage(release) && CanDistribute(release, distributionProtocol)
            ? CreatePackage(release, DesktopArtifactKinds.Setup) : null;
    }

    private bool CanDistribute(AppRelease release, int protocol)
    {
        var package = release.Artifacts.SingleOrDefault(x => x.Kind == DesktopArtifactKinds.DesktopPackage);
        if (package is null) return false;
        if (package.SourceKind == DesktopArtifactSourceKinds.Internal)
            return package.InstallMode == DesktopInstallModes.Legacy && package.DistributionProtocol == DesktopDistributionProtocol.Legacy
                && !string.IsNullOrWhiteSpace(package.RelativePath) && package.ExternalDownloadUrl is null;
        if (protocol != DesktopDistributionProtocol.Current || package.DistributionProtocol != DesktopDistributionProtocol.Current || release.FirstPublishedAtUtc is null) return false;
        try { EnsureExternalReady(release); return true; }
        catch (Exception error) when (error is TOOL_SERVER.Authentication.AccountApiException or IOException or InvalidOperationException or ArgumentException)
        { return false; }
    }

    private void EnsureExternalReady(AppRelease release)
    {
        RequireExternalEnabled();
        var package = release.Artifacts.SingleOrDefault(x => x.Kind == DesktopArtifactKinds.DesktopPackage);
        if (package?.SourceKind != DesktopArtifactSourceKinds.ExternalVps || package.RelativePath is not null
            || package.RegistrationEvidenceJson is not { Length: > 0 and <= 4 * 1024 * 1024 })
            throw Validation("desktop_external_metadata_invalid", "Thiếu metadata package nguồn ngoài.");
        var evidence = ReadRegistration(package);
        var normalized = ValidateExternalRegistration(evidence, release);
        if (package.ExternalDownloadUrl != normalized.DownloadUrl || package.FileName != normalized.FileName
            || package.SizeBytes != normalized.SizeBytes || package.Sha256 != normalized.Sha256
            || package.BoundPackageSha256 != normalized.Sha256 || package.InstallMode != normalized.InstallMode
            || package.DistributionProtocol != normalized.DistributionProtocol || package.ModelBundleId != normalized.ModelBundleId)
            throw Validation("desktop_external_binding_invalid", "Metadata package không khớp đăng ký.");
        var setup = release.Artifacts.SingleOrDefault(x => x.Kind == DesktopArtifactKinds.Setup);
        if (setup is null || !SetupMatches(setup, normalized) || setup.BoundPackageSha256 != package.Sha256
            || setup.InstallMode != package.InstallMode || setup.DistributionProtocol != package.DistributionProtocol
            || setup.ModelBundleId != package.ModelBundleId)
            throw Validation("desktop_exact_setup_required", "Thiếu Setup đúng file và binding của release này.");
        // This verifies the server-owned Setup exists. No request to the external
        // origin is made and no claim about remote ZIP bytes is produced.
        if (new FileInfo(storage.ResolvePath(setup.RelativePath!)) is not { Exists: true } file || file.Length != setup.SizeBytes)
            throw Validation("desktop_exact_setup_required", "File Setup của release không còn đầy đủ.");
    }

    private RegisterExternalDesktopArtifactRequest ValidateExternalRegistration(RegisterExternalDesktopArtifactRequest request, AppRelease release)
    {
        if (request is null || request.SchemaVersion != 1 || request.Version != release.Version
            || request.BuildNumber != release.BuildNumber || request.Channel != release.Channel || request.Platform != release.Platform
            || request.InstallMode != DesktopInstallModes.StaticArchive || request.DistributionProtocol != DesktopDistributionProtocol.Current
            || request.ModelBundleId != ApprovedStaticModelBundle.Current.BundleId
            || request.SizeBytes <= 0 || request.SizeBytes > Math.Min(_options.MaximumExternalArtifactBytes, DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes)
            || request.ExpandedSizeBytes <= 0 || request.ExpandedSizeBytes > DesktopArtifactDownloadPolicy.MaximumExpandedBytes
            || request.EntryCount is <= 0 or > DesktopArtifactDownloadPolicy.MaximumEntryCount
            || request.PackageManifest is null || request.Setup is null)
            throw Validation("desktop_external_metadata_invalid", "Metadata đăng ký không khớp release, mode, bundle hoặc giới hạn.");
        ValidateArtifactName(request.FileName, ".zip"); ValidateArtifactName(request.Setup.FileName, ".exe");
        if (request.Setup.SizeBytes is < 2 || request.Setup.SizeBytes > Math.Min(_options.MaximumArtifactBytes, DesktopArtifactDownloadPolicy.MaximumSetupArtifactBytes)
            || request.Setup.FileName.Length > DesktopArtifactDownloadPolicy.MaximumArtifactFileNameLength)
            throw Validation("desktop_external_metadata_invalid", "Kích thước Setup đăng ký không hợp lệ.");
        var packageHash = NormalizeHash(request.Sha256);
        var setupHash = NormalizeHash(request.Setup.Sha256);
        Uri resource;
        try
        {
            resource = DesktopArtifactDownloadPolicy.Validate(new Uri("https://metadata.invalid/"), request.DownloadUrl,
                DesktopArtifactSourceKinds.ExternalVps, _options.AllowedExternalPrefixes);
            _ = DesktopArtifactDownloadPolicy.Validate(new Uri("https://metadata.invalid/"), request.DownloadUrl,
                DesktopArtifactSourceKinds.ExternalVps); // Consumers must also know this compiled source.
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        { throw Validation("desktop_external_url_invalid", "URL package không thuộc nguồn VPS đã duyệt."); }
        if (!resource.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw Validation("desktop_external_url_invalid", "URL phải xác định file ZIP ứng dụng.");
        ValidateDeclaredManifest(request.PackageManifest, release, request.EntryCount);
        return request with { DownloadUrl = resource.AbsoluteUri, Sha256 = packageHash, Setup = request.Setup with { Sha256 = setupHash } };
    }

    private static void ValidateDeclaredManifest(DesktopUpdateManifest manifest, AppRelease release, int entryCount)
    {
        if (manifest.Product != "VideoMaker" || manifest.Version != release.Version || manifest.BuildNumber != release.BuildNumber
            || manifest.Platform != release.Platform || manifest.ManagedFiles is not { Count: > 0 and <= DesktopArtifactDownloadPolicy.MaximumEntryCount }
            || manifest.ManagedFiles.Count > entryCount)
            throw Validation("desktop_external_manifest_invalid", "Manifest khai báo không khớp release.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in manifest.ManagedFiles)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 260 || path.Contains('\\') || path.Contains(':')
                || path.Any(c => char.IsControl(c) || "<>\"|?*".Contains(c))
                || path.Split('/').Any(x => x is "" or "." or ".." || x.EndsWith('.') || x.EndsWith(' '))
                || path.Split('/').Any(x => System.Text.RegularExpressions.Regex.IsMatch(x.Split('.')[0],
                    "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                || !paths.Add(path)) throw Validation("desktop_external_manifest_invalid", "Manifest khai báo có path không an toàn hoặc trùng.");
        }
        if (!paths.Contains("TOOL-LOCAL.exe") || !paths.Contains("update-manifest.json")
            || DesktopMediaBundleIntegrity.RequiredRelativePaths.Any(x => !paths.Contains(x)))
            throw Validation("desktop_external_manifest_invalid", "Manifest khai báo thiếu file ứng dụng/media bắt buộc.");
    }

    private void RequireExternalEnabled()
    {
        if (!_options.ExternalVpsEnabled || _options.AllowedExternalPrefixes is not { Length: > 0 })
            throw Validation("desktop_external_disabled", "Phân phối ứng dụng từ VPS chưa được cấu hình/bật trên server.");
    }
    private static bool HasExternalPackage(AppRelease release) => release.Artifacts.Any(x =>
        x.Kind == DesktopArtifactKinds.DesktopPackage && x.SourceKind == DesktopArtifactSourceKinds.ExternalVps);
    private static void RequireMutableExternal(AppRelease release)
    {
        if (release.FirstPublishedAtUtc is not null)
            throw Conflict("desktop_release_immutable", "Release đã phát hành. Dùng release/build mới để đổi package hoặc Setup.");
    }
    private static bool SetupMatches(AppReleaseArtifact setup, RegisterExternalDesktopArtifactRequest registration) =>
        setup.SourceKind == DesktopArtifactSourceKinds.Internal && !string.IsNullOrWhiteSpace(setup.RelativePath)
        && setup.FileName == registration.Setup.FileName && setup.SizeBytes == registration.Setup.SizeBytes
        && setup.Sha256 == registration.Setup.Sha256;
    private static RegisterExternalDesktopArtifactRequest ReadRegistration(AppReleaseArtifact package)
    {
        if (package.RegistrationEvidenceJson is not { Length: > 0 and <= 4 * 1024 * 1024 })
            throw Validation("desktop_external_metadata_invalid", "Metadata package không hợp lệ.");
        try
        {
            return JsonSerializer.Deserialize<RegisterExternalDesktopArtifactRequest>(package.RegistrationEvidenceJson, RegistrationJson)
                ?? throw Validation("desktop_external_metadata_invalid", "Metadata package không hợp lệ.");
        }
        catch (JsonException) { throw Validation("desktop_external_metadata_invalid", "Metadata package không hợp lệ."); }
    }
    private Task<AppRelease> LoadReleaseAsync(Guid id, CancellationToken token) =>
        LoadReleaseCoreAsync(id, token);
    private async Task<AppRelease> LoadReleaseCoreAsync(Guid id, CancellationToken token) =>
        await dbContext.AppReleases.Include(x => x.Artifacts).SingleOrDefaultAsync(x => x.AppReleaseId == id, token)
        ?? throw NotFound("desktop_release_not_found", "Không tìm thấy desktop release.");
    private async Task SaveReleaseAsync(AppRelease release, CancellationToken token)
    {
        release.Revision = Guid.NewGuid();
        try { await dbContext.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw Conflict("desktop_release_changed", "Release đã đổi. Tải lại để tiếp tục."); }
        catch (DbUpdateException error) when (error.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        { throw Conflict("desktop_release_changed", "Release/artifact vừa được tạo ở lượt khác. Tải lại để tiếp tục."); }
    }
    private static void ValidateProtocol(int protocol)
    {
        if (protocol is not (DesktopDistributionProtocol.Legacy or DesktopDistributionProtocol.Current or DesktopDistributionProtocol.Shared))
            throw Validation("desktop_distribution_protocol_unsupported", "Distribution protocol chưa được hỗ trợ.");
    }
    private static string NormalizeHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit)
        ? value.ToLowerInvariant() : throw Validation("desktop_external_metadata_invalid", "SHA-256 không hợp lệ.");
    private static void ValidateArtifactName(string? name, string extension)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > DesktopArtifactDownloadPolicy.MaximumArtifactFileNameLength || name != name.Trim() || name.EndsWith('.')
            || name is "." or ".." || name.Any(c => char.IsControl(c) || "\\/:<>\"|?*".Contains(c))
            || System.Text.RegularExpressions.Regex.IsMatch(name.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            || !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            throw Validation("desktop_external_metadata_invalid", "Tên artifact không hợp lệ.");
    }
}

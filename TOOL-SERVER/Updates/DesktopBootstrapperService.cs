using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Updates;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;
using static TOOL_SERVER.Updates.DesktopSharedReleaseValidation;

namespace TOOL_SERVER.Updates;

public interface IDesktopBootstrapperMetadataReader
{
    DesktopBootstrapperBindingInfo Read(string path);
}

public sealed class DesktopBootstrapperMetadataReader : IDesktopBootstrapperMetadataReader
{
    public DesktopBootstrapperBindingInfo Read(string path) => DesktopExecutableMetadata.ReadBootstrapperBinding(path);
}

public sealed class DesktopBootstrapperService(AccountDbContext db, IDesktopReleaseStorage storage,
    IDesktopBootstrapperMetadataReader metadata, IOptions<DesktopReleaseOptions> settings, TimeProvider clock)
{
    private readonly DesktopReleaseOptions _options = settings.Value;
    public DesktopSharedBootstrapperStatusResponse Status() => new(_options.SharedBootstrapperEnabled,
        _options.SharedBootstrapperEnabled && _options.PublicationMode == DesktopReleaseOptions.ServerInspectionMode,
        IsConfigured(_options), _options.Profile, _options.PublicationMode);

    public async Task<IReadOnlyList<DesktopBootstrapperAdminResponse>> ListAsync(CancellationToken token) =>
        (await db.DesktopBootstrappers.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToListAsync(token))
        .Select(MapAdmin).ToArray();

    public async Task<DesktopBootstrapperAdminResponse> UploadAsync(string actor, string fileName, Stream source, long length, CancellationToken token)
    {
        RequireEnabled(_options);
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 450 || length is < 2 or > DesktopArtifactDownloadPolicy.MaximumSetupArtifactBytes
            || string.IsNullOrWhiteSpace(fileName) || fileName.Length > DesktopArtifactDownloadPolicy.MaximumArtifactFileNameLength
            || fileName != Path.GetFileName(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw Invalid("desktop_bootstrapper_invalid", "File Setup hoặc người thực hiện không hợp lệ.");
        var id = Guid.NewGuid();
        StoredDesktopArtifact? stored = null;
        try
        {
            stored = await storage.SaveAsync(id, DesktopArtifactKinds.Setup, fileName, source, length, token);
            var binding = metadata.Read(storage.ResolvePath(stored.RelativePath));
            ValidateBinding(binding, _options);
            var existing = await db.DesktopBootstrappers.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Profile == binding.Profile && x.Platform == binding.Platform && x.Channel == binding.Channel && x.Version == binding.Version, token);
            if (existing is not null)
            {
                if (existing.Sha256 != stored.Sha256 || existing.SizeBytes != stored.SizeBytes
                    || JsonSerializer.Deserialize<DesktopBootstrapperBindingInfo>(existing.EmbeddedMetadataJson, Json) != binding)
                    throw Conflict("desktop_bootstrapper_immutable", "Phiên bản Setup đã có nội dung khác. Hãy dùng phiên bản Setup mới.");
                storage.DeleteFile(stored.RelativePath);
                return MapAdmin(existing);
            }
            var entity = new DesktopBootstrapper
            {
                BootstrapperId = id, Version = binding.Version, Profile = binding.Profile, Channel = binding.Channel,
                Platform = binding.Platform, ServerBaseUrl = NormalizeApi(binding.ApiBaseUrl), ModelBundleId = binding.ModelBundleId,
                DistributionProtocol = binding.DistributionProtocol, FileName = stored.FileName, RelativePath = stored.RelativePath,
                SizeBytes = stored.SizeBytes, Sha256 = stored.Sha256, EmbeddedMetadataJson = JsonSerializer.Serialize(binding, Json),
                CreatedByUserId = actor, CreatedAtUtc = clock.GetUtcNow().UtcDateTime
            };
            db.DesktopBootstrappers.Add(entity);
            await db.SaveChangesAsync(token);
            return MapAdmin(entity);
        }
        catch (DbUpdateException) { storage.DeleteFile(stored?.RelativePath); throw Conflict("desktop_bootstrapper_changed", "Setup vừa được đăng ký ở lượt khác. Tải lại danh sách."); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
        {
            storage.DeleteFile(stored?.RelativePath);
            throw Invalid("desktop_bootstrapper_file_invalid", "File Setup thiếu metadata dùng chung hoặc không đọc được. Hãy chọn đúng bộ cài đã build cho hệ thống.");
        }
        catch { storage.DeleteFile(stored?.RelativePath); throw; }
    }

    public async Task<DesktopBootstrapperAdminResponse> ActivateAsync(Guid id, Guid revision, CancellationToken token)
    {
        RequireEnabled(_options);
        var snapshot = await db.DesktopBootstrappers.AsNoTracking().SingleOrDefaultAsync(x => x.BootstrapperId == id, token)
            ?? throw new TOOL_SERVER.Authentication.AccountApiException(404, "desktop_bootstrapper_not_found", "Không tìm thấy Setup.");
        if (revision == Guid.Empty || snapshot.Revision != revision)
            throw Conflict("desktop_bootstrapper_changed", "Setup đã đổi. Tải lại trước khi kích hoạt.");
        await VerifyFileAsync(snapshot, token);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var selected = await db.DesktopBootstrappers.SingleOrDefaultAsync(x => x.BootstrapperId == id, token)
            ?? throw new TOOL_SERVER.Authentication.AccountApiException(404, "desktop_bootstrapper_not_found", "Không tìm thấy Setup.");
        if (revision == Guid.Empty || selected.Revision != revision)
            throw Conflict("desktop_bootstrapper_changed", "Setup đã đổi. Tải lại trước khi kích hoạt.");
        ValidateStored(selected);
        if (selected.IsActive) { await transaction.CommitAsync(token); return MapAdmin(selected); }
        var previous = await db.DesktopBootstrappers.Where(x => x.IsActive && x.Profile == selected.Profile
            && x.Channel == selected.Channel && x.Platform == selected.Platform).ToListAsync(token);
        foreach (var item in previous) { item.IsActive = false; item.Revision = Guid.NewGuid(); }
        try
        {
            // Two saves within one transaction avoid transient unique-index violations.
            await db.SaveChangesAsync(token);
            selected.IsActive = true; selected.ActivatedAtUtc = clock.GetUtcNow().UtcDateTime; selected.Revision = Guid.NewGuid();
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch (DbUpdateException) { throw Conflict("desktop_bootstrapper_changed", "Setup Active đã đổi ở lượt khác. Tải lại để tiếp tục."); }
        return MapAdmin(selected);
    }

    public async Task<DesktopSharedBootstrapperResponse?> ActiveAsync(string? minimumVersion, CancellationToken token)
    {
        if (!_options.SharedBootstrapperEnabled || !IsConfigured(_options)) return null;
        var item = await db.DesktopBootstrappers.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive && !x.IsRevoked && x.Profile == _options.Profile
            && x.Platform == "win-x64" && x.Channel == "Stable", token);
        if (item is null || minimumVersion is not null && DesktopReleaseVersionPolicy.CompareVersions(item.Version, minimumVersion) < 0) return null;
        try { ValidateStored(item); return Map(item); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or TOOL_SERVER.Authentication.AccountApiException or JsonException) { return null; }
    }

    public async Task<(string Path, string FileName, long SizeBytes)?> DownloadAsync(Guid id, CancellationToken token)
    {
        var item = await db.DesktopBootstrappers.AsNoTracking().SingleOrDefaultAsync(x => x.BootstrapperId == id, token);
        if (item is null) return null;
        try { await VerifyFileAsync(item, token); return (storage.ResolvePath(item.RelativePath), item.FileName, item.SizeBytes); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or TOOL_SERVER.Authentication.AccountApiException or JsonException) { return null; }
    }

    // Called only at publication, activation and native download. Listing versions does not rehash 512 MiB files.
    public async Task<DesktopSharedBootstrapperResponse?> VerifyActiveAsync(string minimumVersion, CancellationToken token)
    {
        if (!_options.SharedBootstrapperEnabled || !IsConfigured(_options)) return null;
        var item = await db.DesktopBootstrappers.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive && !x.IsRevoked
            && x.Profile == _options.Profile && x.Platform == "win-x64" && x.Channel == "Stable", token);
        if (item is null || DesktopReleaseVersionPolicy.CompareVersions(item.Version, minimumVersion) < 0) return null;
        await VerifyFileAsync(item, token);
        return Map(item);
    }

    private async Task VerifyFileAsync(DesktopBootstrapper item, CancellationToken token)
    {
        try
        {
            ValidateStored(item);
            var path = storage.ResolvePath(item.RelativePath);
            await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (source.Length != item.SizeBytes
                || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(source, token)), item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Setup hash changed.");
            var actualBinding = metadata.Read(path);
            ValidateBinding(actualBinding, _options);
            if (actualBinding != JsonSerializer.Deserialize<DesktopBootstrapperBindingInfo>(item.EmbeddedMetadataJson, Json))
                throw new InvalidDataException("Actual Setup metadata changed.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or JsonException)
        { throw Invalid("desktop_bootstrapper_file_invalid", "File Setup đã thiếu, hỏng hoặc không còn khớp metadata. Hãy tải lại đúng bản Setup."); }
    }

    public async Task<DesktopSharedBootstrapperResponse?> CompatibleAsync(Guid id, string minimumVersion, CancellationToken token)
    {
        if (!_options.SharedBootstrapperEnabled || !IsConfigured(_options)) return null;
        var item = await db.DesktopBootstrappers.AsNoTracking().SingleOrDefaultAsync(x => x.BootstrapperId == id && !x.IsRevoked, token);
        if (item is null || DesktopReleaseVersionPolicy.CompareVersions(item.Version, minimumVersion) < 0) return null;
        try { ValidateStored(item); return Map(item); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or TOOL_SERVER.Authentication.AccountApiException or JsonException) { return null; }
    }

    public async Task<DesktopSharedBootstrapperResponse?> CompatibleVersionAsync(string version, string minimumVersion, CancellationToken token)
    {
        if (!_options.SharedBootstrapperEnabled || !IsConfigured(_options) || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(version)
            || DesktopReleaseVersionPolicy.CompareVersions(version, minimumVersion) < 0) return null;
        var item = await db.DesktopBootstrappers.AsNoTracking().SingleOrDefaultAsync(x => x.Version == version && !x.IsRevoked
            && x.Profile == _options.Profile && x.Channel == "Stable" && x.Platform == "win-x64", token);
        if (item is null) return null;
        try { ValidateStored(item); return Map(item); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or TOOL_SERVER.Authentication.AccountApiException or JsonException) { return null; }
    }

    public async Task<DesktopBootstrapperAdminResponse> RevokeAsync(Guid id, Guid revision, CancellationToken token)
    {
        var entity = await db.DesktopBootstrappers.SingleOrDefaultAsync(x => x.BootstrapperId == id, token)
            ?? throw new TOOL_SERVER.Authentication.AccountApiException(404, "desktop_bootstrapper_not_found", "Không tìm thấy Setup.");
        if (revision == Guid.Empty || entity.Revision != revision) throw Conflict("desktop_bootstrapper_changed", "Setup đã đổi. Tải lại trước khi thu hồi.");
        entity.IsRevoked = true; entity.IsActive = false; entity.Revision = Guid.NewGuid();
        try { await db.SaveChangesAsync(token); } catch (DbUpdateException) { throw Conflict("desktop_bootstrapper_changed", "Setup vừa đổi ở lượt khác."); }
        return MapAdmin(entity);
    }

    private void ValidateStored(DesktopBootstrapper item)
    {
        if (item.IsRevoked) throw new InvalidDataException("Setup has been revoked.");
        var binding = JsonSerializer.Deserialize<DesktopBootstrapperBindingInfo>(item.EmbeddedMetadataJson, Json)
            ?? throw new InvalidDataException("Missing Setup metadata.");
        ValidateBinding(binding, _options);
        var file = new FileInfo(storage.ResolvePath(item.RelativePath));
        if (!file.Exists || file.Length != item.SizeBytes || item.SizeBytes is < 2 or > DesktopArtifactDownloadPolicy.MaximumSetupArtifactBytes)
            throw new InvalidDataException("Setup file is missing or incomplete.");
        if (item.Version != binding.Version || item.Profile != binding.Profile || item.Channel != binding.Channel || item.Platform != binding.Platform
            || item.DistributionProtocol != binding.DistributionProtocol || item.ModelBundleId != binding.ModelBundleId
            || item.ServerBaseUrl != NormalizeApi(binding.ApiBaseUrl)) throw new InvalidDataException("Setup metadata changed.");
    }

    private static DesktopSharedBootstrapperResponse Map(DesktopBootstrapper item) => new(item.BootstrapperId, item.Version, item.FileName,
        $"/api/launcher-distribution/bootstrappers/{item.BootstrapperId:D}/download", item.SizeBytes, item.Sha256,
        item.ServerBaseUrl, item.Profile, item.Channel, item.Platform, item.DistributionProtocol, item.ModelBundleId,
        JsonSerializer.Deserialize<DesktopBootstrapperBindingInfo>(item.EmbeddedMetadataJson, Json)?.SupportedCatalogProtocols ?? "3");
    private static DesktopBootstrapperAdminResponse MapAdmin(DesktopBootstrapper item) => new(Map(item), item.IsActive, item.Revision, item.CreatedAtUtc, item.IsRevoked);
}

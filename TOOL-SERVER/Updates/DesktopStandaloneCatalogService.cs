using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Updates;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;
using static TOOL_SERVER.Updates.DesktopSharedReleaseValidation;

namespace TOOL_SERVER.Updates;

/// <summary>Stores a declared ZIP catalog without inspecting files or requiring a registered Setup.</summary>
public sealed class DesktopStandaloneCatalogService(AccountDbContext db, DesktopDownloadSettingsService downloadSettings,
    IOptions<DesktopReleaseOptions> settings, TimeProvider clock)
{
    private readonly DesktopReleaseOptions _options = settings.Value;
    private bool Enabled => _options.PublicationMode == DesktopPublicationModes.StandaloneCatalog && IsConfigured(_options);

    public async Task<AdminDesktopStandaloneReleaseResponse> CreateAsync(string actor, DesktopStandaloneReleaseRequest request, CancellationToken token)
    {
        RequireEnabled();
        var values = ValidateRequest(actor, request);
        var fingerprint = Fingerprint(values, request);
        var existing = await db.DesktopDirectReleases.SingleOrDefaultAsync(x => x.ActorUserId == actor && x.ClientRequestId == request.RequestId, token);
        if (existing is not null)
        {
            if (existing.CatalogProtocol != DesktopStandaloneCatalogProtocol.Version || existing.CreateRequestFingerprint != fingerprint)
                throw Conflict("desktop_standalone_request_reused", "Mã lưu đã dùng cho nội dung khác. Tải lại bản đã lưu.");
            return MapAdmin(existing);
        }
        if (await db.DesktopDirectReleases.AnyAsync(x => x.Profile == _options.Profile && x.Channel == "Stable" && x.Platform == "win-x64" && x.Version == values.Version, token))
            throw Conflict("desktop_standalone_version_exists", "Phiên bản đã có trong catalog. Hãy sửa bản nháp hoặc tăng phiên bản.");
        var now = clock.GetUtcNow().UtcDateTime;
        var entity = new DesktopDirectRelease
        {
            ActorUserId = actor, ClientRequestId = request.RequestId, CreateRequestFingerprint = fingerprint,
            Profile = _options.Profile, ModelBundleId = ApprovedStaticModelBundle.Current.BundleId,
            CatalogProtocol = DesktopStandaloneCatalogProtocol.Version, ApplicationDistributionProtocol = DesktopDistributionProtocol.Shared,
            MinimumBootstrapperVersion = DesktopStandaloneCatalogProtocol.MinimumSetupVersion,
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
        SetDraft(entity, values, request); db.DesktopDirectReleases.Add(entity); await SaveAsync(token);
        return MapAdmin(entity);
    }

    public async Task<AdminDesktopStandaloneReleaseResponse?> GetAsync(Guid id, CancellationToken token)
    {
        var entity = await db.DesktopDirectReleases.AsNoTracking().SingleOrDefaultAsync(x => x.DirectReleaseId == id && x.CatalogProtocol == DesktopStandaloneCatalogProtocol.Version, token);
        return entity is null ? null : MapAdmin(entity);
    }

    public async Task<PagedResponse<AdminDesktopStandaloneReleaseResponse>> PageAsync(int page, int pageSize, CancellationToken token)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw Invalid("invalid_page", "Thông tin phân trang không hợp lệ.");
        var query = db.DesktopDirectReleases.AsNoTracking().Where(x => x.CatalogProtocol == DesktopStandaloneCatalogProtocol.Version);
        var count = await query.CountAsync(token);
        var effective = count == 0 ? 1 : Math.Min(page, (int)Math.Ceiling(count / (double)pageSize));
        var rows = await query.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.DirectReleaseId)
            .Skip((effective - 1) * pageSize).Take(pageSize).ToListAsync(token);
        return new(rows.Select(MapAdmin).ToArray(), effective, pageSize, count);
    }

    public async Task<AdminDesktopStandaloneReleaseResponse> UpdateAsync(Guid id, string actor, DesktopStandaloneReleaseRequest request, CancellationToken token)
    {
        RequireEnabled(); var values = ValidateRequest(actor, request);
        var entity = await LoadAsync(id, token); RequireRevision(entity, request.Revision); RequireScope(entity);
        if (entity.FirstPublishedAtUtc is not null && (entity.Version != values.Version || entity.DownloadUrl != values.Url))
            throw Conflict("desktop_standalone_immutable", "Phiên bản đã từng phát hành. Tạo phiên bản mới để đổi ZIP hoặc link tải.");
        if (entity.FirstPublishedAtUtc is null) SetDraft(entity, values, request);
        else { entity.ReleaseNotes = values.Notes; entity.IsMandatory = request.IsMandatory; }
        Touch(entity); await SaveAsync(token); return MapAdmin(entity);
    }

    public async Task<AdminDesktopStandaloneReleaseResponse> PublishAsync(Guid id, Guid revision, CancellationToken token)
    {
        RequireEnabled();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var entity = await LoadAsync(id, token); RequireRevision(entity, revision); ValidateStoredIdentity(entity);
        if (await db.AppReleases.AnyAsync(x => x.Version == entity.Version && x.Channel == entity.Channel && x.Platform == entity.Platform
                && x.FirstPublishedAtUtc != null && (x.BuildNumber != entity.DeclaredBuildNumber
                    || x.Artifacts.Any(a => a.Kind == DesktopArtifactKinds.DesktopPackage && a.Sha256 != entity.ExpectedSha256)), token))
            throw Conflict("desktop_standalone_version_conflict", "Phiên bản này đã phát hành ZIP khác ở luồng cũ. Hãy tăng phiên bản.");
        var now = clock.GetUtcNow().UtcDateTime;
        entity.FirstPublishedAtUtc ??= now; entity.PublishedAtUtc = now; entity.IsActive = true; Touch(entity);
        await SaveAsync(token); await transaction.CommitAsync(token);
        return MapAdmin(entity);
    }

    public async Task<AdminDesktopStandaloneReleaseResponse> DeactivateAsync(Guid id, Guid revision, CancellationToken token)
    {
        RequireEnabled(); var entity = await LoadAsync(id, token); RequireRevision(entity, revision); RequireScope(entity);
        if (entity.FirstPublishedAtUtc is null) throw Conflict("desktop_standalone_not_published", "Phiên bản chưa từng được phát hành.");
        entity.IsActive = false; Touch(entity); await SaveAsync(token); return MapAdmin(entity);
    }

    public async Task<IReadOnlyList<DesktopStandaloneReleaseResponse>> VisibleAsync(string profile, string channel, string platform, CancellationToken token,
        string? setupVersion = null)
    {
        if (!Enabled || profile != _options.Profile || channel != "Stable" || platform != "win-x64" ||
            setupVersion is not null && (!DesktopReleaseVersionPolicy.IsSupportedStableVersion(setupVersion) ||
                DesktopReleaseVersionPolicy.CompareVersions(setupVersion, DesktopStandaloneCatalogProtocol.MinimumSetupVersion) < 0)) return [];
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.DesktopDirectReleases.AsNoTracking().Where(x => x.CatalogProtocol == DesktopStandaloneCatalogProtocol.Version &&
            x.Profile == profile && x.Channel == channel && x.Platform == platform && x.IsActive && x.FirstPublishedAtUtc != null && x.PublishedAtUtc <= now).ToListAsync(token);
        var setup = await DeclaredSetupAsync(token);
        var result = new List<DesktopStandaloneReleaseResponse>();
        foreach (var row in rows) if (MapPublic(row, setup) is { } item) result.Add(item);
        result.Sort((a, b) => DesktopReleaseVersionPolicy.CompareVersions(b.Version, a.Version));
        return result;
    }

    public async Task<DesktopStandaloneReleaseResponse?> ExactAsync(Guid id, string profile, CancellationToken token)
    {
        if (!Enabled || profile != _options.Profile) return null;
        var now = clock.GetUtcNow().UtcDateTime;
        var entity = await db.DesktopDirectReleases.AsNoTracking().SingleOrDefaultAsync(x => x.DirectReleaseId == id &&
            x.CatalogProtocol == DesktopStandaloneCatalogProtocol.Version && x.Profile == profile && x.IsActive && x.FirstPublishedAtUtc != null && x.PublishedAtUtc <= now, token);
        return entity is null ? null : MapPublic(entity, await DeclaredSetupAsync(token));
    }

    private async Task<DesktopStandaloneSetupResponse?> DeclaredSetupAsync(CancellationToken token)
    {
        var url = await downloadSettings.GetPublicDownloadUrlAsync(token);
        if (url is null) return null;
        try { return DesktopStandaloneCatalogPolicy.ParseSetupUrl(new Uri(_options.ApiBaseUrl), url, _options.AllowedExternalPrefixes); }
        catch (InvalidDataException) { return null; }
    }

    private DesktopStandaloneReleaseResponse? MapPublic(DesktopDirectRelease entity, DesktopStandaloneSetupResponse? setup)
    {
        try
        {
            ValidateStoredIdentity(entity);
            var result = new DesktopStandaloneReleaseResponse(entity.DirectReleaseId, entity.Version, entity.DeclaredBuildNumber, entity.FileName,
                entity.DownloadUrl, entity.ExpectedSha256, NormalizeApi(_options.ApiBaseUrl), entity.Profile, entity.Channel, entity.Platform,
                entity.ModelBundleId, entity.Revision, entity.PublishedAtUtc!.Value, entity.IsMandatory, entity.ReleaseNotes, setup,
                entity.CatalogProtocol, entity.ApplicationDistributionProtocol, entity.MinimumBootstrapperVersion);
            DesktopStandaloneCatalogPolicy.ValidateRelease(result, new Uri(_options.ApiBaseUrl), _options.Profile);
            return result;
        }
        catch (Exception error) when (error is AccountApiException or InvalidDataException or ArgumentException) { return null; }
    }

    private AdminDesktopStandaloneReleaseResponse MapAdmin(DesktopDirectRelease entity) => new(
        entity.DirectReleaseId, entity.Version, entity.DeclaredBuildNumber, entity.DownloadUrl, entity.ExpectedSha256,
        entity.Profile, entity.Channel, entity.Platform, entity.FirstPublishedAtUtc is null ? DesktopDirectReleaseStatuses.Draft : DesktopDirectReleaseStatuses.Published,
        entity.IsActive, entity.IsMandatory, entity.ReleaseNotes, entity.Revision, entity.CreatedAtUtc, entity.UpdatedAtUtc, entity.FirstPublishedAtUtc,
        Enabled && entity.Profile == _options.Profile && entity.ModelBundleId == ApprovedStaticModelBundle.Current.BundleId);

    private sealed record Values(string Version, string Url, string FileName, string Sha256, int Build, string? Notes);
    private Values ValidateRequest(string actor, DesktopStandaloneReleaseRequest request)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 450 || request is null || request.RequestId == Guid.Empty ||
            !DesktopReleaseVersionPolicy.IsSupportedStableVersion(request.Version) || request.Version == "0.0.0")
            throw Invalid("desktop_standalone_version_invalid", "Nhập phiên bản dạng major.minor.patch, ví dụ 1.2.0.");
        if (request.ReleaseNotes?.Length > 10000) throw Invalid("desktop_release_notes_invalid", "Ghi chú phát hành tối đa 10.000 ký tự.");
        var url = ValidateUrl(request.DownloadUrl, _options);
        try
        {
            var file = Path.GetFileName(new Uri(url).AbsolutePath); var name = DesktopDirectCatalogPolicy.ParseFileName(file);
            if (name.Version != request.Version) throw Invalid("desktop_standalone_version_mismatch", "Phiên bản nhập phải khớp phiên bản trong tên ZIP.");
            return new(request.Version, url, file, name.Sha256, name.BuildNumber, string.IsNullOrWhiteSpace(request.ReleaseNotes) ? null : request.ReleaseNotes.Trim());
        }
        catch (InvalidDataException) { throw Invalid("desktop_standalone_filename_invalid", "Dùng link ZIP có đúng tên phiên bản, build và SHA-256 do công cụ đóng gói tạo."); }
    }

    private string Fingerprint(Values values, DesktopStandaloneReleaseRequest request) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { values.Version, values.Url, values.Notes, request.IsMandatory, _options.Profile,
            api = NormalizeApi(_options.ApiBaseUrl), modelBundleId = ApprovedStaticModelBundle.Current.BundleId,
            catalogProtocol = DesktopStandaloneCatalogProtocol.Version }, Json)))).ToLowerInvariant();
    private static void SetDraft(DesktopDirectRelease entity, Values values, DesktopStandaloneReleaseRequest request)
    { entity.Version = values.Version; entity.DownloadUrl = values.Url; entity.FileName = values.FileName; entity.ExpectedSha256 = values.Sha256; entity.DeclaredBuildNumber = values.Build; entity.ReleaseNotes = values.Notes; entity.IsMandatory = request.IsMandatory; }
    private void ValidateStoredIdentity(DesktopDirectRelease entity)
    {
        RequireScope(entity);
        var url = ValidateUrl(entity.DownloadUrl, _options); var file = Path.GetFileName(new Uri(url).AbsolutePath); var name = DesktopDirectCatalogPolicy.ParseFileName(file);
        if (url != entity.DownloadUrl || file != entity.FileName || name.Version != entity.Version || name.BuildNumber != entity.DeclaredBuildNumber ||
            name.Sha256 != entity.ExpectedSha256 || entity.ApplicationDistributionProtocol != DesktopDistributionProtocol.Shared ||
            entity.MinimumBootstrapperVersion != DesktopStandaloneCatalogProtocol.MinimumSetupVersion)
            throw Invalid("desktop_standalone_binding_invalid", "Thông tin catalog không khớp ZIP và policy hệ thống.");
    }
    private void RequireScope(DesktopDirectRelease entity)
    {
        if (entity.CatalogProtocol != DesktopStandaloneCatalogProtocol.Version || entity.Profile != _options.Profile || entity.Channel != "Stable" ||
            entity.Platform != "win-x64" || entity.ModelBundleId != ApprovedStaticModelBundle.Current.BundleId)
            throw Conflict("desktop_standalone_scope_changed", "Phiên bản không thuộc cấu hình phát hành hiện tại.");
    }
    private void RequireEnabled() { if (!Enabled) throw Conflict("desktop_standalone_catalog_disabled", "Catalog độc lập chưa được bật hoặc cấu hình chưa hợp lệ."); }
    private static void RequireRevision(DesktopDirectRelease entity, Guid? revision)
    { if (revision is null || revision == Guid.Empty || entity.Revision != revision) throw Conflict("desktop_standalone_changed", "Phiên bản vừa đổi. Tải lại trước khi tiếp tục."); }
    private static AccountApiException Missing() => new(404, "desktop_standalone_not_found", "Không tìm thấy phiên bản độc lập.");
    private async Task<DesktopDirectRelease> LoadAsync(Guid id, CancellationToken token) => await db.DesktopDirectReleases.SingleOrDefaultAsync(
        x => x.DirectReleaseId == id && x.CatalogProtocol == DesktopStandaloneCatalogProtocol.Version, token) ?? throw Missing();
    private void Touch(DesktopDirectRelease entity) { entity.Revision = Guid.NewGuid(); entity.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime; }
    private async Task SaveAsync(CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw Conflict("desktop_standalone_changed", "Phiên bản vừa đổi. Tải lại trước khi tiếp tục."); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        { throw Conflict("desktop_standalone_changed", "Phiên bản hoặc mã lưu đã tồn tại. Tải lại trước khi tiếp tục."); }
    }
}

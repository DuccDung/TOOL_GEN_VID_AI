using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Updates;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;
using static TOOL_SERVER.Updates.DesktopSharedReleaseValidation;

namespace TOOL_SERVER.Updates;

/// <summary>Only database and the server-owned Setup store. No HTTP client or ZIP inspector participates in catalog publication.</summary>
public sealed class DesktopDirectCatalogService(AccountDbContext db, DesktopBootstrapperService bootstrappers,
    IOptions<DesktopReleaseOptions> settings, TimeProvider clock)
{
    private readonly DesktopReleaseOptions _options = settings.Value;
    private bool Enabled => _options.PublicationMode == DesktopPublicationModes.DirectCatalog && _options.SharedBootstrapperEnabled && IsConfigured(_options);
    private bool Readable => (_options.PublicationMode is DesktopPublicationModes.DirectCatalog or DesktopPublicationModes.StandaloneCatalog) && _options.SharedBootstrapperEnabled && IsConfigured(_options);

    public async Task<AdminDesktopDirectReleaseResponse> CreateAsync(string actor, DesktopDirectReleaseRequest request, CancellationToken token)
    {
        RequireDirect();
        var values = ValidateRequest(actor, request);
        var fingerprint = CreateFingerprint(values, request);
        var existing = await db.DesktopDirectReleases.SingleOrDefaultAsync(x => x.ActorUserId == actor && x.ClientRequestId == request.RequestId, token);
        if (existing is not null)
        {
            if (existing.CreateRequestFingerprint != fingerprint) throw Conflict("desktop_direct_request_reused", "Mã lưu đã dùng cho nội dung khác. Tải lại bản đã lưu.");
            return await MapAdminAsync(existing, token);
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var entity = new DesktopDirectRelease { ActorUserId = actor, ClientRequestId = request.RequestId, CreateRequestFingerprint = fingerprint,
            Profile = _options.Profile, ModelBundleId = ApprovedStaticModelBundle.Current.BundleId, CreatedAtUtc = now, UpdatedAtUtc = now };
        SetDraftIdentity(entity, values, request);
        db.DesktopDirectReleases.Add(entity); await SaveAsync(token);
        return await MapAdminAsync(entity, token);
    }

    public async Task<AdminDesktopDirectReleaseResponse?> GetAsync(Guid id, CancellationToken token)
    {
        var entity = await db.DesktopDirectReleases.AsNoTracking().SingleOrDefaultAsync(x => x.DirectReleaseId == id && x.CatalogProtocol == DesktopDirectCatalogProtocol.Version, token);
        return entity is null ? null : await MapAdminAsync(entity, token);
    }

    public async Task<PagedResponse<AdminDesktopDirectReleaseResponse>> PageAsync(int page, int pageSize, CancellationToken token)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw Invalid("invalid_page", "Thông tin phân trang không hợp lệ.");
        var query = db.DesktopDirectReleases.Where(x => x.CatalogProtocol == DesktopDirectCatalogProtocol.Version);
        var count = await query.CountAsync(token);
        var effective = count == 0 ? 1 : Math.Min(page, (int)Math.Ceiling(count / (double)pageSize));
        var rows = await query.AsNoTracking().OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.DirectReleaseId)
            .Skip((effective - 1) * pageSize).Take(pageSize).ToListAsync(token);
        var result = new List<AdminDesktopDirectReleaseResponse>();
        foreach (var row in rows) result.Add(await MapAdminAsync(row, token));
        return new(result, effective, pageSize, count);
    }

    public async Task<AdminDesktopDirectReleaseResponse> UpdateAsync(Guid id, string actor, DesktopDirectReleaseRequest request, CancellationToken token)
    {
        RequireDirect(); var values = ValidateRequest(actor, request);
        var entity = await LoadAsync(id, token); RequireRevision(entity, request.Revision); RequireScope(entity);
        if (entity.FirstPublishedAtUtc is not null && (entity.Version != values.Version || entity.DownloadUrl != values.Url))
            throw Conflict("desktop_direct_immutable", "Phiên bản đã từng phát hành. Tạo phiên bản mới để đổi ZIP hoặc link tải.");
        if (entity.FirstPublishedAtUtc is null) SetDraftIdentity(entity, values, request);
        else { entity.ReleaseNotes = values.Notes; entity.IsMandatory = request.IsMandatory; }
        Touch(entity); await SaveAsync(token); return await MapAdminAsync(entity, token);
    }

    public async Task<AdminDesktopDirectReleaseResponse> PublishAsync(Guid id, Guid revision, CancellationToken token)
    {
        RequireDirect();
        var snapshot = await db.DesktopDirectReleases.AsNoTracking().SingleOrDefaultAsync(x => x.DirectReleaseId == id && x.CatalogProtocol == DesktopDirectCatalogProtocol.Version, token) ?? throw MissingDirect();
        RequireRevision(snapshot, revision); RequireScope(snapshot); ValidateStoredIdentity(snapshot);
        var verified = await bootstrappers.VerifyActiveAsync(snapshot.MinimumBootstrapperVersion, token);
        if (verified is null || !DesktopDirectCatalogPolicy.SupportsCatalog(verified))
            throw Conflict("desktop_direct_bootstrapper_required", "Chưa có Setup hệ thống Active hỗ trợ catalog trực tiếp. Hãy đăng ký Setup 1.2.0 hoặc mới hơn.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var entity = await LoadAsync(id, token); RequireRevision(entity, revision); RequireScope(entity); ValidateStoredIdentity(entity);
        var current = await bootstrappers.ActiveAsync(entity.MinimumBootstrapperVersion, token);
        if (current != verified) throw Conflict("desktop_bootstrapper_changed", "Setup hệ thống vừa đổi. Hãy phát hành lại.");
        if (await db.AppReleases.AnyAsync(x => x.Version == entity.Version && x.Channel == entity.Channel && x.Platform == entity.Platform
                && x.FirstPublishedAtUtc != null && (x.BuildNumber != entity.DeclaredBuildNumber
                    || x.Artifacts.Any(a => a.Kind == DesktopArtifactKinds.DesktopPackage && a.Sha256 != entity.ExpectedSha256)), token))
            throw Conflict("desktop_direct_version_conflict", "Phiên bản này đã phát hành ZIP khác ở luồng cũ. Hãy tăng phiên bản.");
        var now = clock.GetUtcNow().UtcDateTime;
        entity.FirstPublishedAtUtc ??= now; entity.PublishedAtUtc = now; entity.IsActive = true; Touch(entity);
        await SaveAsync(token); await transaction.CommitAsync(token);
        return await MapAdminAsync(entity, token);
    }

    public async Task<AdminDesktopDirectReleaseResponse> DeactivateAsync(Guid id, Guid revision, CancellationToken token)
    {
        RequireDirect(); var entity = await LoadAsync(id, token); RequireRevision(entity, revision); RequireScope(entity);
        if (entity.FirstPublishedAtUtc is null) throw Conflict("desktop_direct_not_published", "Phiên bản chưa từng được phát hành.");
        entity.IsActive = false; Touch(entity); await SaveAsync(token); return await MapAdminAsync(entity, token);
    }

    public async Task<IReadOnlyList<DesktopDirectReleaseResponse>> VisibleAsync(string profile, string channel, string platform, Guid? bootstrapperId, CancellationToken token,
        string? bootstrapperVersion = null)
    {
        if (!Readable || profile != _options.Profile || channel != "Stable" || platform != "win-x64") return [];
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.DesktopDirectReleases.AsNoTracking().Where(x => x.CatalogProtocol == DesktopDirectCatalogProtocol.Version && x.Profile == profile && x.Channel == channel && x.Platform == platform
            && x.IsActive && x.FirstPublishedAtUtc != null && x.PublishedAtUtc <= now).ToListAsync(token);
        var result = new List<DesktopDirectReleaseResponse>();
        foreach (var entity in rows)
            if (await MapPublicAsync(entity, bootstrapperId, token, bootstrapperVersion) is { } item) result.Add(item);
        result.Sort((a, b) => DesktopReleaseVersionPolicy.CompareVersions(b.Version, a.Version));
        return result;
    }

    public async Task<DesktopDirectReleaseResponse?> ExactAsync(Guid id, string profile, Guid? bootstrapperId, CancellationToken token)
    {
        if (!Readable || profile != _options.Profile) return null;
        var now = clock.GetUtcNow().UtcDateTime;
        var entity = await db.DesktopDirectReleases.AsNoTracking().SingleOrDefaultAsync(x => x.DirectReleaseId == id && x.CatalogProtocol == DesktopDirectCatalogProtocol.Version && x.Profile == profile
            && x.IsActive && x.FirstPublishedAtUtc != null && x.PublishedAtUtc <= now, token);
        return entity is null ? null : await MapPublicAsync(entity, bootstrapperId, token);
    }

    private async Task<DesktopDirectReleaseResponse?> MapPublicAsync(DesktopDirectRelease entity, Guid? bootstrapperId, CancellationToken token,
        string? bootstrapperVersion = null)
    {
        try
        {
            ValidateStoredIdentity(entity);
            var setup = bootstrapperId is { } selected
                ? await bootstrappers.CompatibleAsync(selected, entity.MinimumBootstrapperVersion, token)
                : bootstrapperVersion is not null
                    ? await bootstrappers.CompatibleVersionAsync(bootstrapperVersion, entity.MinimumBootstrapperVersion, token)
                    : await bootstrappers.ActiveAsync(entity.MinimumBootstrapperVersion, token);
            if (setup is null || bootstrapperVersion is not null && setup.Version != bootstrapperVersion || !DesktopDirectCatalogPolicy.SupportsCatalog(setup)) return null;
            var result = new DesktopDirectReleaseResponse(entity.DirectReleaseId, entity.Version, entity.DeclaredBuildNumber, entity.FileName,
                entity.DownloadUrl, entity.ExpectedSha256, entity.Profile, entity.Channel, entity.Platform, entity.ModelBundleId,
                entity.Revision, entity.PublishedAtUtc!.Value, entity.IsMandatory, entity.ReleaseNotes, setup,
                entity.CatalogProtocol, entity.ApplicationDistributionProtocol, entity.MinimumBootstrapperVersion);
            DesktopDirectCatalogPolicy.ValidateRelease(result, new Uri(_options.ApiBaseUrl), _options.Profile);
            return result;
        }
        catch (Exception error) when (error is TOOL_SERVER.Authentication.AccountApiException or InvalidDataException or ArgumentException) { return null; }
    }

    private async Task<AdminDesktopDirectReleaseResponse> MapAdminAsync(DesktopDirectRelease entity, CancellationToken token)
    {
        var setup = await bootstrappers.ActiveAsync(entity.MinimumBootstrapperVersion, token);
        if (setup is not null && !DesktopDirectCatalogPolicy.SupportsCatalog(setup)) setup = null;
        return new(entity.DirectReleaseId, entity.Version, entity.DeclaredBuildNumber, entity.DownloadUrl, entity.ExpectedSha256,
            entity.Profile, entity.Channel, entity.Platform, entity.FirstPublishedAtUtc is null ? DesktopDirectReleaseStatuses.Draft : DesktopDirectReleaseStatuses.Published,
            entity.IsActive, entity.IsMandatory, entity.ReleaseNotes, entity.Revision, entity.CreatedAtUtc, entity.UpdatedAtUtc, entity.FirstPublishedAtUtc,
            setup, Enabled && entity.Profile == _options.Profile && setup is not null,
            setup is null ? "desktop_direct_bootstrapper_required" : null,
            setup is null ? "Chưa có Setup hệ thống Active hỗ trợ catalog trực tiếp (1.2.0 trở lên)." : null);
    }

    private sealed record Values(string Version, string Url, string FileName, string Sha256, int Build, string? Notes);
    private Values ValidateRequest(string actor, DesktopDirectReleaseRequest request)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 450 || request is null || request.RequestId == Guid.Empty
            || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(request.Version) || request.Version == "0.0.0")
            throw Invalid("desktop_direct_version_invalid", "Nhập phiên bản dạng major.minor.patch, ví dụ 1.2.0.");
        if (request.ReleaseNotes?.Length > 10000) throw Invalid("desktop_release_notes_invalid", "Ghi chú phát hành tối đa 10.000 ký tự.");
        var url = ValidateUrl(request.DownloadUrl, _options);
        try
        {
            var file = Path.GetFileName(new Uri(url).AbsolutePath);
            var name = DesktopDirectCatalogPolicy.ParseFileName(file);
            if (name.Version != request.Version) throw Invalid("desktop_direct_version_mismatch", "Phiên bản nhập phải khớp phiên bản trong tên ZIP.");
            return new(request.Version, url, file, name.Sha256, name.BuildNumber, string.IsNullOrWhiteSpace(request.ReleaseNotes) ? null : request.ReleaseNotes.Trim());
        }
        catch (InvalidDataException) { throw Invalid("desktop_direct_filename_invalid", "Link phải trỏ tới ZIP có tên VideoMaker-<phiên bản>-<build>-win-x64-<SHA256>.zip. Dùng tên gói do công cụ đóng gói tạo."); }
    }
    private string CreateFingerprint(Values values, DesktopDirectReleaseRequest request) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { values.Version, values.Url, values.Notes, request.IsMandatory, _options.Profile, api = NormalizeApi(_options.ApiBaseUrl),
            channel = "Stable", platform = "win-x64", modelBundleId = ApprovedStaticModelBundle.Current.BundleId, catalogProtocol = 4 }, Json)))).ToLowerInvariant();
    private static void SetDraftIdentity(DesktopDirectRelease entity, Values values, DesktopDirectReleaseRequest request)
    {
        entity.Version = values.Version; entity.DownloadUrl = values.Url; entity.FileName = values.FileName;
        entity.ExpectedSha256 = values.Sha256; entity.DeclaredBuildNumber = values.Build; entity.ReleaseNotes = values.Notes; entity.IsMandatory = request.IsMandatory;
    }
    private void ValidateStoredIdentity(DesktopDirectRelease entity)
    {
        RequireScope(entity);
        var url = ValidateUrl(entity.DownloadUrl, _options); var file = Path.GetFileName(new Uri(url).AbsolutePath);
        var name = DesktopDirectCatalogPolicy.ParseFileName(file);
        if (url != entity.DownloadUrl || file != entity.FileName || name.Version != entity.Version || name.BuildNumber != entity.DeclaredBuildNumber
            || name.Sha256 != entity.ExpectedSha256 || entity.CatalogProtocol != 4 || entity.ApplicationDistributionProtocol != 3
            || entity.MinimumBootstrapperVersion != DesktopDirectCatalogProtocol.MinimumBootstrapperVersion)
            throw Invalid("desktop_direct_binding_invalid", "Thông tin catalog không khớp tên ZIP và policy hệ thống.");
    }
    private void RequireScope(DesktopDirectRelease entity)
    {
        if (entity.Profile != _options.Profile || entity.Channel != "Stable" || entity.Platform != "win-x64" || entity.ModelBundleId != ApprovedStaticModelBundle.Current.BundleId)
            throw Conflict("desktop_direct_scope_changed", "Phiên bản không thuộc cấu hình phát hành hiện tại.");
    }
    private void RequireDirect() { if (!Enabled) throw Conflict("desktop_direct_catalog_disabled", "Catalog trực tiếp chưa được bật hoặc cấu hình chưa hợp lệ."); }
    private static void RequireRevision(DesktopDirectRelease entity, Guid? revision)
    { if (revision is null || revision == Guid.Empty || entity.Revision != revision) throw Conflict("desktop_direct_changed", "Phiên bản vừa đổi. Tải lại trước khi tiếp tục."); }
    private static TOOL_SERVER.Authentication.AccountApiException MissingDirect() => new(404, "desktop_direct_not_found", "Không tìm thấy phiên bản trực tiếp.");
    private async Task<DesktopDirectRelease> LoadAsync(Guid id, CancellationToken token) => await db.DesktopDirectReleases.SingleOrDefaultAsync(x => x.DirectReleaseId == id && x.CatalogProtocol == DesktopDirectCatalogProtocol.Version, token) ?? throw MissingDirect();
    private void Touch(DesktopDirectRelease entity) { entity.Revision = Guid.NewGuid(); entity.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime; }
    private async Task SaveAsync(CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException) { throw Conflict("desktop_direct_changed", "Phiên bản hoặc mã lưu đã tồn tại/vừa đổi. Tải lại danh sách trước khi tiếp tục."); }
    }
}

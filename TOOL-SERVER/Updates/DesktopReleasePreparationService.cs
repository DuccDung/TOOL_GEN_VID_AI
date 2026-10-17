using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Updates;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.Updates;
using static TOOL_SERVER.Updates.DesktopSharedReleaseValidation;

namespace TOOL_SERVER.Updates;

public sealed class DesktopReleasePreparationService(AccountDbContext db, DbContextOptions<AccountDbContext> contextOptions,
    IDesktopReleaseArchiveInspector inspector, DesktopBootstrapperService bootstrappers, IOptions<DesktopReleaseOptions> settings, TimeProvider clock)
{
    private readonly DesktopReleaseOptions _options = settings.Value;

    public async Task<DesktopReleasePreparationResponse> CreateAsync(string actor, DesktopReleasePreparationRequest request, CancellationToken token)
    {
        RequireInspectionMode();
        RequireEnabled(_options);
        var (version, url, notes) = ValidateRequest(actor, request);
        var fingerprint = Fingerprint(version, url, _options);
        var createFingerprint = CreateFingerprint(fingerprint, notes, request.IsMandatory);
        var existing = await db.DesktopReleasePreparations.SingleOrDefaultAsync(x => x.ActorUserId == actor && x.ClientRequestId == request.RequestId, token);
        if (existing is not null)
        {
            if (existing.CreateRequestFingerprint != createFingerprint)
                throw Conflict("desktop_preparation_request_reused", "Mã lưu nháp đã dùng cho nội dung khác. Tải lại nháp hiện có.");
            return await MapAsync(existing, token);
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var entity = new DesktopReleasePreparation
        {
            ActorUserId = actor, ClientRequestId = request.RequestId, Version = version, DownloadUrl = url,
            ReleaseNotes = notes, IsMandatory = request.IsMandatory, Profile = _options.Profile,
            InputFingerprint = fingerprint, CreateRequestFingerprint = createFingerprint, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        db.DesktopReleasePreparations.Add(entity);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException) { throw Conflict("desktop_preparation_changed", "Nháp vừa được lưu ở lượt khác. Tải lại danh sách."); }
        return await MapAsync(entity, token);
    }

    public async Task<DesktopReleasePreparationResponse?> GetAsync(Guid id, CancellationToken token)
    {
        var entity = await db.DesktopReleasePreparations.AsNoTracking().SingleOrDefaultAsync(x => x.PreparationId == id, token);
        return entity is null ? null : await MapAsync(entity, token);
    }

    public async Task<DesktopReleasePreparationResponse?> ByReleaseAsync(Guid releaseId, CancellationToken token)
    {
        var entity = await db.DesktopReleasePreparations.AsNoTracking().SingleOrDefaultAsync(x => x.ReleaseId == releaseId, token);
        return entity is null ? null : await MapAsync(entity, token);
    }

    public async Task<PagedResponse<DesktopReleasePreparationResponse>> PageAsync(int page, int pageSize, CancellationToken token)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw Invalid("invalid_page", "Thông tin phân trang không hợp lệ.");
        var count = await db.DesktopReleasePreparations.CountAsync(token);
        var effective = count == 0 ? 1 : Math.Min(page, (int)Math.Ceiling(count / (double)pageSize));
        var items = await db.DesktopReleasePreparations.AsNoTracking().OrderByDescending(x => x.UpdatedAtUtc)
            .ThenBy(x => x.PreparationId).Skip((effective - 1) * pageSize).Take(pageSize).ToListAsync(token);
        var mapped = new List<DesktopReleasePreparationResponse>();
        foreach (var entity in items) mapped.Add(await MapAsync(entity, token));
        return new(mapped, effective, pageSize, count);
    }

    public async Task<DesktopReleasePreparationResponse> UpdateAsync(Guid id, string actor, DesktopReleasePreparationRequest request, CancellationToken token)
    {
        RequireInspectionMode();
        RequireEnabled(_options);
        var (version, url, notes) = ValidateRequest(actor, request);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var entity = await LoadAsync(id, token);
        RequireRevision(entity, request.Revision);
        var release = entity.ReleaseId is { } releaseId ? await db.AppReleases.Include(x => x.Artifacts).SingleAsync(x => x.AppReleaseId == releaseId, token) : null;
        var fingerprint = Fingerprint(version, url, _options);
        var identityChanged = entity.InputFingerprint != fingerprint;
        if (identityChanged && release?.FirstPublishedAtUtc is not null)
            throw Conflict("desktop_release_immutable", "Release đã từng phát hành. Dùng bản phát hành mới để đổi phiên bản/link ZIP.");
        entity.ReleaseNotes = notes; entity.IsMandatory = request.IsMandatory;
        if (identityChanged)
        {
            entity.Version = version; entity.DownloadUrl = url; entity.InputFingerprint = fingerprint;
            entity.Profile = _options.Profile; entity.Generation++;
            ResetQueued(entity);
            if (release is not null)
            {
                release.IsActive = false;
                // Old bytes/identity remain as draft data until a new inspection succeeds.
                var artifact = release.Artifacts.SingleOrDefault(x => x.Kind == DesktopArtifactKinds.DesktopPackage);
                if (artifact is not null) artifact.VerifiedPreparationId = null;
            }
        }
        if (release is not null)
        {
            release.ReleaseNotes = notes; release.IsMandatory = request.IsMandatory; release.Revision = Guid.NewGuid();
        }
        Touch(entity);
        await SaveAsync(token);
        await transaction.CommitAsync(token);
        return await MapAsync(entity, token);
    }

    public async Task<DesktopReleasePreparationResponse> RetryAsync(Guid id, Guid revision, CancellationToken token)
    {
        RequireInspectionMode();
        RequireEnabled(_options);
        var entity = await LoadAsync(id, token); RequireRevision(entity, revision);
        if (entity.State is not (DesktopReleasePreparationStatuses.Failed or DesktopReleasePreparationStatuses.Cancelled))
            throw Conflict("desktop_preparation_not_retryable", "Chỉ thử lại nháp kiểm tra lỗi hoặc đã hủy.");
        if (entity.ReleaseId is { } releaseId && await db.AppReleases.AnyAsync(x => x.AppReleaseId == releaseId && x.FirstPublishedAtUtc != null, token))
            throw Conflict("desktop_release_immutable", "Release đã từng phát hành; không kiểm lại để thay nội dung.");
        entity.Generation++; ResetQueued(entity); Touch(entity);
        await SaveAsync(token); return await MapAsync(entity, token);
    }

    public async Task<DesktopReleasePreparationResponse> CancelAsync(Guid id, Guid revision, CancellationToken token)
    {
        var entity = await LoadAsync(id, token); RequireRevision(entity, revision);
        if (entity.State is DesktopReleasePreparationStatuses.Ready or DesktopReleasePreparationStatuses.Published)
            throw Conflict("desktop_preparation_not_running", "Nháp đã kiểm tra/phát hành, không còn tác vụ đang chạy để hủy.");
        entity.CancellationRequested = true; entity.Generation++;
        entity.State = DesktopReleasePreparationStatuses.Cancelled; entity.Stage = "cancelled";
        entity.LeaseOwner = null; entity.LeaseExpiresAtUtc = null; Touch(entity);
        await SaveAsync(token); return await MapAsync(entity, token);
    }

    public async Task<DesktopReleasePreparationResponse> PublishAsync(Guid id, Guid revision, CancellationToken token)
    {
        RequireInspectionMode();
        RequireEnabled(_options);
        var snapshot = await db.DesktopReleasePreparations.AsNoTracking().SingleOrDefaultAsync(x => x.PreparationId == id, token) ?? throw Missing();
        RequireRevision(snapshot, revision);
        if (snapshot.State is not (DesktopReleasePreparationStatuses.Ready or DesktopReleasePreparationStatuses.Published)
            || snapshot.VerifiedAtUtc is null || snapshot.ReleaseId is null)
            throw Conflict("desktop_preparation_not_ready", "Phiên bản/link hiện tại chưa có kết quả kiểm tra đạt.");
        var snapshotEvidence = ReadEvidence(snapshot);
        ValidateInspection(snapshotEvidence, snapshot.Version, _options);
        var verifiedBootstrapper = await bootstrappers.VerifyActiveAsync(snapshotEvidence.DistributionProfile.MinimumBootstrapperVersion, token)
            ?? throw Conflict("desktop_shared_bootstrapper_required", "Chưa có Setup hệ thống Active tương thích. Hãy cấu hình Setup ở mục riêng.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var entity = await LoadAsync(id, token); RequireRevision(entity, revision);
        if (entity.State is not (DesktopReleasePreparationStatuses.Ready or DesktopReleasePreparationStatuses.Published)
            || entity.ReleaseId is not { } releaseId || entity.VerifiedAtUtc is null
            || entity.InputFingerprint != Fingerprint(entity.Version, entity.DownloadUrl, _options))
            throw Conflict("desktop_preparation_not_ready", "Phiên bản/link hiện tại chưa có kết quả kiểm tra đạt.");
        var evidence = ReadEvidence(entity);
        ValidateInspection(evidence, entity.Version, _options);
        var currentBootstrapper = await bootstrappers.ActiveAsync(evidence.DistributionProfile.MinimumBootstrapperVersion, token);
        if (currentBootstrapper is null || currentBootstrapper.BootstrapperId != verifiedBootstrapper.BootstrapperId || currentBootstrapper.Sha256 != verifiedBootstrapper.Sha256)
            throw Conflict("desktop_bootstrapper_changed", "Setup hệ thống vừa đổi. Hãy thử phát hành lại.");
        var release = await db.AppReleases.Include(x => x.Artifacts).SingleAsync(x => x.AppReleaseId == releaseId, token);
        var artifact = release.Artifacts.SingleOrDefault(x => x.Kind == DesktopArtifactKinds.DesktopPackage);
        EnsureArtifactMatches(entity, artifact, evidence, release);
        release.IsActive = true; release.FirstPublishedAtUtc ??= clock.GetUtcNow().UtcDateTime;
        release.PublishedAtUtc = clock.GetUtcNow().UtcDateTime; release.Revision = Guid.NewGuid();
        entity.State = DesktopReleasePreparationStatuses.Published; Touch(entity);
        await SaveAsync(token); await transaction.CommitAsync(token);
        return await MapAsync(entity, token);
    }

    public async Task<DesktopReleasePreparationResponse> DeactivateAsync(Guid id, Guid revision, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var entity = await LoadAsync(id, token); RequireRevision(entity, revision);
        if (entity.ReleaseId is not { } releaseId) throw Conflict("desktop_preparation_not_published", "Nháp chưa có release đã phát hành.");
        var release = await db.AppReleases.SingleAsync(x => x.AppReleaseId == releaseId, token);
        if (release.FirstPublishedAtUtc is null) throw Conflict("desktop_preparation_not_published", "Nháp chưa từng được phát hành.");
        release.IsActive = false; release.Revision = Guid.NewGuid();
        entity.State = DesktopReleasePreparationStatuses.Published; Touch(entity);
        await SaveAsync(token); await transaction.CommitAsync(token);
        return await MapAsync(entity, token);
    }

    /// <summary>Claim one database-backed job. Every progress/completion write is fenced by generation and owner.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken stoppingToken)
    {
        if (_options.PublicationMode != DesktopReleaseOptions.ServerInspectionMode || !_options.SharedBootstrapperEnabled || !IsConfigured(_options)) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        var candidate = await db.DesktopReleasePreparations.AsNoTracking().Where(x => !x.CancellationRequested &&
            (x.State == DesktopReleasePreparationStatuses.Queued && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now)
             || (x.State == DesktopReleasePreparationStatuses.Downloading || x.State == DesktopReleasePreparationStatuses.Inspecting) && x.LeaseExpiresAtUtc <= now))
            .OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync(stoppingToken);
        if (candidate is null) return false;
        var owner = Guid.NewGuid(); var expiry = now.AddSeconds(_options.PreparationLeaseSeconds);
        var claimed = await db.DesktopReleasePreparations.Where(x => x.PreparationId == candidate.PreparationId && x.Generation == candidate.Generation
            && !x.CancellationRequested && (x.State == DesktopReleasePreparationStatuses.Queued && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now)
                || (x.State == DesktopReleasePreparationStatuses.Downloading || x.State == DesktopReleasePreparationStatuses.Inspecting) && x.LeaseExpiresAtUtc <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, DesktopReleasePreparationStatuses.Downloading)
                .SetProperty(x => x.Stage, "downloading").SetProperty(x => x.LeaseOwner, (Guid?)owner)
                .SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)expiry).SetProperty(x => x.HeartbeatAtUtc, (DateTime?)now)
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount < _options.MaximumPreparationAttempts ? x.AttemptCount + 1 : x.AttemptCount)
                .SetProperty(x => x.UpdatedAtUtc, now), stoppingToken);
        if (claimed == 0) return true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(_options.InspectionTimeoutSeconds));
        var progress = new ProgressSink();
        Task<DesktopReleaseInspectionResult>? inspection = null;
        try
        {
            if (candidate.AttemptCount >= _options.MaximumPreparationAttempts)
                throw Invalid("desktop_preparation_attempts_exhausted", "Đã hết số lần kiểm tự động. Hãy kiểm tra nguồn rồi bấm Thử lại.");
            inspection = inspector.InspectAsync(new DesktopReleaseInspectionRequest(candidate.PreparationId, candidate.Generation,
                candidate.DownloadUrl, candidate.Version), progress, cancellation.Token);
            while (!inspection.IsCompleted)
            {
                var delay = Task.Delay(TimeSpan.FromSeconds(Math.Min(5, Math.Max(1, _options.PreparationLeaseSeconds / 3))), stoppingToken);
                if (await Task.WhenAny(inspection, delay) == inspection) break;
                if (!await HeartbeatAsync(candidate, owner, progress.Latest, stoppingToken)) { cancellation.Cancel(); break; }
            }
            var result = await inspection;
            ValidateInspection(result, candidate.Version, _options);
            await CompleteAsync(candidate, owner, result, stoppingToken);
        }
        catch (Exception error)
        {
            cancellation.Cancel();
            if (inspection is not null)
            {
                // The inspector owns private files and a bounded HTTP stream. Drain cancellation before releasing this claim.
                try { await inspection; } catch { }
            }
            var code = error is DesktopReleaseInspectionException inspectionError ? inspectionError.Code : error is AccountApiException api ? api.Code : error is OperationCanceledException
                ? "desktop_release_inspection_interrupted" : error is HttpRequestException ? "desktop_release_download_failed" : "desktop_release_inspection_failed";
            var message = error is DesktopReleaseInspectionException safeInspection ? safeInspection.SafeMessage : error is AccountApiException safe ? safe.Message : "Chưa kiểm tra được ZIP. Hãy kiểm tra nguồn, dung lượng/quyền ghi rồi thử lại.";
            var retry = stoppingToken.IsCancellationRequested || error is HttpRequestException or TimeoutException
                || error is OperationCanceledException && cancellation.IsCancellationRequested
                || error is DesktopReleaseInspectionException { Code: "desktop_preparation_download_failed" or "desktop_preparation_timeout" };
            await FailOwnedAsync(candidate, owner, code, message, retry, CancellationToken.None);
        }
        return true;
    }

    private async Task<bool> HeartbeatAsync(DesktopReleasePreparation candidate, Guid owner, DesktopReleaseInspectionProgress? progress, CancellationToken token)
    {
        await using var heartbeat = new AccountDbContext(contextOptions);
        var now = clock.GetUtcNow().UtcDateTime;
        var bytes = Math.Clamp(progress?.BytesReceived ?? 0, 0, _options.MaximumExternalArtifactBytes);
        var total = progress?.TotalBytes;
        var state = string.Equals(progress?.Stage, DesktopReleasePreparationStatuses.Inspecting, StringComparison.OrdinalIgnoreCase)
            ? DesktopReleasePreparationStatuses.Inspecting : DesktopReleasePreparationStatuses.Downloading;
        return await heartbeat.DesktopReleasePreparations.Where(x => x.PreparationId == candidate.PreparationId && x.Generation == candidate.Generation
                && x.InputFingerprint == candidate.InputFingerprint && x.LeaseOwner == owner && x.LeaseExpiresAtUtc > now && !x.CancellationRequested
                && (x.State == DesktopReleasePreparationStatuses.Downloading || x.State == DesktopReleasePreparationStatuses.Inspecting))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, state).SetProperty(x => x.Stage, state == DesktopReleasePreparationStatuses.Inspecting ? "inspecting" : "downloading")
                .SetProperty(x => x.BytesReceived, bytes).SetProperty(x => x.TotalBytes, total)
                .SetProperty(x => x.HeartbeatAtUtc, (DateTime?)now).SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)now.AddSeconds(_options.PreparationLeaseSeconds))
                .SetProperty(x => x.UpdatedAtUtc, now), token) == 1;
    }

    private async Task CompleteAsync(DesktopReleasePreparation candidate, Guid owner, DesktopReleaseInspectionResult evidence, CancellationToken token)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var entity = await db.DesktopReleasePreparations.SingleAsync(x => x.PreparationId == candidate.PreparationId, token);
        if (!Owns(entity, candidate, owner) || entity.LeaseExpiresAtUtc <= clock.GetUtcNow().UtcDateTime) return;
        var json = SerializeEvidence(evidence);
        AppRelease release;
        if (entity.ReleaseId is { } releaseId)
        {
            release = await db.AppReleases.Include(x => x.Artifacts).SingleAsync(x => x.AppReleaseId == releaseId, token);
            if (release.FirstPublishedAtUtc is not null) throw Conflict("desktop_release_immutable", "Release đã từng phát hành, không thay nội dung từ worker.");
        }
        else
        {
            if (await db.AppReleases.AnyAsync(x => x.Version == entity.Version && x.BuildNumber == evidence.PackageManifest.BuildNumber
                    && x.Channel == entity.Channel && x.Platform == entity.Platform, token))
                throw Conflict("desktop_release_exists", "Phiên bản/build này đã có. Nội dung đã có không bị ghi đè.");
            release = new AppRelease { AppReleaseId = Guid.NewGuid(), Channel = entity.Channel, Platform = entity.Platform };
            db.AppReleases.Add(release);
        }
        release.Version = entity.Version; release.BuildNumber = evidence.PackageManifest.BuildNumber;
        release.ReleaseNotes = entity.ReleaseNotes; release.IsMandatory = entity.IsMandatory; release.IsActive = false;
        release.PublishedAtUtc = clock.GetUtcNow().UtcDateTime; release.Revision = Guid.NewGuid();
        var artifact = release.Artifacts.SingleOrDefault(x => x.Kind == DesktopArtifactKinds.DesktopPackage);
        if (artifact is null) { artifact = new AppReleaseArtifact { AppReleaseArtifactId = Guid.NewGuid(), Kind = DesktopArtifactKinds.DesktopPackage }; release.Artifacts.Add(artifact); }
        artifact.SourceKind = DesktopArtifactSourceKinds.ExternalVps; artifact.RelativePath = null; artifact.ExternalDownloadUrl = entity.DownloadUrl;
        artifact.FileName = evidence.FileName; artifact.SizeBytes = evidence.SizeBytes; artifact.Sha256 = evidence.Sha256.ToLowerInvariant();
        artifact.InstallMode = DesktopInstallModes.StaticArchive; artifact.DistributionProtocol = DesktopDistributionProtocol.Shared;
        artifact.ModelBundleId = evidence.DistributionProfile.ModelBundleId; artifact.BoundPackageSha256 = artifact.Sha256;
        artifact.RegistrationEvidenceJson = json; artifact.VerifiedPreparationId = entity.PreparationId; artifact.CreatedAtUtc = clock.GetUtcNow().UtcDateTime;
        entity.ReleaseId = release.AppReleaseId; entity.InspectionEvidenceJson = json; entity.VerifiedAtUtc = clock.GetUtcNow().UtcDateTime;
        entity.State = DesktopReleasePreparationStatuses.Ready; entity.Stage = "ready"; entity.ErrorCode = null; entity.ErrorMessage = null;
        entity.BytesReceived = evidence.SizeBytes; entity.TotalBytes = evidence.SizeBytes; entity.LeaseOwner = null; entity.LeaseExpiresAtUtc = null;
        Touch(entity); await SaveAsync(token); await transaction.CommitAsync(token);
    }

    private async Task FailOwnedAsync(DesktopReleasePreparation candidate, Guid owner, string code, string message, bool retry, CancellationToken token)
    {
        db.ChangeTracker.Clear();
        var now = clock.GetUtcNow().UtcDateTime;
        var queued = retry && candidate.AttemptCount + 1 < _options.MaximumPreparationAttempts;
        await db.DesktopReleasePreparations.Where(x => x.PreparationId == candidate.PreparationId && x.Generation == candidate.Generation
                && x.InputFingerprint == candidate.InputFingerprint && x.LeaseOwner == owner && x.LeaseExpiresAtUtc > now && !x.CancellationRequested
                && (x.State == DesktopReleasePreparationStatuses.Downloading || x.State == DesktopReleasePreparationStatuses.Inspecting))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, queued ? DesktopReleasePreparationStatuses.Queued : DesktopReleasePreparationStatuses.Failed)
                .SetProperty(x => x.Stage, queued ? "queued" : "failed").SetProperty(x => x.ErrorCode, code).SetProperty(x => x.ErrorMessage, message)
                .SetProperty(x => x.LeaseOwner, (Guid?)null).SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(x => x.NextAttemptAtUtc, queued ? (DateTime?)now.AddSeconds(_options.InspectionRetryDelaySeconds) : null)
                .SetProperty(x => x.UpdatedAtUtc, now).SetProperty(x => x.Revision, Guid.NewGuid()), token);
    }

    internal static bool Owns(DesktopReleasePreparation entity, DesktopReleasePreparation candidate, Guid owner) =>
        entity.Generation == candidate.Generation && entity.InputFingerprint == candidate.InputFingerprint && entity.LeaseOwner == owner
        && !entity.CancellationRequested && entity.State is DesktopReleasePreparationStatuses.Downloading or DesktopReleasePreparationStatuses.Inspecting;

    internal static DesktopReleaseInspectionResult ReadEvidence(DesktopReleasePreparation entity)
    {
        if (entity.InspectionEvidenceJson is not { Length: > 0 and <= 4 * 1024 * 1024 }) throw Invalid("desktop_preparation_not_ready", "Thiếu bằng chứng kiểm ZIP hiện hành.");
        try { return JsonSerializer.Deserialize<DesktopReleaseInspectionResult>(entity.InspectionEvidenceJson, Json) ?? throw new JsonException(); }
        catch (JsonException) { throw Invalid("desktop_preparation_not_ready", "Bằng chứng kiểm ZIP không hợp lệ."); }
    }

    internal static void EnsureArtifactMatches(DesktopReleasePreparation entity, AppReleaseArtifact? artifact, DesktopReleaseInspectionResult evidence, AppRelease release)
    {
        if (artifact is null || artifact.VerifiedPreparationId != entity.PreparationId || artifact.DistributionProtocol != DesktopDistributionProtocol.Shared
            || artifact.SourceKind != DesktopArtifactSourceKinds.ExternalVps || artifact.RelativePath is not null || artifact.ExternalDownloadUrl != entity.DownloadUrl
            || artifact.Sha256 != evidence.Sha256.ToLowerInvariant() || artifact.BoundPackageSha256 != artifact.Sha256
            || artifact.SizeBytes != evidence.SizeBytes || artifact.FileName != evidence.FileName || artifact.ModelBundleId != evidence.DistributionProfile.ModelBundleId
            || artifact.InstallMode != DesktopInstallModes.StaticArchive || artifact.RegistrationEvidenceJson != entity.InspectionEvidenceJson
            || release.Version != entity.Version || release.BuildNumber != evidence.PackageManifest.BuildNumber || release.Channel != "Stable" || release.Platform != "win-x64")
            throw Invalid("desktop_release_binding_invalid", "Release không khớp bằng chứng kiểm ZIP hiện hành.");
    }

    private async Task<DesktopReleasePreparationResponse> MapAsync(DesktopReleasePreparation entity, CancellationToken token)
    {
        var release = entity.ReleaseId is { } releaseId ? await db.AppReleases.AsNoTracking().SingleOrDefaultAsync(x => x.AppReleaseId == releaseId, token) : null;
        DesktopReleaseInspectionResult? evidence = null;
        try { if (entity.VerifiedAtUtc is not null) evidence = ReadEvidence(entity); } catch (AccountApiException) { }
        var bootstrapper = evidence is null ? null : await bootstrappers.ActiveAsync(evidence.DistributionProfile.MinimumBootstrapperVersion, token);
        var canPublish = entity.State is DesktopReleasePreparationStatuses.Ready or DesktopReleasePreparationStatuses.Published
            && evidence is not null && bootstrapper is not null && _options.SharedBootstrapperEnabled
            && entity.InputFingerprint == Fingerprint(entity.Version, entity.DownloadUrl, _options);
        var message = entity.ErrorMessage ?? (entity.State == DesktopReleasePreparationStatuses.Ready && bootstrapper is null
            ? "Chưa có Setup hệ thống Active tương thích. Hãy cấu hình Setup ở mục riêng." : null);
        return new(entity.PreparationId, entity.Version, entity.DownloadUrl, entity.ReleaseNotes, entity.IsMandatory,
            release?.FirstPublishedAtUtc is not null ? DesktopReleasePreparationStatuses.Published : entity.State, entity.Revision, entity.CreatedAtUtc, entity.UpdatedAtUtc,
            entity.ReleaseId, evidence?.FileName, evidence?.SizeBytes, evidence?.Sha256, evidence?.PackageManifest.BuildNumber,
            entity.BytesReceived, entity.TotalBytes, entity.ErrorCode, message, canPublish, bootstrapper, release?.Revision,
            release?.FirstPublishedAtUtc is not null, entity.Generation, release?.IsActive ?? false);
    }

    private (string Version, string Url, string? Notes) ValidateRequest(string actor, DesktopReleasePreparationRequest request)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 450 || request is null || request.RequestId == Guid.Empty
            || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(request.Version) || DesktopReleaseVersionPolicy.CompareVersions(request.Version, "0") <= 0)
            throw Invalid("desktop_release_version_invalid", "Nhập phiên bản dạng major.minor.patch, ví dụ 1.2.0.");
        if (request.ReleaseNotes?.Length > 10000) throw Invalid("desktop_release_notes_invalid", "Ghi chú phát hành tối đa 10.000 ký tự.");
        return (request.Version, ValidateUrl(request.DownloadUrl, _options), string.IsNullOrWhiteSpace(request.ReleaseNotes) ? null : request.ReleaseNotes.Trim());
    }
    private Task<DesktopReleasePreparation> LoadAsync(Guid id, CancellationToken token) => LoadCoreAsync(id, token);
    private async Task<DesktopReleasePreparation> LoadCoreAsync(Guid id, CancellationToken token) =>
        await db.DesktopReleasePreparations.SingleOrDefaultAsync(x => x.PreparationId == id, token) ?? throw Missing();
    private static void RequireRevision(DesktopReleasePreparation entity, Guid? revision)
    { if (revision is null || revision == Guid.Empty || entity.Revision != revision) throw Conflict("desktop_preparation_changed", "Nháp đã đổi. Tải lại để tiếp tục."); }
    private void Touch(DesktopReleasePreparation entity) { entity.Revision = Guid.NewGuid(); entity.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime; }
    private static void ResetQueued(DesktopReleasePreparation entity)
    {
        entity.State = DesktopReleasePreparationStatuses.Queued; entity.Stage = "queued"; entity.CancellationRequested = false;
        entity.LeaseOwner = null; entity.LeaseExpiresAtUtc = null; entity.HeartbeatAtUtc = null; entity.NextAttemptAtUtc = null;
        entity.AttemptCount = 0; entity.ErrorCode = null; entity.ErrorMessage = null; entity.BytesReceived = 0; entity.TotalBytes = null;
        entity.InspectionEvidenceJson = null; entity.VerifiedAtUtc = null;
    }

    private void RequireInspectionMode()
    {
        if (_options.PublicationMode != DesktopReleaseOptions.ServerInspectionMode)
            throw Conflict("desktop_server_inspection_disabled", "Hệ thống dùng catalog trực tiếp. Tạo phiên bản ở mục phát hành trực tiếp; tác vụ kiểm ZIP trên server đã tắt.");
    }
    private async Task SaveAsync(CancellationToken token)
    { try { await db.SaveChangesAsync(token); } catch (DbUpdateException) { throw Conflict("desktop_preparation_changed", "Nháp/release vừa đổi ở lượt khác. Tải lại để tiếp tục."); } }
    private sealed class ProgressSink : IProgress<DesktopReleaseInspectionProgress>
    {
        private DesktopReleaseInspectionProgress? _latest;
        public DesktopReleaseInspectionProgress? Latest => Volatile.Read(ref _latest);
        public void Report(DesktopReleaseInspectionProgress value) => Volatile.Write(ref _latest, value);
    }
}

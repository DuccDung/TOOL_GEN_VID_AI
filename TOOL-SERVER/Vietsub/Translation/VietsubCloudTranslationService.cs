using System.Data;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Vietsub.Data;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_SERVER.Vietsub.Translation;

internal sealed record CloudAccess(string UserId, Guid SessionId, Guid DeviceId)
{
    public static CloudAccess From(ClaimsPrincipal user) => new(
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException(),
        Guid.Parse(user.FindFirstValue(AuthClaimTypes.SessionId) ?? ""),
        Guid.Parse(user.FindFirstValue(AuthClaimTypes.DeviceId) ?? ""));
}

internal sealed partial class VietsubCloudTranslationService(
    VietsubDbContext db, AiGovernanceDbContext governance, ProviderAdminDbContext providers,
    VideoFactoryDbContext videoDb, AccountDbContext accounts, IGenerationAccessService access,
    IProviderRuntimeResolver resolver, IAiBudgetService budget, IOpenAiSubtitleTranslationClient client,
    IDataProtectionProvider protection, IOptions<VietsubCloudTranslationOptions> configuration, TimeProvider time)
{
    private readonly IDataProtector protector = protection.CreateProtector("VideoMaker.VietsubCloudPayload.v1");
    private VietsubCloudTranslationOptions Options => configuration.Value;
    private DateTime Now => time.GetUtcNow().UtcDateTime;
    internal static AccountApiException Error(string code, int status = 409) => new(status, code, Message(code));
    internal static string Message(string? code) => code switch
    {
        "CLOUD_SOURCE_CHANGED" => "Phụ đề đã thay đổi. Hãy tải lại trước khi dịch.",
        "CLOUD_JOB_ACTIVE" => "Dự án đang có một tác vụ Dịch Cloud. Hãy tiếp tục tác vụ hiện tại.",
        "CLOUD_RESULT_EXPIRED" => "Kết quả dịch đã hết thời hạn lưu trên server.",
        "CLOUD_UNKNOWN" => "Dịch Cloud đang cần đối soát. Vui lòng liên hệ quản trị viên; chưa gửi lại yêu cầu.",
        "organization_budget_exceeded" or "member_budget_exceeded" => "Ngân sách AI không đủ để tiếp tục dịch.",
        "license_unavailable" or "CLOUD_ACCESS_REQUIRED" => "Cần đăng nhập và xác minh quyền truy cập để tiếp tục dịch.",
        "CLOUD_RESULT_INVALID" or "CLOUD_RESULT_INCOMPLETE" or "CLOUD_CONTENT_REFUSED" => "Một số câu chưa dịch được. Phần đã hoàn tất được giữ lại.",
        "CLOUD_RATE_LIMITED" => "Dịch Cloud đang quá tải. Bạn có thể thử lại phần chưa hoàn tất sau.",
        "CLOUD_PROVIDER_FAILED" => "Dịch Cloud chưa xử lý được yêu cầu. Vui lòng thử lại sau.",
        "CLOUD_CONTEXT_TOO_LARGE" => "Nội dung một câu hoặc ngữ cảnh quá dài để dịch Cloud.",
        "CLOUD_PAUSED" => "Dịch Cloud đã tạm dừng. Bạn có thể tiếp tục tác vụ này.",
        "CLOUD_RECONCILED" => "Tác vụ đã được đối soát. Bạn có thể thử lại phần chưa hoàn tất.",
        "CLOUD_DISABLED" => "Dịch Cloud chưa được bật cho hệ thống này.",
        "pricing_not_configured" => "Dịch Cloud chưa được cấu hình chi phí. Vui lòng liên hệ quản trị viên.",
        _ => "Dịch Cloud chưa sẵn sàng. Vui lòng liên hệ quản trị viên."
    };

    internal async Task AuthorizeAsync(Guid projectId, Guid organizationId, CloudAccess who, CancellationToken ct)
    {
        await RequireLiveSessionAsync(who, ct);
        await access.RequireAsync(who.UserId, who.DeviceId, organizationId, null, ct);
        if (!await db.Projects.AsNoTracking().AnyAsync(x => x.ProjectId == projectId
            && x.OrganizationId == organizationId && x.CreatedByUserId == who.UserId && !x.IsArchived, ct))
            throw Error("vietsub_project_not_found", 404);
    }

    private async Task RequireLiveSessionAsync(CloudAccess who, CancellationToken ct)
    {
        if (!await accounts.UserSessions.AsNoTracking().AnyAsync(x => x.SessionId == who.SessionId
            && x.UserId == who.UserId && x.DeviceId == who.DeviceId && x.Status == SessionStatuses.Active
            && x.RevokedAtUtc == null && x.AbsoluteExpiresAtUtc > Now && !x.Device!.IsRevoked
            && x.User.AccountStatus == "Active" && x.User.DeletedAtUtc == null, ct))
            throw Error("CLOUD_ACCESS_REQUIRED", 401);
    }

    internal async Task<VietsubCloudAvailability> AvailabilityAsync(Guid projectId, Guid org, CloudAccess who, CancellationToken ct)
    {
        await AuthorizeAsync(projectId, org, who, ct);
        if (!Options.Enabled) return new(false, "CLOUD_DISABLED", Message("CLOUD_DISABLED"));
        try
        {
            _ = await ConfigurationAsync(org, ct);
            if ((await budget.GetSnapshotAsync(org, ct)).RemainingBudget <= 0) throw Error("organization_budget_exceeded");
            return new(true, null, null);
        }
        catch (AccountApiException e) { return new(false, e.Code, Message(e.Code)); }
        catch (System.Data.Common.DbException) { return new(false, "CLOUD_CONFIGURATION_REQUIRED", Message(null)); }
    }

    private async Task<(Guid ModelId, Guid CredentialId, string Rates, string Currency)> ConfigurationAsync(Guid org, CancellationToken ct)
    {
        if (!Options.Enabled || string.IsNullOrWhiteSpace(Options.ModelCode)) throw Error("CLOUD_DISABLED", 503);
        // Query the new table as part of readiness, so an unapplied migration never looks ready.
        _ = await db.CloudTranslationJobs.AsNoTracking().Take(1).Select(x => x.Id).ToArrayAsync(ct);
        var model = await providers.ProviderModels.AsNoTracking().Include(x => x.Provider)
            .SingleOrDefaultAsync(x => x.ModelCode == Options.ModelCode && x.Modality == "Text"
                && x.IsEnabled && x.Provider.IsEnabled && x.Provider.ProviderCode == ProviderCodes.OpenAi, ct);
        if (model is null) throw Error("CLOUD_CONFIGURATION_REQUIRED", 503);
        try
        {
            using var capabilities = JsonDocument.Parse(model.CapabilitiesJson ?? "{}");
            if (!capabilities.RootElement.TryGetProperty("structuredOutput", out var supported) || !supported.GetBoolean())
                throw Error("CLOUD_CONFIGURATION_REQUIRED", 503);
        }
        catch (JsonException) { throw Error("CLOUD_CONFIGURATION_REQUIRED", 503); }
        var credential = await governance.OrganizationProviderCredentials.AsNoTracking()
            .Where(x => x.OrganizationId == org && x.ProviderId == model.ProviderId && x.Status == ProviderCredentialStatuses.Active)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct) ?? throw Error("CLOUD_CONFIGURATION_REQUIRED", 503);
        var rates = await videoDb.CostRates.AsNoTracking().Where(x => x.ProviderModelId == model.ProviderModelId
            && x.IsActive && x.EffectiveFromUtc <= Now && (x.EffectiveToUtc == null || x.EffectiveToUtc > Now))
            .OrderByDescending(x => x.EffectiveFromUtc).ToArrayAsync(ct);
        var selected = new[] { "InputToken", "OutputToken" }.Select(kind => rates.FirstOrDefault(x => x.UsageType == kind)).ToArray();
        if (selected.Any(x => x is null || x.UnitPrice <= 0 || x.Unit is not ("Token" or "1KTokens" or "MillionTokens"))
            || selected.Select(x => x!.CurrencyCode).Distinct().Count() != 1) throw Error("pricing_not_configured", 503);
        var currency = selected[0]!.CurrencyCode;
        var organizationCurrency = await governance.Organizations.Where(x => x.OrganizationId == org).Select(x => x.CurrencyCode).SingleAsync(ct);
        if (currency != organizationCurrency) throw Error("pricing_not_configured", 503);
        return (model.ProviderModelId, credential.OrganizationProviderCredentialId,
            JsonSerializer.Serialize(selected.Select(x => new { x!.CostRateId, x.UsageType, x.Unit, x.UnitPrice, x.CurrencyCode }),
                VietsubCloudSnapshot.JsonOptions), currency);
    }

    internal async Task<VietsubCloudJobResponse> StartAsync(Guid projectId, VietsubCloudStartRequest input, CloudAccess who, CancellationToken ct)
    {
        await AuthorizeAsync(projectId, input.OrganizationId, who, ct);
        VietsubCloudSnapshot.Validate(input);
        var hash = VietsubCloudSnapshot.Hash(input);
        await using var guard = await CloudDatabaseLock.AcquireAsync(db, "VietsubCloudDispatch", ct);
        var existing = await db.CloudTranslationJobs.SingleOrDefaultAsync(x => x.OrganizationId == input.OrganizationId
            && x.ClientOperationId == input.ClientOperationId, ct);
        if (existing is not null)
        {
            if (existing.UserId != who.UserId || existing.ProjectId != projectId || existing.SnapshotHash != hash)
                throw Error("idempotency_key_conflict");
            return await ResponseAsync(existing, ct);
        }
        if (await db.CloudTranslationJobs.AnyAsync(x => x.ProjectId == projectId && x.Active, ct)) throw Error("CLOUD_JOB_ACTIVE");
        var config = await ConfigurationAsync(input.OrganizationId, ct);
        var planned = OpenAiSubtitleTranslationClient.Plan(input);
        var cap = Math.Clamp(Options.MaximumOutputTokens, 1000, 16000);
        var job = new VietsubCloudJob { Id = Guid.NewGuid(), ProjectId = projectId, OrganizationId = input.OrganizationId,
            UserId = who.UserId, SessionId = who.SessionId, DeviceId = who.DeviceId, ClientOperationId = input.ClientOperationId,
            TrackId = input.TrackId, SnapshotHash = hash, ProtectedInput = protector.Protect(VietsubCloudSnapshot.Serialize(input)),
            ModelCode = Options.ModelCode, ProviderModelId = config.ModelId, CredentialId = config.CredentialId,
            RateSnapshotJson = config.Rates, CurrencyCode = config.Currency, MaximumOutputTokens = cap,
            TotalCues = input.Cues.Count(c => c.IsTarget), CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var batches = planned.Select((cues, n) => new VietsubCloudBatch { Id = Guid.NewGuid(), JobId = job.Id,
            Ordinal = n, RequestId = Guid.NewGuid(), TargetCount = cues.Count(c => c.IsTarget), UpdatedAtUtc = Now,
            EstimatedCost = Estimate(job, input, cues) }).ToArray();
        var balance = await budget.GetSnapshotAsync(input.OrganizationId, ct);
        if (balance.RemainingBudget < batches.Sum(x => x.EstimatedCost)) throw Error("organization_budget_exceeded");
        db.CloudTranslationJobs.Add(job); db.CloudTranslationBatches.AddRange(batches);
        Audit(job, who.UserId, "vietsub.cloud.created", new { job.Id, job.TrackId, job.TotalCues, job.SnapshotHash });
        await db.SaveChangesAsync(ct);
        return await ResponseAsync(job, ct);
    }

    internal static decimal Estimate(VietsubCloudJob job, VietsubCloudStartRequest snapshot, IReadOnlyList<VietsubCloudCue> cues)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(OpenAiSubtitleTranslationClient.Body(job.ModelCode, snapshot, cues,
            job.MaximumOutputTokens, job.UserId)).Length;
        if (bytes > 60_000) throw Error("CLOUD_CONTEXT_TOO_LARGE");
        // UTF-8 bytes plus overhead is a conservative bound, rather than a Latin-only chars/token ratio.
        return Math.Max(0.000001m, AiCostEstimator.CalculateOpenAiActual(job.RateSnapshotJson, bytes + 1024, job.MaximumOutputTokens));
    }

    internal async Task<VietsubCloudJobResponse?> FindAsync(Guid projectId, Guid org, Guid operationId, CloudAccess who, CancellationToken ct)
    {
        await AuthorizeAsync(projectId, org, who, ct);
        var job = await db.CloudTranslationJobs.AsNoTracking().Where(x => x.ProjectId == projectId
            && x.OrganizationId == org && x.UserId == who.UserId && (operationId == Guid.Empty ? x.Active : x.ClientOperationId == operationId))
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        return job is null ? null : await ResponseAsync(job, ct);
    }

    private async Task<VietsubCloudJob> OwnedAsync(Guid project, Guid org, Guid id, CloudAccess who, CancellationToken ct)
    {
        await AuthorizeAsync(project, org, who, ct);
        return await db.CloudTranslationJobs.SingleOrDefaultAsync(x => x.Id == id && x.ProjectId == project
            && x.OrganizationId == org && x.UserId == who.UserId, ct) ?? throw Error("cloud_job_not_found", 404);
    }
    internal async Task<VietsubCloudJobResponse> GetAsync(Guid project, Guid org, Guid id, CloudAccess who, CancellationToken ct) =>
        await ResponseAsync(await OwnedAsync(project, org, id, who, ct), ct);

    private async Task<VietsubCloudJobResponse> ResponseAsync(VietsubCloudJob job, CancellationToken ct)
    {
        var batches = await db.CloudTranslationBatches.AsNoTracking().Where(x => x.JobId == job.Id).ToArrayAsync(ct);
        return new(job.Id, job.ClientOperationId, job.TrackId, job.SnapshotHash, job.Status, job.TotalCues,
            batches.Where(x => x.Status == "COMPLETED").Sum(x => x.TargetCount),
            batches.Where(x => x.Status == "FAILED").Sum(x => x.TargetCount), job.ErrorCode,
            job.ErrorCode is null ? null : Message(job.ErrorCode), job.ResultExpiresAtUtc,
            job.Status == VietsubCloudStates.Failed && job.ProtectedInput != null
                && batches.Any(x => x.Status == "FAILED" && x.Attempt < 3 && x.Settled));
    }

    internal async Task<VietsubCloudResultPage> ResultsAsync(Guid project, Guid org, Guid id, int cursor, CloudAccess who, CancellationToken ct)
    {
        var job = await OwnedAsync(project, org, id, who, ct);
        if (job.Acknowledged || job.ResultExpiresAtUtc <= Now) throw Error("CLOUD_RESULT_EXPIRED", 410);
        if (cursor < 0) throw new ArgumentException("Invalid result cursor.");
        var batches = await db.CloudTranslationBatches.AsNoTracking().Where(x => x.JobId == id && x.Ordinal >= cursor)
            .OrderBy(x => x.Ordinal).Take(8).ToArrayAsync(ct);
        var result = new List<VietsubCloudCueResult>(); var next = cursor;
        foreach (var batch in batches)
        {
            if (batch.Status is not ("COMPLETED" or "FAILED")) break;
            if (batch.ProtectedResult is not null)
                result.AddRange(JsonSerializer.Deserialize<VietsubCloudCueResult[]>(protector.Unprotect(batch.ProtectedResult), VietsubCloudSnapshot.JsonOptions)!);
            next = batch.Ordinal + 1;
        }
        return new(id, job.SnapshotHash, next,
            next > cursor && await db.CloudTranslationBatches.AnyAsync(x => x.JobId == id && x.Ordinal == next
                && (x.Status == "COMPLETED" || x.Status == "FAILED"), ct), result);
    }

    internal async Task<VietsubCloudJobResponse> ControlAsync(Guid project, Guid org, Guid id, string command, CloudAccess who, CancellationToken ct)
    {
        await using var guard = await CloudDatabaseLock.AcquireAsync(db, "VietsubCloudDispatch", ct);
        var job = await OwnedAsync(project, org, id, who, ct);
        if (command == "ack")
        {
            if (job.Status != VietsubCloudStates.Completed) throw Error("CLOUD_JOB_NOT_COMPLETED");
            job.Acknowledged = true;
        }
        else if (command == "pause" && job.Active && job.Status != VietsubCloudStates.Unknown) job.Status = VietsubCloudStates.Paused;
        else if (command == "cancel" && job.Status != VietsubCloudStates.Unknown
            && (job.Active || job.Status is VietsubCloudStates.Failed or VietsubCloudStates.Cancelled))
        {
            job.Status = VietsubCloudStates.Cancelled; job.Active = job.LeaseOwner != null; job.FinishedAtUtc = Now;
            job.ResultExpiresAtUtc = Now.AddDays(7);
            if (job.LeaseOwner is null) await ReleaseUnsentAsync(job.Id, ct);
        }
        else if ((command == "resume" && job.Status is VietsubCloudStates.Paused or VietsubCloudStates.Blocked)
            || (command == "retry" && job.Status == VietsubCloudStates.Failed))
        {
            if (!Options.Enabled || job.ProtectedInput is null) throw Error("CLOUD_RESULT_EXPIRED");
            if (job.LeaseOwner != null) throw Error("CLOUD_JOB_ACTIVE");
            if (await db.CloudTranslationJobs.AnyAsync(x => x.ProjectId == project && x.Id != id && x.Active, ct)) throw Error("CLOUD_JOB_ACTIVE");
            if (command == "retry")
            {
                var failed = await db.CloudTranslationBatches.Where(x => x.JobId == id && x.Status == "FAILED").ToArrayAsync(ct);
                if (failed.Length == 0 || failed.Any(x => !x.Settled || x.Attempt >= 3)) throw Error("CLOUD_RETRY_UNAVAILABLE");
                foreach (var batch in failed)
                {
                    batch.RequestId = Guid.NewGuid(); batch.ReservationId = null; batch.Settled = false;
                    batch.Status = "QUEUED"; batch.ErrorCode = null; batch.UsageJson = null; batch.ResponseId = null;
                }
            }
            job.Status = VietsubCloudStates.Queued; job.Active = true; job.ErrorCode = null;
            job.FinishedAtUtc = null; job.ResultExpiresAtUtc = null;
            job.SessionId = who.SessionId; job.DeviceId = who.DeviceId;
        }
        else throw Error(job.Status == VietsubCloudStates.Unknown ? "CLOUD_UNKNOWN" : "CLOUD_INVALID_STATE");
        Audit(job, who.UserId, "vietsub.cloud." + command, new { job.Id, job.Status });
        job.UpdatedAtUtc = Now; await db.SaveChangesAsync(ct);
        return await ResponseAsync(job, ct);
    }

    private void Audit(VietsubCloudJob job, string actor, string action, object data) => db.OrganizationAuditLogs.Add(new()
    {
        OrganizationId = job.OrganizationId, ActorUserId = actor, EventType = action,
        DataJson = JsonSerializer.Serialize(data, VietsubCloudSnapshot.JsonOptions), OccurredAtUtc = Now
    });
}

// Database-scoped lock shared by start/claim/control and credential retirement, including other server instances.
internal static class CloudDatabaseLock
{
    internal static async Task<bool> SchemaReadyAsync(DbContext db, CancellationToken ct) =>
        await db.Database.SqlQueryRaw<int>("SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.1.6') THEN 1 ELSE 0 END AS int) AS [Value]")
            .SingleAsync(ct) == 1;
    public static async Task<IAsyncDisposable> AcquireAsync(DbContext db, string name, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            if (db.Database.IsSqlServer())
            {
                using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource=@name, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=10000; SELECT @r;";
                var parameter = command.CreateParameter(); parameter.ParameterName = "@name"; parameter.Value = name; command.Parameters.Add(parameter);
                if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) < 0) throw new TimeoutException("Cloud coordination lock is busy.");
            }
            return new Releaser(db, name);
        }
        catch { await db.Database.CloseConnectionAsync(); throw; }
    }
    private sealed class Releaser(DbContext db, string name) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (db.Database.IsSqlServer())
                {
                    using var command = db.Database.GetDbConnection().CreateCommand();
                    command.CommandText = "EXEC sys.sp_releaseapplock @Resource=@name, @LockOwner='Session';";
                    var p = command.CreateParameter(); p.ParameterName = "@name"; p.Value = name; command.Parameters.Add(p);
                    await command.ExecuteNonQueryAsync();
                }
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }
}

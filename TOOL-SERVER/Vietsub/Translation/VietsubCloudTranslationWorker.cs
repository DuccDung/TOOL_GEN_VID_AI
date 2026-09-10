using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Vietsub.Data;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_SERVER.Vietsub.Translation;

internal sealed partial class VietsubCloudTranslationService
{
    internal async Task<bool> ProcessOneAsync(CancellationToken stopping)
    {
        var owner = Guid.NewGuid(); Guid jobId;
        await using (var guard = await CloudDatabaseLock.AcquireAsync(db, "VietsubCloudDispatch", stopping))
        {
            var inflight = await db.CloudTranslationJobs.AsNoTracking().Where(x => x.LeaseUntilUtc > Now).ToArrayAsync(stopping);
            if (inflight.Length >= Math.Clamp(Options.MaximumConcurrentJobs, 1, 8)) return false;
            var candidates = await db.CloudTranslationJobs.Where(x => x.Active
                && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= Now)
                && (x.Status == VietsubCloudStates.Queued || x.Status == VietsubCloudStates.Running
                    || x.LeaseOwner != null)).OrderBy(x => x.CreatedAtUtc).Take(50).ToArrayAsync(stopping);
            var job = candidates.FirstOrDefault(x => inflight.Count(j => j.OrganizationId == x.OrganizationId)
                < Math.Clamp(Options.MaximumConcurrentJobsPerOrganization, 1, 4));
            if (job is null) return false;
            jobId = job.Id;
            var abandoned = await db.CloudTranslationBatches.Where(x => x.JobId == job.Id && x.Status == "DISPATCHING").ToArrayAsync(stopping);
            if (abandoned.Length > 0)
            {
                foreach (var batch in abandoned)
                {
                    batch.Status = "UNKNOWN"; batch.ErrorCode = "CLOUD_UNKNOWN";
                    await SnapshotAttemptAsync(batch, stopping);
                }
                job.Status = VietsubCloudStates.Unknown; job.ErrorCode = "CLOUD_UNKNOWN";
                job.LeaseOwner = null; job.LeaseUntilUtc = null;
                await db.SaveChangesAsync(stopping); return true;
            }
            job.LeaseOwner = owner; job.LeaseUntilUtc = Now.AddSeconds(Math.Clamp(Options.RequestTimeoutSeconds, 10, 180) + 90);
            if (job.Status == VietsubCloudStates.Queued) job.Status = VietsubCloudStates.Running;
            await db.SaveChangesAsync(stopping);
        }
        try { await ProcessClaimAsync(jobId, owner, stopping); }
        finally
        {
            await using var guard = await CloudDatabaseLock.AcquireAsync(db, "VietsubCloudDispatch", CancellationToken.None);
            // Persist/reconcile even when the worker is stopping; provider cancellation does not prove no charge.
            db.ChangeTracker.Clear();
            var job = await db.CloudTranslationJobs.SingleAsync(x => x.Id == jobId, CancellationToken.None);
            if (job.LeaseOwner == owner)
            {
                job.LeaseOwner = null; job.LeaseUntilUtc = null; job.UpdatedAtUtc = Now;
                var batches = await db.CloudTranslationBatches.Where(x => x.JobId == jobId).ToArrayAsync(CancellationToken.None);
                if (batches.Any(x => x.Status is "UNKNOWN" or "DISPATCHING"))
                {
                    foreach (var uncertain in batches.Where(x => x.Status == "DISPATCHING"))
                    {
                        uncertain.Status = "UNKNOWN"; uncertain.ErrorCode = "CLOUD_UNKNOWN";
                        await SnapshotAttemptAsync(uncertain, CancellationToken.None);
                    }
                    job.Status = VietsubCloudStates.Unknown; job.ErrorCode = "CLOUD_UNKNOWN"; job.Active = true;
                }
                else if (job.Status == VietsubCloudStates.Cancelled)
                {
                    await ReleaseUnsentAsync(job.Id, CancellationToken.None);
                    job.Active = false;
                }
                else if (batches.All(x => x.Status is "COMPLETED" or "FAILED") && batches.All(x => x.Settled))
                {
                    job.Status = batches.Any(x => x.Status == "FAILED") ? VietsubCloudStates.Failed : VietsubCloudStates.Completed;
                    job.ErrorCode = batches.FirstOrDefault(x => x.ErrorCode != null)?.ErrorCode;
                    job.Active = false; job.FinishedAtUtc = Now; job.ResultExpiresAtUtc = Now.AddDays(7);
                }
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
        return true;
    }

    private async Task ProcessClaimAsync(Guid id, Guid owner, CancellationToken stopping)
    {
        var job = await db.CloudTranslationJobs.SingleAsync(x => x.Id == id, stopping);
        // Complete durable settlement before considering any new outbound call.
        foreach (var finished in await db.CloudTranslationBatches.Where(x => x.JobId == id && !x.Settled
            && (x.Status == "COMPLETED" || x.Status == "FAILED")).ToArrayAsync(stopping))
            await SettleBatchAsync(job, finished);
        if (job.Status != VietsubCloudStates.Running) return;
        var batch = await db.CloudTranslationBatches.Where(x => x.JobId == id && (x.Status == "QUEUED" || x.Status == "RESERVED"))
            .OrderBy(x => x.Ordinal).FirstOrDefaultAsync(stopping);
        if (batch is null) return;
        ProviderRuntimeConfiguration runtime;
        VietsubCloudStartRequest snapshot;
        IReadOnlyList<VietsubCloudCue> cues;
        try
        {
            if (!Options.Enabled || job.PromptVersion != OpenAiSubtitleTranslationClient.PromptVersion) throw Error("CLOUD_DISABLED", 503);
            await AuthorizeAsync(job.ProjectId, job.OrganizationId, new(job.UserId, job.SessionId, job.DeviceId), stopping);
            runtime = await resolver.ResolveModelAsync(job.OrganizationId, ProviderCodes.OpenAi, "Text", job.ModelCode,
                job.CredentialId, requireEnabled: true, stopping);
            if (runtime.ProviderModelId != job.ProviderModelId || runtime.OrganizationProviderCredentialId != job.CredentialId)
                throw Error("CLOUD_CONFIGURATION_REQUIRED");
            try
            {
                snapshot = JsonSerializer.Deserialize<VietsubCloudStartRequest>(protector.Unprotect(job.ProtectedInput
                    ?? throw Error("CLOUD_RESULT_EXPIRED")), VietsubCloudSnapshot.JsonOptions) ?? throw new JsonException();
                VietsubCloudSnapshot.Validate(snapshot);
                if (VietsubCloudSnapshot.Hash(snapshot) != job.SnapshotHash) throw new JsonException();
                cues = OpenAiSubtitleTranslationClient.Plan(snapshot)[batch.Ordinal];
            }
            catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or JsonException or ArgumentException)
            { throw Error("CLOUD_SNAPSHOT_INVALID"); }
            var reservation = await budget.ReserveVietsubAsync(job.OrganizationId, job.UserId, job.ProjectId,
                batch.RequestId, $"vietsub-cloud:{batch.RequestId:N}", ProviderCodes.OpenAi, job.ModelCode, batch.EstimatedCost, stopping);
            batch.ReservationId = reservation.ReservationId; batch.Status = "RESERVED";
            await db.SaveChangesAsync(stopping);
        }
        catch (AccountApiException e)
        {
            await db.Entry(job).ReloadAsync(CancellationToken.None);
            if (job.Status == VietsubCloudStates.Running) { job.Status = VietsubCloudStates.Blocked; job.ErrorCode = e.Code; }
            await db.SaveChangesAsync(CancellationToken.None); return;
        }
        await using (var guard = await CloudDatabaseLock.AcquireAsync(db, "VietsubCloudDispatch", stopping))
        {
            await db.Entry(job).ReloadAsync(stopping);
            if (job.LeaseOwner != owner || job.Status != VietsubCloudStates.Running) return;
            // Once persisted, an abandoned attempt is UNKNOWN and can never be blindly retried by another worker.
            job.LeaseUntilUtc = Now.AddSeconds(Math.Clamp(Options.RequestTimeoutSeconds, 10, 180) + 90);
            batch.Status = "DISPATCHING"; batch.Attempt++; batch.UpdatedAtUtc = Now;
            db.CloudTranslationAttempts.Add(new VietsubCloudAttempt { RequestId = batch.RequestId, JobId = job.Id,
                Ordinal = batch.Ordinal, Attempt = batch.Attempt, CreatedAtUtc = Now, UpdatedAtUtc = Now });
            await db.SaveChangesAsync(stopping);
        }
        SubtitleProviderResult result;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Options.RequestTimeoutSeconds, 10, 180)));
        try { result = await client.TranslateAsync(runtime, snapshot, cues, job.MaximumOutputTokens, job.UserId, timeout.Token); }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            batch.Status = "UNKNOWN"; batch.ErrorCode = "CLOUD_UNKNOWN"; batch.UpdatedAtUtc = Now;
            await SnapshotAttemptAsync(batch, CancellationToken.None);
            await db.SaveChangesAsync(CancellationToken.None); return;
        }
        batch.Status = result.ErrorCode is null ? "COMPLETED" : "FAILED";
        batch.ErrorCode = result.ErrorCode; batch.ResponseId = result.ResponseId; batch.UpdatedAtUtc = Now;
        batch.ActualCost = result.InputTokens is { } it && result.OutputTokens is { } ot
            ? AiCostEstimator.CalculateOpenAiActual(job.RateSnapshotJson, it, ot) : batch.EstimatedCost;
        batch.UsageJson = JsonSerializer.Serialize(new { inputTokens = result.InputTokens, outputTokens = result.OutputTokens,
            estimated = result.InputTokens is null || result.OutputTokens is null, responseId = result.ResponseId,
            batch.Ordinal, batch.Attempt, batch.RequestId }, VietsubCloudSnapshot.JsonOptions);
        if (result.ErrorCode is null)
            batch.ProtectedResult = protector.Protect(JsonSerializer.Serialize(result.Items, VietsubCloudSnapshot.JsonOptions));
        await SnapshotAttemptAsync(batch, CancellationToken.None);
        // The job row can have changed due to pause/cancel. Only the batch is modified here.
        await db.SaveChangesAsync(CancellationToken.None);
        await SettleBatchAsync(job, batch);
    }

    private async Task SettleBatchAsync(VietsubCloudJob job, VietsubCloudBatch batch)
    {
        if (batch.ReservationId is not { } reservation) throw new InvalidOperationException("Missing Cloud reservation.");
        await budget.SettleAsync(reservation, batch.ActualCost, job.CredentialId,
            JsonSerializer.Deserialize<JsonElement>(batch.UsageJson ?? "{}"),
            JsonSerializer.Deserialize<JsonElement>(job.RateSnapshotJson), CancellationToken.None);
        batch.Settled = true;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task SnapshotAttemptAsync(VietsubCloudBatch batch, CancellationToken ct)
    {
        var attempt = await db.CloudTranslationAttempts.SingleAsync(x => x.RequestId == batch.RequestId, ct);
        attempt.Status = batch.Status; attempt.UsageJson = batch.UsageJson; attempt.ResponseId = batch.ResponseId;
        attempt.ErrorCode = batch.ErrorCode; attempt.UpdatedAtUtc = Now;
    }

    internal async Task CleanupAsync(CancellationToken ct)
    {
        await using var guard = await CloudDatabaseLock.AcquireAsync(db, "VietsubCloudDispatch", ct);
        var expired = await db.CloudTranslationJobs.Where(x => x.LeaseOwner == null
            && ((x.ProtectedInput != null && ((x.FinishedAtUtc != null && x.FinishedAtUtc < Now.AddHours(-24))
                    || x.CreatedAtUtc < Now.AddDays(-7) || x.Acknowledged))
                || (db.CloudTranslationBatches.Any(b => b.JobId == x.Id && b.ProtectedResult != null)
                    && (x.Acknowledged || x.ResultExpiresAtUtc <= Now || x.CreatedAtUtc < Now.AddDays(-7)))))
            .OrderBy(x => x.CreatedAtUtc).Take(100).ToArrayAsync(ct);
        foreach (var job in expired)
        {
            job.ProtectedInput = null;
            if (job.CreatedAtUtc < Now.AddDays(-7)) job.ResultExpiresAtUtc ??= Now;
            if (job.ResultExpiresAtUtc <= Now || job.Acknowledged)
            {
                var batches = await db.CloudTranslationBatches.Where(x => x.JobId == job.Id && x.ProtectedResult != null).ToArrayAsync(ct);
                foreach (var batch in batches) batch.ProtectedResult = null;
            }
            if (job.Active && job.Status != VietsubCloudStates.Unknown)
            {
                await ReleaseUnsentAsync(job.Id, ct);
                job.Status = VietsubCloudStates.Cancelled; job.Active = false; job.FinishedAtUtc = Now; job.ResultExpiresAtUtc ??= Now.AddDays(7);
            }
            if (job.ResultExpiresAtUtc <= Now) job.Acknowledged = true;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task ReleaseUnsentAsync(Guid jobId, CancellationToken ct)
    {
        foreach (var batch in await db.CloudTranslationBatches.Where(x => x.JobId == jobId
            && (x.Status == "QUEUED" || x.Status == "RESERVED")).ToArrayAsync(ct))
        {
            // Also recover a reservation persisted immediately before a process crash.
            var reservation = batch.ReservationId ?? await governance.AiBudgetReservations.AsNoTracking()
                .Where(x => x.ProviderRequestId == batch.RequestId).Select(x => (Guid?)x.AiBudgetReservationId).SingleOrDefaultAsync(ct);
            if (reservation is { } id) await budget.ReleaseAsync(id, ct);
            batch.Status = "CANCELLED"; batch.Settled = true;
        }
    }
}

internal sealed class VietsubCloudTranslationWorker(IServiceScopeFactory scopes,
    ILogger<VietsubCloudTranslationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cycle = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var schema = scope.ServiceProvider.GetRequiredService<TOOL_SERVER.Data.VideoFactoryDbContext>();
                if (await CloudDatabaseLock.SchemaReadyAsync(schema, stoppingToken))
                {
                    var service = scope.ServiceProvider.GetRequiredService<VietsubCloudTranslationService>();
                    await service.ProcessOneAsync(stoppingToken);
                    if (++cycle % 30 == 0) await service.CleanupAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogWarning("Cloud translation worker cycle failed ({FailureType}).", e.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}

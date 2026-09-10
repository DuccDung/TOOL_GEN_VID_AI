using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Generation;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_SERVER.Vietsub.Translation;

internal sealed partial class VietsubCloudTranslationService
{
    internal async Task<VietsubCloudReconcileResponse> ReconcileAsync(Guid jobId, VietsubCloudReconcileRequest input,
        CloudAccess admin, CancellationToken ct)
    {
        await RequireLiveSessionAsync(admin, ct);
        if (!await (from membership in accounts.UserRoles join role in accounts.Roles on membership.RoleId equals role.Id
            where membership.UserId == admin.UserId && role.Name == "Admin" select membership).AnyAsync(ct))
            throw Error("forbidden", 403);
        ValidateReconciliation(input);
        await using var guard = await CloudDatabaseLock.AcquireAsync(db, "VietsubCloudDispatch", ct);
        var job = await db.CloudTranslationJobs.SingleOrDefaultAsync(x => x.Id == jobId, ct) ?? throw Error("cloud_job_not_found", 404);
        var batch = await db.CloudTranslationBatches.SingleOrDefaultAsync(x => x.JobId == jobId && x.RequestId == input.RequestId, ct)
            ?? throw Error("cloud_attempt_not_found", 404);
        if (job.LeaseOwner is not null || batch.ReservationId is null) throw Error("CLOUD_JOB_ACTIVE");
        var evidence = JsonSerializer.Serialize(input, VietsubCloudSnapshot.JsonOptions);
        var reconciliationKey = VietsubCloudSnapshot.HashText(evidence);
        if (batch.Status == "FAILED" && batch.ErrorCode == "CLOUD_RECONCILED")
        {
            using var previous = JsonDocument.Parse(batch.UsageJson!);
            if (previous.RootElement.GetProperty("reconciliationKey").GetString() != reconciliationKey)
                throw Error("idempotency_key_conflict");
            if (batch.Settled && job.Status == VietsubCloudStates.Failed && job.ErrorCode == "CLOUD_RECONCILED")
                return new(job.Id, batch.RequestId, job.Status, batch.ActualCost, true);
        }
        else if (job.Status != VietsubCloudStates.Unknown || batch.Status != "UNKNOWN") throw Error("CLOUD_INVALID_STATE");
        batch.ActualCost = input.Decision == "CONFIRMED_NO_CHARGE" ? 0m
            : AiCostEstimator.CalculateOpenAiActual(job.RateSnapshotJson, input.InputTokens!.Value, input.OutputTokens!.Value);
        batch.UsageJson = JsonSerializer.Serialize(new { reconciliationKey, input.Decision, input.EvidenceReference,
            input.InputTokens, input.OutputTokens, input.ResponseId, estimated = false, reconciledBy = admin.UserId,
            batch.RequestId, batch.Ordinal, batch.Attempt }, VietsubCloudSnapshot.JsonOptions);
        batch.Status = "FAILED"; batch.ErrorCode = "CLOUD_RECONCILED"; batch.ResponseId = input.ResponseId; batch.UpdatedAtUtc = Now;
        await SnapshotAttemptAsync(batch, ct);
        // Durable evidence precedes idempotent settlement, so interruption cannot silently discard the charge.
        await db.SaveChangesAsync(ct);
        await SettleBatchAsync(job, batch);
        job.Status = VietsubCloudStates.Failed; job.Active = false; job.ErrorCode = "CLOUD_RECONCILED";
        job.FinishedAtUtc = Now; job.ResultExpiresAtUtc = Now.AddDays(7); job.UpdatedAtUtc = Now;
        Audit(job, admin.UserId, "vietsub.cloud.reconciled", new { job.Id, input.RequestId, input.Decision,
            input.EvidenceReference, batch.ActualCost, reconciliationKey });
        await db.SaveChangesAsync(ct);
        return new(job.Id, batch.RequestId, job.Status, batch.ActualCost, true);
    }

    internal static void ValidateReconciliation(VietsubCloudReconcileRequest input)
    {
        if (input.RequestId == Guid.Empty || input.Decision is not ("CONFIRMED_NO_CHARGE" or "CONFIRMED_USAGE")
            || input.EvidenceReference is null || !Regex.IsMatch(input.EvidenceReference, @"\A[A-Za-z0-9][A-Za-z0-9_.:/-]{2,199}\z")
            || (input.ResponseId != null && !Regex.IsMatch(input.ResponseId, @"\Aresp_[A-Za-z0-9_-]{1,190}\z"))
            || (input.Decision == "CONFIRMED_USAGE" && (input.InputTokens is null or < 0 or > 10_000_000
                || input.OutputTokens is null or < 0 or > 1_000_000))
            || (input.Decision == "CONFIRMED_NO_CHARGE" && (input.InputTokens != null || input.OutputTokens != null)))
            throw new ArgumentException("Chứng từ đối soát Cloud không hợp lệ.");
    }
}

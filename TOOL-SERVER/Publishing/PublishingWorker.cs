using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Publishing;

namespace TOOL_SERVER.Publishing;

internal sealed class PublishingWorker(IServiceScopeFactory scopes, IOptions<PublishingOptions> options,
    ILogger<PublishingWorker> logger, TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        var nextCleanup = DateTimeOffset.MinValue;
        do
        {
            if (!options.Value.Enabled) continue;
            if (time.GetUtcNow() >= nextCleanup)
            {
                try
                {
                    await using var cleanup = scopes.CreateAsyncScope();
                    await cleanup.ServiceProvider.GetRequiredService<PublishingRetention>().SweepAsync(stoppingToken);
                    nextCleanup = time.GetUtcNow().AddHours(1);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogWarning("Publishing cleanup failed ({ErrorType}).", ex.GetType().Name); nextCleanup = time.GetUtcNow().AddMinutes(5); }
            }
            if (!options.Value.WorkerEnabled) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PublishingDispatcher>().TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Publishing iteration failed ({ErrorType}).", ex.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

internal sealed class PublishingDispatcher(PublishingDbContext db, PublishingService service,
    IPublishingProduction production, IPublishingPublisher publisher, IGenerationAccessService access,
    IOptions<PublishingOptions> options, TimeProvider time)
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;
    internal static bool Terminal(string status) => status is "Completed" or "Failed" or "PartialFailure" or "Skipped" or "Cancelled" or "NeedsAttention";

    public async Task TickAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled || !options.Value.WorkerEnabled || options.Value.EmergencyDisabled) return;
        await MaterializeAsync(ct);
        var ids = await db.Runs.AsNoTracking().Where(x => x.NextCheckAtUtc <= Now && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= Now) &&
            x.Status != "Completed" && x.Status != "Failed" && x.Status != "PartialFailure" && x.Status != "Skipped" && x.Status != "Cancelled" && x.Status != "NeedsAttention")
            .OrderBy(x => x.NextCheckAtUtc).Select(x => x.RunId).Take(20).ToListAsync(ct);
        foreach (var id in ids)
        {
            db.ChangeTracker.Clear();
            var lease = Guid.NewGuid(); var now = Now;
            if (db.Database.IsRelational())
            {
                var claimed = await db.Runs.Where(x => x.RunId == id && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now))
                    .ExecuteUpdateAsync(set => set.SetProperty(x => x.LeaseId, lease).SetProperty(x => x.LeaseUntilUtc, now.AddMinutes(8)), ct);
                if (claimed == 0) continue;
            }
            var run = await db.Runs.SingleAsync(x => x.RunId == id, ct);
            if (!db.Database.IsRelational()) { run.LeaseId = lease; run.LeaseUntilUtc = now.AddMinutes(8); await db.SaveChangesAsync(ct); }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                var schedule = await db.Schedules.AsNoTracking().SingleAsync(x => x.ScheduleId == run.ScheduleId, timeout.Token);
                if (schedule.Status is "Paused" or "Cancelled" or "Draft")
                {
                    // In-flight provider work is reconciled by its existing worker. A paused schedule never starts another paid step or post.
                    run.ResumeStatus = run.Status; run.Status = "NeedsAttention"; run.ErrorCode = "publishing_schedule_paused"; run.Message = "Lịch đã tạm dừng hoặc thay đổi. Lượt này giữ nguyên snapshot và chờ tiếp tục.";
                    continue;
                }
                if (Now >= run.DeadlineAtUtc && run.Status is not ("Publishing" or "AwaitingReview" or "ReadyToPublish" or "Generating" or "SubmittingVideo" or "SubmittingImage"))
                { run.Status = "Skipped"; run.Message = "Đã quá hạn đăng của lượt này. Không tạo thêm video hoặc gửi bài muộn."; continue; }
                await service.RequireSessionAsync(run.UserId, run.DeviceId, run.SessionId, timeout.Token);
                await access.RequireAsync(run.UserId, run.DeviceId, run.OrganizationId, run.Status == "Queued" ? null : run.ProjectId, timeout.Token);
                if (run.Status is "ReadyToPublish" or "AwaitingReview" or "Publishing")
                {
                    if (Now >= run.PublishAtUtc) await publisher.StepAsync(run, timeout.Token);
                }
                else await production.StepAsync(run, timeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is AccountApiException or HttpRequestException or IOException or System.Text.Json.JsonException or OperationCanceledException)
            {
                run.ResumeStatus = run.Status; run.Status = "NeedsAttention";
                run.ErrorCode = ex is AccountApiException known ? known.Code : "publishing_interrupted";
                run.Message = ex is AccountApiException safe ? safe.Message : "Lượt chạy bị gián đoạn. Tiếp tục sẽ đối soát thao tác đã có trước khi thực hiện bước mới.";
            }
            finally
            {
                run.LeaseId = null; run.LeaseUntilUtc = null; run.NextCheckAtUtc = Now.AddSeconds(30); run.UpdatedAtUtc = Now;
                if (!ct.IsCancellationRequested) await db.SaveChangesAsync(ct);
            }
        }
    }

    internal async Task MaterializeAsync(CancellationToken ct)
    {
        var ids = await db.Schedules.AsNoTracking().Where(x => x.Status == "Active" && x.NextPublishAtUtc != null)
            .OrderBy(x => x.NextPublishAtUtc).Select(x => x.ScheduleId).Take(100).ToListAsync(ct);
        foreach (var id in ids)
        {
            db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var schedule = await db.Schedules.SingleAsync(x => x.ScheduleId == id, ct);
            if (schedule.Status != "Active" || schedule.ConsentAtUtc is null || schedule.NextPublishAtUtc is not { } publish) continue;
            var input = PublishingService.Read<PublishingScheduleInput>(schedule.InputJson);
            var generateAt = publish.AddMinutes(-input.LeadMinutes);
            if (generateAt > Now) continue;
            if (!await db.Runs.AnyAsync(x => x.ScheduleId == id && x.PublishAtUtc == publish, ct))
            {
                var run = new PublishingRun { RunId = Guid.NewGuid(), ScheduleId = id, OrganizationId = schedule.OrganizationId, UserId = schedule.UserId,
                    DeviceId = schedule.DeviceId, SessionId = schedule.SessionId, ScheduleRevision = schedule.Revision, InputJson = schedule.InputJson,
                    ProductionConsentAtUtc = schedule.ConsentAtUtc.Value,
                    GenerateAtUtc = generateAt, PublishAtUtc = publish, DeadlineAtUtc = PublishingCalendar.Deadline(input, publish),
                    NextCheckAtUtc = Now, CreatedAtUtc = Now, UpdatedAtUtc = Now };
                if (run.DeadlineAtUtc <= Now) { run.Status = "Skipped"; run.Message = "Server đã bỏ lỡ hạn đăng. Không chạy bù lượt cũ và không phát sinh chi phí."; }
                db.Runs.Add(run);
                // Names are resolved when the delivery is actually authorized; the IDs remain pinned to this occurrence.
                foreach (var target in input.Targets)
                    db.Deliveries.Add(new() { DeliveryId = Guid.NewGuid(), RunId = run.RunId, Platform = target.Platform,
                        ConnectionId = target.ConnectionId, AccountName = target.Platform, SettingsJson = PublishingService.Write(target), UpdatedAtUtc = Now });
            }
            schedule.NextPublishAtUtc = PublishingCalendar.Next(input, publish); schedule.UpdatedAtUtc = Now;
            if (schedule.NextPublishAtUtc is null) schedule.Status = "Completed";
            await db.SaveChangesAsync(ct); if (transaction is not null) await transaction.CommitAsync(ct);
        }
    }
}

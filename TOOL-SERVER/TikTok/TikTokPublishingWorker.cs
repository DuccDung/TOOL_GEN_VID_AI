using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.TikTok.Data;
using TOOL_SERVER.TikTok.Domain;

namespace TOOL_SERVER.TikTok;

public sealed class TikTokPublishingWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<TikTokPublishingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PollPendingJobsAsync(stoppingToken);

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task PollPendingJobsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TikTokDbContext>();
            var now = DateTime.UtcNow;
            var dueBefore = now.Subtract(PollInterval);

            var expiredSessions = await db.OAuthSessions
                .Where(x => x.ExpiresAtUtc <= now)
                .OrderBy(x => x.ExpiresAtUtc)
                .Take(500)
                .ToListAsync(cancellationToken);
            if (expiredSessions.Count > 0)
            {
                db.OAuthSessions.RemoveRange(expiredSessions);
                await db.SaveChangesAsync(cancellationToken);
            }

            var pendingJobs = await db.PublishJobs
                .AsNoTracking()
                .Where(x => x.Status != TikTokPublishStatuses.Complete
                            && x.Status != TikTokPublishStatuses.Failed
                            && (x.NextPollAtUtc == null || x.NextPollAtUtc <= now)
                            && x.UpdatedAtUtc <= dueBefore)
                .OrderBy(x => x.UpdatedAtUtc)
                .Select(x => new { x.TikTokPublishJobId, x.UserId })
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            foreach (var job in pendingJobs)
            {
                // Claim before HTTP; a process crash releases the job after this deadline.
                var claimed = await db.PublishJobs.Where(x => x.TikTokPublishJobId == job.TikTokPublishJobId &&
                    x.Status != TikTokPublishStatuses.Complete && x.Status != TikTokPublishStatuses.Failed &&
                    (x.NextPollAtUtc == null || x.NextPollAtUtc <= now))
                    .ExecuteUpdateAsync(updates => updates.SetProperty(x => x.NextPollAtUtc, now.AddMinutes(5)), cancellationToken);
                if (claimed == 0) continue;
                await using var jobScope = scopeFactory.CreateAsyncScope();
                var jobService = jobScope.ServiceProvider.GetRequiredService<ITikTokService>();
                try
                {
                    await jobService.GetPublishStatusAsync(job.UserId, job.TikTokPublishJobId, cancellationToken);
                }
                catch (AccountApiException ex)
                {
                    await FailJobForPermanentAuthorizationErrorAsync(
                        db,
                        job.TikTokPublishJobId,
                        ex.Code,
                        cancellationToken);
                    logger.LogWarning(
                        "TikTok publish reconciliation rejected for job {JobId}: {ErrorCode}.",
                        job.TikTokPublishJobId,
                        ex.Code);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    logger.LogWarning(
                        ex,
                        "TikTok publish reconciliation is temporarily unavailable for job {JobId}.",
                        job.TikTokPublishJobId);
                }
                finally
                {
                    if (!cancellationToken.IsCancellationRequested)
                        await db.PublishJobs.Where(x => x.TikTokPublishJobId == job.TikTokPublishJobId)
                            .ExecuteUpdateAsync(updates => updates.SetProperty(x => x.NextPollAtUtc, DateTime.UtcNow.AddSeconds(30)), cancellationToken);
                    db.ChangeTracker.Clear();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TikTok publishing worker iteration failed.");
        }
    }

    private static async Task FailJobForPermanentAuthorizationErrorAsync(
        TikTokDbContext db,
        Guid publishJobId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        if (errorCode is not ("tiktok_not_connected" or "tiktok_reconnect_required" or "tiktok_publish_scope_missing" or "tiktok_connection_not_found" or "tiktok_app_changed"))
        {
            return;
        }

        var job = await db.PublishJobs.SingleOrDefaultAsync(
            x => x.TikTokPublishJobId == publishJobId,
            cancellationToken);
        if (job is null || TikTokPublishStatuses.IsTerminal(job.Status)) return;
        job.Status = TikTokPublishStatuses.Failed;
        job.FailureReason = errorCode;
        job.ProtectedUploadUrl = null;
        job.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another server instance reconciled the same idempotent status job first.
        }
    }
}

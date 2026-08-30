using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;

namespace TOOL_SERVER.Generation;

internal sealed class GeneratedVoiceCleanupWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<GeneratedVoiceCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), timeProvider);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<VideoFactoryDbContext>();
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var deleted = await dbContext.GeneratedVoiceOutputs
                    .Where(x => x.ExpiresAtUtc <= now)
                    .ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("Deleted {Count} expired generated voice payloads.", deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Generated voice cleanup failed and will retry later.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

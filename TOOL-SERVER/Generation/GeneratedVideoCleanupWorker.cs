namespace TOOL_SERVER.Generation;

internal sealed class GeneratedVideoCleanupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<GeneratedVideoCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var removed = await scope.ServiceProvider
                    .GetRequiredService<IVideoOutputStore>()
                    .CleanupExpiredAsync(stoppingToken);
                if (removed > 0)
                {
                    logger.LogInformation("Cleaned {Count} expired generated video outputs.", removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Generated video output cleanup cycle failed.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }
}

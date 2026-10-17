using Microsoft.Extensions.Options;
using TOOL_SERVER.Configuration;

namespace TOOL_SERVER.Updates;

public sealed class DesktopReleasePreparationWorker(IServiceScopeFactory scopes, IOptions<DesktopReleaseOptions> settings,
    ILogger<DesktopReleasePreparationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var count = Math.Clamp(settings.Value.MaximumInspectionConcurrency, 1, 4);
        await Task.WhenAll(Enumerable.Range(0, count).Select(_ => RunAsync(stoppingToken)));
    }
    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<DesktopReleasePreparationService>().ProcessNextAsync(token)) continue;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                // No raw exception/request/URL or deployment configuration enters logs.
                logger.LogWarning("Desktop release inspection worker paused after {ErrorType}.", error.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(2), token); } catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }
}

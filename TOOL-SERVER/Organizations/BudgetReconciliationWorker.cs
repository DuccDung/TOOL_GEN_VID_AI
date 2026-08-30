using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Organizations;

namespace TOOL_SERVER.Organizations;

internal sealed class BudgetReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<BudgetReconciliationWorker> logger,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "AI budget reconciliation cycle failed.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var governanceDb = scope.ServiceProvider.GetRequiredService<AiGovernanceDbContext>();
        var videoDb = scope.ServiceProvider.GetRequiredService<VideoFactoryDbContext>();
        var budgetService = scope.ServiceProvider.GetRequiredService<IAiBudgetService>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expired = await governanceDb.AiBudgetReservations
            .AsNoTracking()
            .Where(x => x.Status == BudgetReservationStatuses.Reserved && x.ExpiresAtUtc <= now)
            .OrderBy(x => x.ExpiresAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var reservation in expired)
        {
            var request = await videoDb.ProviderRequests.AsNoTracking().SingleOrDefaultAsync(
                x => x.ProviderRequestId == reservation.ProviderRequestId,
                cancellationToken);
            if (request is null || request.Status is "Failed" or "Cancelled")
            {
                await budgetService.ReleaseAsync(reservation.AiBudgetReservationId, cancellationToken);
            }
            else if (request.Status == "Completed")
            {
                await budgetService.SettleAsync(
                    reservation.AiBudgetReservationId,
                    request.ActualCost,
                    request.OrganizationProviderCredentialId,
                    DeserializeJson(request.UsageJson),
                    DeserializeJson(request.RateSnapshotJson),
                    cancellationToken);
            }
            else
            {
                var tracked = await governanceDb.AiBudgetReservations.SingleAsync(
                    x => x.AiBudgetReservationId == reservation.AiBudgetReservationId,
                    cancellationToken);
                tracked.ExpiresAtUtc = now.AddHours(24);
                await governanceDb.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private static object? DeserializeJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(value);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}

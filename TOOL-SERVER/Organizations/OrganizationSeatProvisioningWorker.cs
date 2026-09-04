using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Payments;

namespace TOOL_SERVER.Organizations;

internal sealed class OrganizationSeatProvisioningWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OrganizationSeatProvisioningWorker> logger,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
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
                logger.LogError(exception, "Organization seat provisioning cycle failed.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        Guid[] paidPaymentIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
            paidPaymentIds = await dbContext.LicensePayments
                .AsNoTracking()
                .Where(x => x.Status == LicensePaymentStatuses.Paid)
                .OrderBy(x => x.PaidAtUtc)
                .Select(x => x.LicensePaymentId)
                .Take(50)
                .ToArrayAsync(cancellationToken);
        }

        foreach (var paymentId in paidPaymentIds)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var paymentService = scope.ServiceProvider.GetRequiredService<ILicensePaymentService>();
                var fulfilled = await paymentService.RetryProvisioningAsync(paymentId, cancellationToken);
                if (!fulfilled)
                {
                    logger.LogWarning(
                        "Paid license payment {LicensePaymentId} is still waiting for organization provisioning.",
                        paymentId);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Retrying provisioning for paid license payment {LicensePaymentId} failed.",
                    paymentId);
            }
        }

        await using var reconciliationScope = scopeFactory.CreateAsyncScope();
        var provisioningService = reconciliationScope.ServiceProvider
            .GetRequiredService<IOrganizationSeatProvisioningService>();
        await provisioningService.ReconcileAsync(
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
    }
}

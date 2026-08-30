using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Organizations;

namespace TOOL_SERVER.Organizations;

internal sealed class ProviderCredentialRetirementWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ProviderCredentialRetirementWorker> logger,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RetireUnusedCredentialsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Provider credential retirement cycle failed.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }

    private async Task RetireUnusedCredentialsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var governanceDb = scope.ServiceProvider.GetRequiredService<AiGovernanceDbContext>();
        var videoDb = scope.ServiceProvider.GetRequiredService<VideoFactoryDbContext>();
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddHours(-1);
        var candidates = await governanceDb.OrganizationProviderCredentials
            .Where(x => x.Status == ProviderCredentialStatuses.Retiring &&
                        x.RetiredAtUtc != null &&
                        x.RetiredAtUtc <= cutoff)
            .Take(50)
            .ToListAsync(cancellationToken);
        foreach (var credential in candidates)
        {
            var hasInFlightRequest = await videoDb.ProviderRequests.AsNoTracking().AnyAsync(
                x => x.OrganizationProviderCredentialId == credential.OrganizationProviderCredentialId &&
                     (x.Status == "Created" ||
                      x.Status == "Submitting" ||
                      x.Status == "Submitted" ||
                      x.Status == "Queued" ||
                      x.Status == "Processing" ||
                      x.Status == "Unknown"),
                cancellationToken);
            if (hasInFlightRequest)
            {
                continue;
            }
            credential.Status = ProviderCredentialStatuses.Revoked;
            credential.EncryptedPayload = "revoked";
            credential.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        }
        if (candidates.Count > 0)
        {
            await governanceDb.SaveChangesAsync(cancellationToken);
        }
    }
}

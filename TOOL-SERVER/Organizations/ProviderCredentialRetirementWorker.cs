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
        await using var cloudLock = await TOOL_SERVER.Vietsub.Translation.CloudDatabaseLock.AcquireAsync(governanceDb, "VietsubCloudDispatch", cancellationToken);
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
            var cloudDb = scope.ServiceProvider.GetRequiredService<TOOL_SERVER.Vietsub.Data.VietsubDbContext>();
            // Only query the Cloud schema after migration; old installations remain compatible.
            if (await TOOL_SERVER.Vietsub.Translation.CloudDatabaseLock.SchemaReadyAsync(videoDb, cancellationToken)
                && await cloudDb.CloudTranslationJobs.AsNoTracking().AnyAsync(x => x.CredentialId == credential.OrganizationProviderCredentialId
                    && (x.Active || x.LeaseOwner != null || (x.Status == "FAILED" && x.ProtectedInput != null)), cancellationToken))
                continue;
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

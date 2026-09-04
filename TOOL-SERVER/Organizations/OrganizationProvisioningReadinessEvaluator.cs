using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Domain.Providers;
using TOOL_SERVER.Generation;

namespace TOOL_SERVER.Organizations;

public sealed record OrganizationProvisioningReadiness(bool Ready, string Message);

public interface IOrganizationProvisioningReadinessEvaluator
{
    Task<OrganizationProvisioningReadiness> EvaluateAsync(
        Guid organizationId,
        CancellationToken cancellationToken);
}

internal sealed class OrganizationProvisioningReadinessEvaluator(
    AiGovernanceDbContext governanceDb,
    ProviderAdminDbContext providerDb,
    TimeProvider timeProvider) : IOrganizationProvisioningReadinessEvaluator
{
    public async Task<OrganizationProvisioningReadiness> EvaluateAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var organization = await governanceDb.Organizations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId, cancellationToken);
        if (organization is null)
        {
            return new(false, "Không tìm thấy tổ chức.");
        }

        var reasons = new List<string>();
        if (organization.Status != OrganizationStatuses.Active)
        {
            reasons.Add("tổ chức chưa Active");
        }
        if (organization.MonthlyBudgetLimit <= 0)
        {
            reasons.Add("budget đang bằng 0");
        }

        var credentialIds = (await governanceDb.OrganizationProviderCredentials.AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.Status == ProviderCredentialStatuses.Active)
                .Select(x => x.ProviderId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var policies = await governanceDb.OrganizationVideoPolicies.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.IsActive)
            .ToListAsync(cancellationToken);
        var providers = await providerDb.Providers.AsNoTracking()
            .Include(x => x.Models)
            .ThenInclude(x => x.CostRates)
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var openAi = providers.SingleOrDefault(x => x.ProviderCode == ProviderCodes.OpenAi);
        var openAiTextReady = openAi is { IsEnabled: true } && credentialIds.Contains(openAi.ProviderId) &&
                              openAi.Models.Any(model =>
                                  model.IsEnabled && model.Modality == "Text" &&
                                  HasActiveRate(model, "InputToken", now) &&
                                  HasActiveRate(model, "OutputToken", now));
        if (!openAiTextReady)
        {
            reasons.Add("OpenAI text/credential/rate chưa sẵn sàng");
        }

        var modelsById = providers.SelectMany(x => x.Models.Select(model => (Provider: x, Model: model)))
            .ToDictionary(x => x.Model.ProviderModelId);
        var videoReady = policies.Any(policy =>
        {
            if (!modelsById.TryGetValue(policy.ProviderModelId, out var selected) ||
                !selected.Provider.IsEnabled || !selected.Model.IsEnabled ||
                policy.ProviderId != selected.Provider.ProviderId ||
                !credentialIds.Contains(selected.Provider.ProviderId))
            {
                return false;
            }

            return selected.Provider.ProviderCode switch
            {
                ProviderCodes.Kling =>
                    KlingNativeAudioPolicy.IsRequiredRequestVariant(policy.Resolution, policy.NativeAudio) &&
                    HasActiveRate(selected.Model, "VideoSecond", now, KlingNativeAudioPolicy.MatchesRateMetadata),
                ProviderCodes.Fal =>
                    policy.NativeAudio &&
                    policy.Resolution.Equals(FalVeoPolicy.Resolution, StringComparison.OrdinalIgnoreCase) &&
                    HasActiveRate(
                        selected.Model,
                        "VideoSecond",
                        now,
                        metadata => FalVeoPolicy.MatchesRateMetadata(metadata, selected.Model.ModelCode)),
                ProviderCodes.BytePlus =>
                    policy.NativeAudio &&
                    policy.Resolution.Equals("720p", StringComparison.OrdinalIgnoreCase) &&
                    HasActiveRate(selected.Model, "OutputToken", now),
                _ => false
            };
        });
        if (!videoReady)
        {
            reasons.Add("video policy/credential/rate chưa sẵn sàng");
        }

        return reasons.Count == 0
            ? new(true, "Credential, policy, pricing và budget đã sẵn sàng.")
            : new(false, $"Tổ chức chưa sẵn sàng: {string.Join("; ", reasons)}.");
    }

    private static bool HasActiveRate(AiProviderModel model, string usageType, DateTime now) =>
        model.CostRates.Any(rate => rate.IsActive && rate.UsageType == usageType &&
                                    rate.EffectiveFromUtc <= now &&
                                    (rate.EffectiveToUtc == null || rate.EffectiveToUtc > now));

    private static bool HasActiveRate(
        AiProviderModel model,
        string usageType,
        DateTime now,
        Func<string?, bool> metadataMatches) =>
        model.CostRates.Any(rate => rate.IsActive && rate.UsageType == usageType &&
                                    rate.EffectiveFromUtc <= now &&
                                    (rate.EffectiveToUtc == null || rate.EffectiveToUtc > now) &&
                                    metadataMatches(rate.MetadataJson));
}

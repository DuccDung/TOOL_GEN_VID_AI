using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Domain.Providers;
using TOOL_SERVER.Organizations;

namespace TOOL_TESTS.Organizations;

public sealed class OrganizationProvisioningReadinessTests
{
    [Fact]
    public async Task KlingPolicy_RequiresMatchingNativeAudioRateMetadata()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var governanceDb = new AiGovernanceDbContext(
            new DbContextOptionsBuilder<AiGovernanceDbContext>()
                .UseInMemoryDatabase($"provisioning-readiness-governance-{suffix}")
                .Options);
        await using var providerDb = new ProviderAdminDbContext(
            new DbContextOptionsBuilder<ProviderAdminDbContext>()
                .UseInMemoryDatabase($"provisioning-readiness-provider-{suffix}")
                .Options);
        var now = new DateTime(2026, 9, 4, 6, 0, 0, DateTimeKind.Utc);
        var organizationId = Guid.NewGuid();
        var openAiProviderId = Guid.NewGuid();
        var openAiModelId = Guid.NewGuid();
        var klingProviderId = Guid.NewGuid();
        var klingModelId = Guid.NewGuid();

        governanceDb.Organizations.Add(new Organization
        {
            OrganizationId = organizationId,
            Code = "ready-org",
            Name = "Ready organization",
            Status = OrganizationStatuses.Active,
            MonthlyBudgetLimit = 100m,
            CurrencyCode = "USD",
            CreatedByUserId = "admin",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        governanceDb.OrganizationProviderCredentials.AddRange(
            ActiveCredential(organizationId, openAiProviderId, now),
            ActiveCredential(organizationId, klingProviderId, now));
        governanceDb.OrganizationVideoPolicies.Add(new OrganizationVideoPolicy
        {
            OrganizationId = organizationId,
            PolicyScope = "Default",
            ProviderId = klingProviderId,
            ProviderModelId = klingModelId,
            PolicyVersion = 1,
            Resolution = "720p",
            NativeAudio = true,
            IsActive = true,
            UpdatedByUserId = "admin",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        providerDb.Providers.AddRange(
            Provider(openAiProviderId, "openai", now),
            Provider(klingProviderId, "kling", now));
        providerDb.ProviderModels.AddRange(
            Model(openAiModelId, openAiProviderId, "gpt-test", "Text", now),
            Model(klingModelId, klingProviderId, "kling-test", "Video", now));
        providerDb.CostRates.AddRange(
            Rate(openAiModelId, "InputToken", null, now),
            Rate(openAiModelId, "OutputToken", null, now),
            Rate(klingModelId, "VideoSecond", """{"resolution":"1080p","nativeAudio":false}""", now));
        await governanceDb.SaveChangesAsync();
        await providerDb.SaveChangesAsync();

        var evaluator = new OrganizationProvisioningReadinessEvaluator(
            governanceDb,
            providerDb,
            new FixedTimeProvider(now));
        var mismatch = await evaluator.EvaluateAsync(organizationId, CancellationToken.None);

        Assert.False(mismatch.Ready);
        Assert.Contains("video policy", mismatch.Message);

        var klingRate = await providerDb.CostRates.SingleAsync(x => x.ProviderModelId == klingModelId);
        klingRate.MetadataJson = """{"resolution":"720p","nativeAudio":true}""";
        await providerDb.SaveChangesAsync();

        var matching = await evaluator.EvaluateAsync(organizationId, CancellationToken.None);

        Assert.True(matching.Ready);
    }

    private static OrganizationProviderCredential ActiveCredential(Guid organizationId, Guid providerId, DateTime now) => new()
    {
        OrganizationProviderCredentialId = Guid.NewGuid(),
        OrganizationId = organizationId,
        ProviderId = providerId,
        Version = 1,
        Name = "Active",
        EncryptedPayload = "encrypted",
        SecretHint = "1234",
        Status = ProviderCredentialStatuses.Active,
        CreatedByUserId = "admin",
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    private static AiProvider Provider(Guid providerId, string code, DateTime now) => new()
    {
        ProviderId = providerId,
        ProviderCode = code,
        DisplayName = code,
        BaseUrl = "https://provider.example.test",
        IsEnabled = true,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    private static AiProviderModel Model(
        Guid modelId,
        Guid providerId,
        string code,
        string modality,
        DateTime now) => new()
    {
        ProviderModelId = modelId,
        ProviderId = providerId,
        ModelCode = code,
        DisplayName = code,
        Modality = modality,
        IsEnabled = true,
        IsDefault = true,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    private static AiCostRate Rate(
        Guid modelId,
        string usageType,
        string? metadataJson,
        DateTime now) => new()
    {
        CostRateId = Guid.NewGuid(),
        ProviderModelId = modelId,
        UsageType = usageType,
        Unit = "unit",
        UnitPrice = 0.01m,
        CurrencyCode = "USD",
        EffectiveFromUtc = now.AddDays(-1),
        IsActive = true,
        MetadataJson = metadataJson,
        CreatedAtUtc = now
    };

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}

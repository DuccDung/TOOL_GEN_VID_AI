using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Domain.Providers;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Vietsub.Translation;

namespace TOOL_TESTS.Organizations;

public sealed class VietsubPlanReadinessTests
{
    [Theory]
    [InlineData("ready",true)]
    [InlineData("credential",false)]
    [InlineData("model",false)]
    [InlineData("pricing",false)]
    [InlineData("duplicate-rate",false)]
    [InlineData("zero-budget",false)]
    [InlineData("member-zero",false)]
    [InlineData("disabled",false)]
    [InlineData("exhausted-period",false)]
    [InlineData("capabilities-array",false)]
    [InlineData("capabilities-wrong-type",false)]
    [InlineData("wrong-host",false)]
    [InlineData("provider-disabled",false)]
    public async Task CloudReadinessNeedsOnlyOpenAiAndFailsClosedForEachMissingRequirement(string scenario, bool ready)
    {
        await using var db = new AccountDbContext(new DbContextOptionsBuilder<AccountDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now=DateTime.UtcNow;
        var org=new Organization { OrganizationId=Guid.NewGuid(), Code="test",Name="Test", CreatedByUserId="admin", MonthlyBudgetLimit=scenario=="zero-budget"?0:10 };
        db.Organizations.Add(org);
        var provider=new AiProvider { ProviderId=Guid.NewGuid(),ProviderCode="openai",DisplayName="OpenAI",IsEnabled=true,BaseUrl="https://api.openai.com/v1/" };
        var model=new AiProviderModel { ProviderModelId=Guid.NewGuid(),Provider=provider,ModelCode="test-text",DisplayName="Test text",Modality="Text",
            IsEnabled=scenario!="model",CapabilitiesJson="""{"structuredOutput":true}""" };
        if (scenario == "capabilities-array") model.CapabilitiesJson = "[]";
        if (scenario == "capabilities-wrong-type") model.CapabilitiesJson = """{"structuredOutput":"true"}""";
        if (scenario == "wrong-host") provider.BaseUrl = "https://example.test/";
        if (scenario == "provider-disabled") provider.IsEnabled = false;
        db.ProviderModels.Add(model);
        if(scenario!="credential") db.OrganizationProviderCredentials.Add(new OrganizationProviderCredential {
            OrganizationProviderCredentialId=Guid.NewGuid(),OrganizationId=org.OrganizationId,ProviderId=provider.ProviderId,
            Name="Test",EncryptedPayload="synthetic-not-a-secret",SecretHint="test",CreatedByUserId="admin",Status="Active" });
        foreach(var usage in new[]{"InputToken","OutputToken"})
            if(scenario!="pricing" || usage!="OutputToken") db.CostRates.Add(new AiCostRate { CostRateId=Guid.NewGuid(),ProviderModelId=model.ProviderModelId,
                UsageType=usage,Unit="MillionTokens",UnitPrice=1,CurrencyCode="USD",EffectiveFromUtc=now.AddDays(-1),IsActive=true });
        if(scenario=="duplicate-rate") db.CostRates.Add(new AiCostRate { CostRateId=Guid.NewGuid(),ProviderModelId=model.ProviderModelId,
            UsageType="InputToken",Unit="MillionTokens",UnitPrice=0,CurrencyCode="USD",EffectiveFromUtc=now.AddDays(-1),IsActive=true });
        if(scenario=="exhausted-period") db.OrganizationBudgetPeriods.Add(new() { OrganizationBudgetPeriodId=Guid.NewGuid(), OrganizationId=org.OrganizationId,
            StartsAtUtc=now.AddDays(-1),EndsAtUtc=now.AddDays(1),HardLimit=10,ActualCost=10 });
        await db.SaveChangesAsync();
        var result=await VietsubPlanReadiness.EvaluateAsync(db,org.OrganizationId,true,scenario=="member-zero"?0:null,now,
            new VietsubCloudTranslationOptions { Enabled=scenario!="disabled",ModelCode=model.ModelCode },default);
        Assert.Equal(ready,result.Ready);
        if (scenario is "model" or "provider-disabled") {
            Assert.Equal(model.ProviderModelId, result.ModelId);
            Assert.Equal(scenario == "model" ? "model" : "provider", result.Target);
        }
        Assert.Empty(await db.OrganizationVideoPolicies.ToArrayAsync());
        Assert.DoesNotContain(await db.Providers.ToArrayAsync(),x=>x.ProviderCode=="fal");
    }
}

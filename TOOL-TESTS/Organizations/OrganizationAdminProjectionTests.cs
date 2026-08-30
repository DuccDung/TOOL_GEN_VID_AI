using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_TESTS.Organizations;

public sealed class OrganizationAdminProjectionTests
{
    [Fact]
    public void UsageParser_ProjectsTypedOpenAiMetrics()
    {
        var metrics = OrganizationUsageMetricsParser.Parse(
            """{"inputTokens":1250,"outputTokens":375}""");

        Assert.Equal(1250, metrics.InputTokens);
        Assert.Equal(375, metrics.OutputTokens);
        Assert.Null(metrics.VideoSeconds);
    }

    [Theory]
    [InlineData("durationSeconds")]
    [InlineData("videoSeconds")]
    [InlineData("DurationSeconds")]
    public void UsageParser_ProjectsVideoSeconds(string propertyName)
    {
        var metrics = OrganizationUsageMetricsParser.Parse($$"""{"{{propertyName}}":12.5}""");

        Assert.Equal(12.5m, metrics.VideoSeconds);
        Assert.Null(metrics.InputTokens);
        Assert.Null(metrics.OutputTokens);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"inputTokens\":-1}")]
    public void UsageParser_HidesMissingOrInvalidMetrics(string? value)
    {
        var metrics = OrganizationUsageMetricsParser.Parse(value);

        Assert.Null(metrics.InputTokens);
        Assert.Null(metrics.OutputTokens);
        Assert.Null(metrics.VideoSeconds);
    }

    [Fact]
    public void UsageParser_SumsOnlyMetricsThatExist()
    {
        var metrics = OrganizationUsageMetricsParser.Sum(
        [
            new OrganizationUsageMetrics(10, 4, null),
            new OrganizationUsageMetrics(20, null, 6)
        ]);

        Assert.Equal(30, metrics.InputTokens);
        Assert.Equal(4, metrics.OutputTokens);
        Assert.Equal(6m, metrics.VideoSeconds);
    }

    [Fact]
    public void Readiness_BudgetZeroAlwaysBlocksAi()
    {
        var result = OrganizationReadinessEvaluator.Evaluate(
            "openai",
            "gpt-5.6-luna",
            providerEnabled: true,
            modelEnabled: true,
            credentialActive: true,
            budgetLimit: 0,
            ["InputToken", "OutputToken"]);

        Assert.False(result.Ready);
        Assert.False(result.BudgetEnabled);
        Assert.Contains("budget_disabled", result.BlockingReasons);
    }

    [Fact]
    public void Readiness_ReportsEachMissingRequiredRate()
    {
        var result = OrganizationReadinessEvaluator.Evaluate(
            "openai",
            "gpt-5.6-luna",
            providerEnabled: true,
            modelEnabled: true,
            credentialActive: true,
            budgetLimit: 25,
            ["InputToken"]);

        Assert.False(result.Ready);
        Assert.Equal(["OutputToken"], result.MissingUsageTypes);
        Assert.Contains("pricing_not_configured", result.BlockingReasons);
    }

    [Fact]
    public void Readiness_IsReadyOnlyWhenAllMandatoryConditionsPass()
    {
        var result = OrganizationReadinessEvaluator.Evaluate(
            "kling",
            "kling-3.0",
            providerEnabled: true,
            modelEnabled: true,
            credentialActive: true,
            budgetLimit: 25,
            ["VideoSecond"]);

        Assert.True(result.Ready);
        Assert.Empty(result.MissingUsageTypes);
        Assert.Empty(result.BlockingReasons);
    }

    [Fact]
    public void AuditSanitizer_OnlyReturnsAllowlistedScalarData()
    {
        var data = OrganizationAuditDataSanitizer.Sanitize(
            """{"providerCode":"openai","version":2,"secretHint":"••••1234","apiKey":"secret","encryptedPayload":"cipher","authorization":"Bearer token","nested":{"apiKey":"secret"}}""");

        Assert.Equal("openai", data["providerCode"]);
        Assert.Equal("2", data["version"]);
        Assert.Equal("••••1234", data["secretHint"]);
        Assert.DoesNotContain(data.Keys, key => key.Contains("key", StringComparison.OrdinalIgnoreCase));
        Assert.False(data.ContainsKey("encryptedPayload"));
        Assert.False(data.ContainsKey("authorization"));
        Assert.False(data.ContainsKey("nested"));
    }

    [Theory]
    [InlineData(typeof(OrganizationProviderResponse))]
    [InlineData(typeof(OrganizationAuditItemResponse))]
    public void PublicReadContracts_DoNotExposeCredentialMaterial(Type contractType)
    {
        var propertyNames = contractType.GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain(propertyNames, name => name.Equals("ApiKey", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("EncryptedPayload", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
    }
}

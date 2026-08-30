using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Models;

namespace TOOL_TESTS.Generation;

public sealed class AiCostEstimatorTests
{
    [Fact]
    public void OpenAiActualCost_UsesCapturedMillionTokenRates()
    {
        const string snapshot = """
            [
              { "usageType": "InputToken", "unit": "MillionTokens", "unitPrice": 2.5 },
              { "usageType": "OutputToken", "unit": "MillionTokens", "unitPrice": 10 }
            ]
            """;

        var actual = AiCostEstimator.CalculateOpenAiActual(snapshot, 1_000_000, 250_000);

        Assert.Equal(5.0m, actual);
    }

    [Fact]
    public void OpenAiActualCost_UsesCapturedPerThousandTokenRates()
    {
        const string snapshot = """
            [
              { "usageType": "InputToken", "unit": "1KTokens", "unitPrice": 0.003 },
              { "usageType": "OutputToken", "unit": "1KTokens", "unitPrice": 0.006 }
            ]
            """;

        var actual = AiCostEstimator.CalculateOpenAiActual(snapshot, 2_000, 1_000);

        Assert.Equal(0.012m, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[{\"usageType\":\"InputToken\"}]")]
    public void OpenAiActualCost_ReturnsZeroForInvalidSnapshot(string snapshot)
    {
        Assert.Equal(0m, AiCostEstimator.CalculateOpenAiActual(snapshot, 100, 100));
    }

    [Theory]
    [InlineData("{\"resolution\":\"720p\",\"nativeAudio\":true}", true)]
    [InlineData("{\"Resolution\":\"720P\",\"NativeAudio\":true}", true)]
    [InlineData("{\"resolution\":\"720p\",\"nativeAudio\":false}", false)]
    [InlineData("{\"resolution\":\"1080p\",\"nativeAudio\":true}", false)]
    [InlineData("{\"source\":\"legacy-rate\"}", false)]
    [InlineData("not-json", false)]
    [InlineData(null, false)]
    public void KlingRateMetadata_MustMatch720pNativeAudioVariant(string? metadataJson, bool expected)
    {
        Assert.Equal(expected, KlingNativeAudioPolicy.MatchesRateMetadata(metadataJson));
    }

    [Fact]
    public void KlingSettlement_DoesNotTreatProviderUnitsAsUsd()
    {
        Assert.Equal(1.26m, KlingNativeAudioPolicy.ResolveActualUsd(1.26m, 9m));
    }

    [Fact]
    public async Task KlingQuote_UsesOnlyMatchingNativeAudioRateAndSnapshotsMetadata()
    {
        var modelId = Guid.NewGuid();
        await using var dbContext = CreateContext();
        dbContext.CostRates.AddRange(
            Rate(modelId, 99m, "{\"resolution\":\"720p\",\"nativeAudio\":false}"),
            Rate(modelId, 0.084m, "{\"source\":\"contract\",\"resolution\":\"720p\",\"nativeAudio\":true}"));
        await dbContext.SaveChangesAsync();
        var estimator = new AiCostEstimator(dbContext, TimeProvider.System);

        var quote = await estimator.QuoteKlingAsync(modelId, 15, "720p", true, CancellationToken.None);

        Assert.Equal(1.26m, quote.EstimatedCost);
        using var snapshot = JsonDocument.Parse(quote.RateSnapshotJson);
        Assert.Equal(1, snapshot.RootElement.GetArrayLength());
        Assert.Equal(0.084m, snapshot.RootElement[0].GetProperty("unitPrice").GetDecimal());
        var capturedMetadata = snapshot.RootElement[0].GetProperty("metadataJson").GetString();
        Assert.True(KlingNativeAudioPolicy.MatchesRateMetadata(capturedMetadata));
    }

    [Fact]
    public async Task OpenAiImageQuote_RequiresAndSnapshotsRatesFromImageModel()
    {
        var imageModelId = Guid.NewGuid();
        var textModelId = Guid.NewGuid();
        await using var dbContext = CreateContext();
        dbContext.CostRates.AddRange(
            TokenRate(imageModelId, "InputToken", 2m),
            TokenRate(imageModelId, "OutputToken", 8m),
            TokenRate(textModelId, "InputToken", 999m),
            TokenRate(textModelId, "OutputToken", 999m));
        await dbContext.SaveChangesAsync();
        var estimator = new AiCostEstimator(dbContext, TimeProvider.System);

        var quote = await estimator.QuoteOpenAiImageAsync(
            imageModelId,
            600,
            1_000,
            16_000,
            CancellationToken.None);

        Assert.Equal(0.13m, quote.EstimatedCost);
        Assert.Equal(1_000, quote.EstimatedInputTokens);
        Assert.Equal(16_000, quote.EstimatedOutputTokens);
        using var snapshot = JsonDocument.Parse(quote.RateSnapshotJson);
        var capturedRates = snapshot.RootElement
            .EnumerateArray()
            .ToDictionary(
                rate => rate.GetProperty("usageType").GetString()!,
                rate => rate.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(2, capturedRates.Count);
        Assert.Equal(2m, capturedRates["InputToken"]);
        Assert.Equal(8m, capturedRates["OutputToken"]);
    }

    [Fact]
    public async Task OpenAiImageQuote_ReturnsZeroWhenEitherRequiredRateIsMissing()
    {
        var imageModelId = Guid.NewGuid();
        await using var dbContext = CreateContext();
        dbContext.CostRates.Add(TokenRate(imageModelId, "InputToken", 2m));
        await dbContext.SaveChangesAsync();
        var estimator = new AiCostEstimator(dbContext, TimeProvider.System);

        var quote = await estimator.QuoteOpenAiImageAsync(
            imageModelId,
            600,
            1_000,
            16_000,
            CancellationToken.None);

        Assert.Equal(0m, quote.EstimatedCost);
    }

    [Theory]
    [InlineData("720p", false)]
    [InlineData("1080p", true)]
    public async Task KlingQuote_ReturnsZeroForUnsupportedRequestVariant(string resolution, bool nativeAudio)
    {
        var modelId = Guid.NewGuid();
        await using var dbContext = CreateContext();
        dbContext.CostRates.Add(Rate(modelId, 0.084m, "{\"resolution\":\"720p\",\"nativeAudio\":true}"));
        await dbContext.SaveChangesAsync();
        var estimator = new AiCostEstimator(dbContext, TimeProvider.System);

        var quote = await estimator.QuoteKlingAsync(modelId, 5, resolution, nativeAudio, CancellationToken.None);

        Assert.Equal(0m, quote.EstimatedCost);
    }

    [Fact]
    public async Task BytePlusQuote_UsesSeedanceOutputTokenFormulaAndSnapshotsOnlyTheModelRate()
    {
        var modelId = Guid.NewGuid();
        await using var dbContext = CreateContext();
        dbContext.CostRates.AddRange(
            TokenRate(modelId, "OutputToken", 2m),
            TokenRate(Guid.NewGuid(), "OutputToken", 999m));
        await dbContext.SaveChangesAsync();
        var estimator = new AiCostEstimator(dbContext, TimeProvider.System);

        var quote = await estimator.QuoteVideoAsync(
            ProviderCodes.BytePlus,
            modelId,
            10,
            "720p",
            true,
            24,
            CancellationToken.None);

        Assert.Equal(216_000, quote.EstimatedOutputTokens);
        Assert.Equal(0.432m, quote.EstimatedCost);
        using var snapshot = JsonDocument.Parse(quote.RateSnapshotJson);
        Assert.Single(snapshot.RootElement.EnumerateArray());
        Assert.Equal("OutputToken", snapshot.RootElement[0].GetProperty("usageType").GetString());
    }

    [Fact]
    public async Task BytePlusSettlement_UsesAuthoritativeCompletionTokensFromProvider()
    {
        const string snapshot = """
            [
              { "usageType": "OutputToken", "unit": "MillionTokens", "unitPrice": 2 }
            ]
            """;
        await using var dbContext = CreateContext();
        var estimator = new AiCostEstimator(dbContext, TimeProvider.System);

        var actual = await estimator.CalculateVideoActualAsync(
            ProviderCodes.BytePlus,
            snapshot,
            9.99m,
            null,
            345_600,
            CancellationToken.None);

        Assert.Equal(0.6912m, actual);
    }

    [Theory]
    [InlineData("1080p", true)]
    [InlineData("720p", false)]
    public async Task BytePlusQuote_ReturnsZeroForUnsupportedPolicyVariant(string resolution, bool nativeAudio)
    {
        var modelId = Guid.NewGuid();
        await using var dbContext = CreateContext();
        dbContext.CostRates.Add(TokenRate(modelId, "OutputToken", 2m));
        await dbContext.SaveChangesAsync();
        var estimator = new AiCostEstimator(dbContext, TimeProvider.System);

        var quote = await estimator.QuoteVideoAsync(
            ProviderCodes.BytePlus,
            modelId,
            10,
            resolution,
            nativeAudio,
            24,
            CancellationToken.None);

        Assert.Equal(0m, quote.EstimatedCost);
    }

    private static VideoFactoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"ai-cost-{Guid.NewGuid():N}")
            .Options);

    private static CostRate Rate(Guid modelId, decimal unitPrice, string metadataJson) =>
        new()
        {
            CostRateId = Guid.NewGuid(),
            ProviderModelId = modelId,
            UsageType = "VideoSecond",
            Unit = "Second",
            UnitPrice = unitPrice,
            CurrencyCode = "USD",
            EffectiveFromUtc = DateTime.UtcNow.AddMinutes(-5),
            IsActive = true,
            MetadataJson = metadataJson,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };

    private static CostRate TokenRate(Guid modelId, string usageType, decimal unitPrice) =>
        new()
        {
            CostRateId = Guid.NewGuid(),
            ProviderModelId = modelId,
            UsageType = usageType,
            Unit = "MillionTokens",
            UnitPrice = unitPrice,
            CurrencyCode = "USD",
            EffectiveFromUtc = DateTime.UtcNow.AddMinutes(-5),
            IsActive = true,
            MetadataJson = "{\"source\":\"test\"}",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };
}

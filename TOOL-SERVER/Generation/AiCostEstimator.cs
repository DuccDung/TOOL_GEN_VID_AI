using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Models;

namespace TOOL_SERVER.Generation;

internal sealed record AiCostQuote(
    decimal EstimatedCost,
    string CurrencyCode,
    string RateSnapshotJson,
    long EstimatedInputTokens = 0,
    long EstimatedOutputTokens = 0);

internal interface IAiCostEstimator
{
    Task<AiCostQuote> QuoteOpenAiAsync(Guid providerModelId, int topicCharacters, int targetDurationSeconds, CancellationToken cancellationToken);
    Task<AiCostQuote> QuoteOpenAiImageAsync(Guid providerModelId, int promptCharacters, long estimatedInputTokens, long estimatedOutputTokens, CancellationToken cancellationToken);
    Task<AiCostQuote> QuoteOpenAiVoiceAsync(Guid providerModelId, int narrationCharacters, decimal estimatedCharactersPerSecond, long estimatedOutputTokensPerSecond, CancellationToken cancellationToken);
    Task<AiCostQuote> QuoteKlingAsync(Guid providerModelId, int durationSeconds, string resolution, bool nativeAudio, CancellationToken cancellationToken);
    Task<AiCostQuote> QuoteVideoAsync(string providerCode, Guid providerModelId, int durationSeconds, string resolution, bool nativeAudio, int framesPerSecond, CancellationToken cancellationToken) =>
        providerCode == ProviderCodes.Kling
            ? QuoteKlingAsync(providerModelId, durationSeconds, resolution, nativeAudio, cancellationToken)
            : throw new InvalidOperationException("Provider video chưa được hỗ trợ bởi cost estimator.");
    Task<decimal> CalculateOpenAiActualAsync(string rateSnapshotJson, long inputTokens, long outputTokens, CancellationToken cancellationToken);
    Task<decimal> CalculateVideoActualAsync(string providerCode, string rateSnapshotJson, decimal estimatedCost, decimal? reportedBillingAmount, long? completionTokens, CancellationToken cancellationToken) =>
        Task.FromResult(providerCode == ProviderCodes.Kling
            ? KlingNativeAudioPolicy.ResolveActualUsd(estimatedCost, reportedBillingAmount)
            : estimatedCost);
}

internal sealed class AiCostEstimator(
    VideoFactoryDbContext dbContext,
    TimeProvider timeProvider) : IAiCostEstimator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AiCostQuote> QuoteOpenAiAsync(
        Guid providerModelId,
        int topicCharacters,
        int targetDurationSeconds,
        CancellationToken cancellationToken)
    {
        var rates = await CurrentRatesAsync(providerModelId, cancellationToken);
        var inputRate = rates.FirstOrDefault(x => x.UsageType == "InputToken");
        var outputRate = rates.FirstOrDefault(x => x.UsageType == "OutputToken");
        if (inputRate is null || outputRate is null)
        {
            return EmptyQuote(rates);
        }

        var scenes = Math.Max(1, (int)Math.Ceiling(targetDurationSeconds / 15m));
        var estimatedInput = Math.Max(2_000L, 1_500L + topicCharacters / 3L);
        var estimatedOutput = Math.Min(8_000L, 2_000L + scenes * 300L);
        var cost = TokenCost(inputRate, estimatedInput) + TokenCost(outputRate, estimatedOutput);
        return new AiCostQuote(
            Round(cost),
            inputRate.CurrencyCode,
            Snapshot(rates),
            estimatedInput,
            estimatedOutput);
    }

    public async Task<AiCostQuote> QuoteKlingAsync(
        Guid providerModelId,
        int durationSeconds,
        string resolution,
        bool nativeAudio,
        CancellationToken cancellationToken)
    {
        var rates = await CurrentRatesAsync(providerModelId, cancellationToken);
        var rate = KlingNativeAudioPolicy.IsRequiredRequestVariant(resolution, nativeAudio)
            ? rates.FirstOrDefault(x =>
                x.UsageType == "VideoSecond" &&
                KlingNativeAudioPolicy.MatchesRateMetadata(x.MetadataJson))
            : null;
        if (rate is null)
        {
            return EmptyQuote(rates);
        }
        return new AiCostQuote(
            Round(rate.UnitPrice * durationSeconds),
            rate.CurrencyCode,
            Snapshot([rate]));
    }

    public async Task<AiCostQuote> QuoteVideoAsync(
        string providerCode,
        Guid providerModelId,
        int durationSeconds,
        string resolution,
        bool nativeAudio,
        int framesPerSecond,
        CancellationToken cancellationToken)
    {
        if (providerCode == ProviderCodes.Kling)
        {
            return await QuoteKlingAsync(
                providerModelId,
                durationSeconds,
                resolution,
                nativeAudio,
                cancellationToken);
        }
        if (providerCode != ProviderCodes.BytePlus ||
            !resolution.Equals("720p", StringComparison.OrdinalIgnoreCase) ||
            !nativeAudio)
        {
            return new AiCostQuote(0, "USD", "[]");
        }

        var rates = await CurrentRatesAsync(providerModelId, cancellationToken);
        var outputRate = rates.FirstOrDefault(x => x.UsageType == "OutputToken");
        if (outputRate is null)
        {
            return EmptyQuote(rates);
        }
        const int width = 1280;
        const int height = 720;
        var estimatedOutputTokens = Math.Max(
            1,
            (long)Math.Ceiling(durationSeconds * width * height * Math.Max(1, framesPerSecond) / 1024m));
        return new AiCostQuote(
            Round(TokenCost(outputRate, estimatedOutputTokens)),
            outputRate.CurrencyCode,
            Snapshot([outputRate]),
            EstimatedOutputTokens: estimatedOutputTokens);
    }

    public async Task<AiCostQuote> QuoteOpenAiImageAsync(
        Guid providerModelId,
        int promptCharacters,
        long estimatedInputTokens,
        long estimatedOutputTokens,
        CancellationToken cancellationToken)
    {
        var rates = await CurrentRatesAsync(providerModelId, cancellationToken);
        var inputRate = rates.FirstOrDefault(x => x.UsageType == "InputToken");
        var outputRate = rates.FirstOrDefault(x => x.UsageType == "OutputToken");
        if (inputRate is null || outputRate is null)
        {
            return EmptyQuote(rates);
        }

        var inputTokens = Math.Max(estimatedInputTokens, Math.Max(1, promptCharacters / 3L));
        var outputTokens = Math.Max(1, estimatedOutputTokens);
        return new AiCostQuote(
            Round(TokenCost(inputRate, inputTokens) + TokenCost(outputRate, outputTokens)),
            inputRate.CurrencyCode,
            Snapshot([inputRate, outputRate]),
            inputTokens,
            outputTokens);
    }

    public async Task<AiCostQuote> QuoteOpenAiVoiceAsync(
        Guid providerModelId,
        int narrationCharacters,
        decimal estimatedCharactersPerSecond,
        long estimatedOutputTokensPerSecond,
        CancellationToken cancellationToken)
    {
        var rates = await CurrentRatesAsync(providerModelId, cancellationToken);
        var inputRate = rates.FirstOrDefault(x => x.UsageType == "InputToken");
        var outputRate = rates.FirstOrDefault(x => x.UsageType == "OutputToken");
        if (inputRate is null || outputRate is null)
        {
            return EmptyQuote(rates);
        }

        var inputTokens = Math.Max(1, (long)Math.Ceiling(narrationCharacters / 3m));
        var estimatedSeconds = Math.Max(1m, narrationCharacters / estimatedCharactersPerSecond);
        var outputTokens = Math.Max(1, (long)Math.Ceiling(estimatedSeconds * estimatedOutputTokensPerSecond));
        return new AiCostQuote(
            Round(TokenCost(inputRate, inputTokens) + TokenCost(outputRate, outputTokens)),
            inputRate.CurrencyCode,
            Snapshot([inputRate, outputRate]),
            inputTokens,
            outputTokens);
    }

    public Task<decimal> CalculateOpenAiActualAsync(
        string rateSnapshotJson,
        long inputTokens,
        long outputTokens,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CalculateOpenAiActual(rateSnapshotJson, inputTokens, outputTokens));
    }

    public Task<decimal> CalculateVideoActualAsync(
        string providerCode,
        string rateSnapshotJson,
        decimal estimatedCost,
        decimal? reportedBillingAmount,
        long? completionTokens,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (providerCode == ProviderCodes.Kling)
        {
            return Task.FromResult(KlingNativeAudioPolicy.ResolveActualUsd(estimatedCost, reportedBillingAmount));
        }
        if (providerCode == ProviderCodes.BytePlus && completionTokens is { } tokens)
        {
            return Task.FromResult(CalculateOpenAiActual(rateSnapshotJson, 0, tokens));
        }
        return Task.FromResult(estimatedCost);
    }

    internal static decimal CalculateOpenAiActual(
        string rateSnapshotJson,
        long inputTokens,
        long outputTokens)
    {
        try
        {
            using var document = JsonDocument.Parse(rateSnapshotJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            decimal total = 0;
            foreach (var rate in document.RootElement.EnumerateArray())
            {
                var usageType = rate.GetProperty("usageType").GetString();
                var tokens = usageType == "InputToken"
                    ? inputTokens
                    : usageType == "OutputToken"
                        ? outputTokens
                        : 0;
                var unit = rate.GetProperty("unit").GetString();
                var unitPrice = rate.GetProperty("unitPrice").GetDecimal();
                total += TokenCost(unit, unitPrice, tokens);
            }
            return Round(total);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return 0;
        }
    }

    private async Task<List<CostRate>> CurrentRatesAsync(Guid providerModelId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return await dbContext.CostRates
            .AsNoTracking()
            .Where(x => x.ProviderModelId == providerModelId &&
                        x.IsActive &&
                        x.EffectiveFromUtc <= now &&
                        (x.EffectiveToUtc == null || x.EffectiveToUtc > now))
            .OrderByDescending(x => x.EffectiveFromUtc)
            .ToListAsync(cancellationToken);
    }

    private static decimal TokenCost(CostRate rate, long tokens) =>
        TokenCost(rate.Unit, rate.UnitPrice, tokens);

    private static decimal TokenCost(string? unit, decimal unitPrice, long tokens) =>
        unit switch
        {
            "Token" => unitPrice * tokens,
            "1KTokens" => unitPrice * tokens / 1_000m,
            "MillionTokens" => unitPrice * tokens / 1_000_000m,
            _ => 0
        };

    private static AiCostQuote EmptyQuote(IReadOnlyCollection<CostRate> rates) =>
        new(0, rates.FirstOrDefault()?.CurrencyCode ?? "USD", Snapshot(rates));

    private static string Snapshot(IEnumerable<CostRate> rates) =>
        JsonSerializer.Serialize(
            rates.Select(x => new
            {
                x.CostRateId,
                x.UsageType,
                x.Unit,
                x.UnitPrice,
                x.CurrencyCode,
                x.EffectiveFromUtc,
                x.MetadataJson
            }),
            JsonOptions);

    private static decimal Round(decimal value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);
}

using System.Text.Json;
using TOOL_SERVER.Domain.Providers;
using TOOL_SERVER.Generation;

namespace TOOL_SERVER.Vietsub.Translation;

// The publisher and the dispatcher must accept exactly the same model/rate metadata.
internal static class VietsubCloudConfiguration
{
    internal sealed record Rate(Guid CostRateId, string UsageType, string Unit, decimal UnitPrice, string CurrencyCode);
    internal static Rate Metadata(AiCostRate rate) => new(rate.CostRateId, rate.UsageType, rate.Unit, rate.UnitPrice, rate.CurrencyCode);
    internal static Rate Metadata(TOOL_SERVER.Models.CostRate rate) => new(rate.CostRateId, rate.UsageType, rate.Unit, rate.UnitPrice, rate.CurrencyCode);
    internal static bool ValidModel(AiProviderModel model)
    {
        if (!Uri.TryCreate(model.Provider.BaseUrl, UriKind.Absolute, out var uri) ||
            !ProviderRuntimeResolver.IsAllowedBaseUri("openai", uri)) return false;
        try
        {
            using var doc = JsonDocument.Parse(model.CapabilitiesJson ?? "{}");
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("structuredOutput", out var value) && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }

    internal static Rate[]? SelectRates(IEnumerable<Rate> rates, string currency)
    {
        var selected = new List<Rate>();
        foreach (var usage in new[] { "InputToken", "OutputToken" })
        {
            var matches = rates.Where(x => x.UsageType == usage).ToArray();
            if (matches.Length != 1 || matches[0].UnitPrice <= 0 || matches[0].CurrencyCode != currency ||
                matches[0].Unit is not ("Token" or "1KTokens" or "MillionTokens")) return null;
            selected.Add(matches[0]);
        }
        return selected.ToArray();
    }
}

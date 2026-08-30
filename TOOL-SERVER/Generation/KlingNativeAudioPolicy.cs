using System.Text.Json;

namespace TOOL_SERVER.Generation;

internal static class KlingNativeAudioPolicy
{
    public const string Resolution = "720p";
    public const bool NativeAudio = true;

    public static bool IsRequiredRequestVariant(string? resolution, bool nativeAudio) =>
        nativeAudio && string.Equals(resolution, Resolution, StringComparison.OrdinalIgnoreCase);

    public static bool MatchesRateMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   TryGetProperty(root, "resolution", out var resolution) &&
                   resolution.ValueKind == JsonValueKind.String &&
                   string.Equals(resolution.GetString(), Resolution, StringComparison.OrdinalIgnoreCase) &&
                   TryGetProperty(root, "nativeAudio", out var nativeAudio) &&
                   nativeAudio.ValueKind is JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Kling may report provider credits/units without a currency. Those units are
    // retained in the provider response for reconciliation, but are not USD.
    public static decimal ResolveActualUsd(decimal snapshotCost, decimal? providerBillingAmount) =>
        snapshotCost;

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

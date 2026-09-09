using System.Text.Json;

namespace TOOL_SERVER.Generation;

internal static class FalLipSyncPolicy
{
    public const string EndpointId = "fal-ai/sync-lipsync/v2";
    public const string PolicyVersion = "scene-lipsync-v1";
    public const string InputPolicyVersion = "scene-lipsync-input-v1";
    public const string ModelVariant = "lipsync-2";
    public const string SyncMode = "cut_off";

    public static bool IsApprovedEndpoint(string? modelCode) =>
        string.Equals(modelCode, EndpointId, StringComparison.Ordinal);

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
            var variant = root.TryGetProperty("model", out var model) ? model.GetString() : null;
            var endpoint = root.TryGetProperty("endpointId", out var endpointId) ? endpointId.GetString() : null;
            var syncMode = root.TryGetProperty("syncMode", out var configuredSyncMode) ? configuredSyncMode.GetString() : null;
            return string.Equals(variant, ModelVariant, StringComparison.Ordinal) &&
                   string.Equals(endpoint, EndpointId, StringComparison.Ordinal) &&
                   string.Equals(syncMode, SyncMode, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

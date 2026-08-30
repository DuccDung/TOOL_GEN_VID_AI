using System.Text.Json;
using System.Text.Json.Nodes;

namespace TOOL_SERVER.Accounts;

internal static class LicensePolicy
{
    public const int DefaultHeartbeatIntervalSeconds = 300;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(10);

    public static int GetMaxConcurrentSessions(string? featureFlagsJson)
    {
        if (string.IsNullOrWhiteSpace(featureFlagsJson))
        {
            return 1;
        }

        try
        {
            using var document = JsonDocument.Parse(featureFlagsJson);
            return document.RootElement.TryGetProperty("maxConcurrentSessions", out var value) &&
                   value.TryGetInt32(out var count)
                ? Math.Clamp(count, 1, 100)
                : 1;
        }
        catch (JsonException)
        {
            return 1;
        }
    }

    public static string MergeMaxConcurrentSessions(string? featureFlagsJson, int maxConcurrentSessions)
    {
        if (maxConcurrentSessions is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentSessions));
        }

        JsonObject flags;
        try
        {
            flags = string.IsNullOrWhiteSpace(featureFlagsJson)
                ? new JsonObject()
                : JsonNode.Parse(featureFlagsJson) as JsonObject
                  ?? throw new JsonException("Feature flags must be a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Feature flags phải là JSON object hợp lệ.", nameof(featureFlagsJson), exception);
        }

        flags["maxConcurrentSessions"] = maxConcurrentSessions;
        return flags.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    public static DateTime LeaseExpiry(DateTime now, DateTime? licenseExpiry)
    {
        var leaseExpiry = now.Add(LeaseDuration);
        return licenseExpiry is { } expiry && expiry < leaseExpiry ? expiry : leaseExpiry;
    }
}

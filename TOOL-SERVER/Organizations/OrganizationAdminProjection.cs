using System.Text.Json;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_SERVER.Organizations;

internal readonly record struct OrganizationUsageMetrics(
    long? InputTokens,
    long? OutputTokens,
    decimal? VideoSeconds)
{
    public static OrganizationUsageMetrics Empty => new(null, null, null);
}

internal static class OrganizationUsageMetricsParser
{
    public static OrganizationUsageMetrics Parse(string? usageJson)
    {
        if (string.IsNullOrWhiteSpace(usageJson))
        {
            return OrganizationUsageMetrics.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(usageJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return OrganizationUsageMetrics.Empty;
            }

            return new OrganizationUsageMetrics(
                ReadInt64(document.RootElement, "inputTokens"),
                ReadInt64(document.RootElement, "outputTokens"),
                ReadDecimal(document.RootElement, "videoSeconds") ??
                ReadDecimal(document.RootElement, "durationSeconds"));
        }
        catch (JsonException)
        {
            return OrganizationUsageMetrics.Empty;
        }
    }

    public static OrganizationUsageMetrics Sum(IEnumerable<OrganizationUsageMetrics> values)
    {
        long inputTokens = 0;
        long outputTokens = 0;
        decimal videoSeconds = 0;
        var hasInput = false;
        var hasOutput = false;
        var hasVideo = false;

        foreach (var value in values)
        {
            if (value.InputTokens is { } input)
            {
                inputTokens = checked(inputTokens + input);
                hasInput = true;
            }
            if (value.OutputTokens is { } output)
            {
                outputTokens = checked(outputTokens + output);
                hasOutput = true;
            }
            if (value.VideoSeconds is { } seconds)
            {
                videoSeconds += seconds;
                hasVideo = true;
            }
        }

        return new OrganizationUsageMetrics(
            hasInput ? inputTokens : null,
            hasOutput ? outputTokens : null,
            hasVideo ? videoSeconds : null);
    }

    private static long? ReadInt64(JsonElement root, string name)
    {
        if (!TryGetProperty(root, name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var parsed) ||
            parsed < 0)
        {
            return null;
        }
        return parsed;
    }

    private static decimal? ReadDecimal(JsonElement root, string name)
    {
        if (!TryGetProperty(root, name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDecimal(out var parsed) ||
            parsed < 0)
        {
            return null;
        }
        return parsed;
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}

internal static class OrganizationReadinessEvaluator
{
    public const string LongFormPolicyProviderCode = "video-long-form";

    public static OrganizationAiReadinessResponse Evaluate(
        string providerCode,
        string? modelCode,
        bool providerEnabled,
        bool modelEnabled,
        bool credentialActive,
        decimal budgetLimit,
        IEnumerable<string> activeUsageTypes,
        IEnumerable<string>? additionalBlockingReasons = null)
    {
        var requiredUsageTypes = providerCode.Equals("openai", StringComparison.OrdinalIgnoreCase)
            ? new[] { "InputToken", "OutputToken" }
            : providerCode.Equals("kling", StringComparison.OrdinalIgnoreCase)
                ? new[] { "VideoSecond" }
                : providerCode.Equals("byteplus", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "OutputToken" }
                : providerCode.Equals("fal", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "VideoSecond" }
                : [];
        var configuredUsageTypes = activeUsageTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingUsageTypes = requiredUsageTypes
            .Where(required => !configuredUsageTypes.Contains(required))
            .ToArray();
        var reasons = new List<string>();
        if (budgetLimit <= 0)
        {
            reasons.Add("budget_disabled");
        }
        if (!providerEnabled)
        {
            reasons.Add("provider_disabled");
        }
        if (!modelEnabled || string.IsNullOrWhiteSpace(modelCode))
        {
            reasons.Add("model_disabled");
        }
        if (!credentialActive)
        {
            reasons.Add("credential_missing");
        }
        if (missingUsageTypes.Length > 0)
        {
            reasons.Add("pricing_not_configured");
        }
        if (additionalBlockingReasons is not null)
        {
            reasons.AddRange(additionalBlockingReasons.Where(reason => !reasons.Contains(reason, StringComparer.Ordinal)));
        }

        return new OrganizationAiReadinessResponse(
            providerCode,
            modelCode,
            providerEnabled,
            modelEnabled,
            credentialActive,
            budgetLimit > 0,
            reasons.Count == 0,
            missingUsageTypes,
            reasons);
    }

    public static OrganizationAiReadinessResponse MissingLongFormPolicy(decimal budgetLimit) =>
        new(
            LongFormPolicyProviderCode,
            null,
            true,
            false,
            false,
            budgetLimit > 0,
            false,
            [],
            ["video_policy_missing"]);
}

internal static class OrganizationAuditDataSanitizer
{
    private static readonly HashSet<string> SafePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "code",
        "name",
        "id",
        "email",
        "role",
        "status",
        "memberUserId",
        "monthlyBudgetLimit",
        "currencyCode",
        "providerCode",
        "version",
        "secretHint",
        "modelCode",
        "policyScope",
        "policyVersion",
        "resolution",
        "nativeAudio"
    };

    public static IReadOnlyDictionary<string, string?> Sanitize(string? dataJson)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(dataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!SafePropertyNames.Contains(property.Name))
                {
                    continue;
                }

                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => Limit(property.Value.GetString()),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => null
                };
            }
        }
        catch (JsonException)
        {
            // Audit data is optional display metadata. Invalid historical rows stay hidden.
        }

        return result;
    }

    private static string? Limit(string? value) =>
        value is { Length: > 300 } ? value[..300] : value;
}

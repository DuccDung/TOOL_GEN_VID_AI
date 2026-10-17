using System.Text.Json;

namespace TOOL_SHARED.Contracts.Accounts;

/// <summary>Product entitlements in the existing immutable license snapshot.</summary>
public static class LicenseProductPolicy
{
    public const string AllTools = "all-tools";
    public const string Vietsub = "vietsub";
    public const string ScopeKey = "productScope";
    public const string CloudKey = "vietsubCloudEnabled";

    public static string Scope(string? flags)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(flags) ? "{}" : flags);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return "invalid";
            if (!doc.RootElement.TryGetProperty(ScopeKey, out var scope)) return AllTools;
            return scope.ValueKind == JsonValueKind.String && scope.GetString() is AllTools or Vietsub
                ? scope.GetString()! : "invalid";
        }
        catch (JsonException) { return "invalid"; }
    }

    public static bool VideoAllowed(string? flags) => Scope(flags) == AllTools;
    public static bool VietsubAllowed(string? flags) => Scope(flags) is AllTools or Vietsub;
    public static bool IsLocalOnly(string? flags) => Scope(flags) == Vietsub && !CloudAllowed(flags);
    public static bool RequiresOrganizationAllocation(string? flags) => !IsLocalOnly(flags);
    public static bool CloudAllowed(string? flags)
    {
        var scope = Scope(flags);
        if (scope == AllTools) return true; // Existing plans preserve their behavior.
        if (scope != Vietsub) return false;
        using var doc = JsonDocument.Parse(flags!);
        return doc.RootElement.TryGetProperty(CloudKey, out var cloud) && cloud.ValueKind == JsonValueKind.True;
    }
}

namespace TOOL_SHARED.Distribution.ModelDistribution;

/// <summary>A bounded, safe error for callers to map without exposing metadata or keys.</summary>
public sealed class ModelDistributionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

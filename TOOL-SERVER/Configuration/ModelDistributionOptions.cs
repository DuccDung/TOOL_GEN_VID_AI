using System.ComponentModel.DataAnnotations;

namespace TOOL_SERVER.Configuration;

/// <summary>Read-only model repository. Trust is provisioned by the operator, never by a catalog.</summary>
public sealed class ModelDistributionOptions
{
    public const string SectionName = "ModelDistribution";

    public bool Enabled { get; init; }

    [Required]
    public string StorageRoot { get; init; } = "App_ModelDistribution";

    [Required]
    public string RepositoryId { get; init; } = "videomaker-models";

    public string Platform { get; init; } = "win-x64";

    public string[] AllowedChannels { get; init; } = ["Stable", "Beta", "Development", "Staging"];

    public Dictionary<string, string> PublicKeysPem { get; init; } = new(StringComparer.Ordinal);

    // Hash of the complete signed envelope, updated deliberately when promoting a catalog.
    // This also prevents rollback across process restarts without writing into the public store.
    public Dictionary<string, string> TrustedCatalogSha256ByChannel { get; init; } = new(StringComparer.Ordinal);

    [Range(1, long.MaxValue)]
    public long MinimumSequence { get; init; } = 1;

    [Range(1, 90)]
    public int MaximumCatalogValidityDays { get; init; } = 30;

    [Range(1024, 16 * 1024 * 1024)]
    public int MaximumCatalogBytes { get; init; } = 2 * 1024 * 1024;

    [Range(1, long.MaxValue)]
    public long MaximumArtifactBytes { get; init; } = 16L * 1024 * 1024 * 1024;
}

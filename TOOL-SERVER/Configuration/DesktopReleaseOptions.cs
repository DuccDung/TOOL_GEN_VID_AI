using System.ComponentModel.DataAnnotations;

namespace TOOL_SERVER.Configuration;

public sealed class DesktopReleaseOptions
{
    public const string SectionName = "DesktopReleases";
    public const string ServerInspectionMode = "ServerInspection";
    public const string DirectCatalogMode = "DirectCatalog";
    public const string StandaloneCatalogMode = "StandaloneCatalog";

    [RegularExpression("^(ServerInspection|DirectCatalog|StandaloneCatalog)$")]
    public string PublicationMode { get; init; } = ServerInspectionMode;

    [Required]
    public string StorageRoot { get; init; } = "App_Releases";

    [Range(1, long.MaxValue)]
    public long MaximumArtifactBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    public bool ExternalVpsEnabled { get; init; }

    public string[] AllowedExternalPrefixes { get; init; } = [];

    [Range(1, 2L * 1024 * 1024 * 1024)]
    public long MaximumExternalArtifactBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    public bool SharedBootstrapperEnabled { get; init; }
    public string ApiBaseUrl { get; init; } = string.Empty;
    public string Profile { get; init; } = "Production";
    public string ScratchRoot { get; init; } = "App_Releases_Inspection";
    [Range(30, 3600)] public int InspectionTimeoutSeconds { get; init; } = 600;
    [Range(1, 4)] public int MaximumInspectionConcurrency { get; init; } = 1;
    [Range(30, 600)] public int PreparationLeaseSeconds { get; init; } = 90;
    [Range(1, 5)] public int MaximumPreparationAttempts { get; init; } = 3;
    [Range(1, 300)] public int InspectionRetryDelaySeconds { get; init; } = 10;
}

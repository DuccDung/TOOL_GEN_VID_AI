namespace TOOL_SHARED.Contracts.ModelDistribution;

/// <summary>Native installer progress. Contains no local paths, credentials or model output.</summary>
public sealed record StaticModelSetupProgress(
    string Stage,
    long BytesProcessed,
    long TotalBytes,
    string Message);

/// <summary>Versioned result of the pre-login component bootstrap.</summary>
public sealed record StaticModelBootstrapResult(
    int SchemaVersion,
    string OperationId,
    string BundleId,
    bool Succeeded,
    IReadOnlyList<StaticModelComponentResult> Components,
    string? ErrorCode = null,
    string? Message = null,
    string? ResourceProfileId = null,
    long? RuntimeDiskRequiredBytes = null);

public sealed record StaticModelComponentResult(
    string ComponentId,
    bool Ready,
    string? Fingerprint = null,
    string? ErrorCode = null);

public static class StaticModelSetupStages
{
    public const string Preparing = "PREPARING";
    public const string Downloading = "DOWNLOADING";
    public const string Verifying = "VERIFYING";
    public const string Extracting = "EXTRACTING";
    public const string Installing = "INSTALLING";
    public const string Probing = "PROBING";
    public const string Completed = "COMPLETED";
}

namespace TOOL_SHARED.Contracts.ModelDistribution;

/// <summary>Signed release metadata. Binary model content is always transferred as a stream.</summary>
public sealed record ModelDistributionEnvelope(
    int SchemaVersion,
    string KeyId,
    string Algorithm,
    string PayloadBase64,
    string SignatureBase64);

public sealed record ModelDistributionCatalog(
    int SchemaVersion,
    string RepositoryId,
    string Channel,
    string Platform,
    long Sequence,
    DateTimeOffset PublishedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<ModelDistributionArtifact> Artifacts,
    IReadOnlyList<string>? RevokedArtifactSha256 = null);

public sealed record ModelDistributionArtifact(
    string ArtifactId,
    string ComponentId,
    string ComponentVersion,
    string FileName,
    string RelativePath,
    long SizeBytes,
    string Sha256,
    long MinimumFreeDiskBytes,
    string MinimumDesktopVersion,
    int MinimumDesktopBuildNumber,
    string? RequiredWorkerVersion = null,
    int? RequiredWorkerProtocolVersion = null);

public static class ModelDistributionErrors
{
    public const string Disabled = "model_distribution_disabled";
    public const string TrustNotConfigured = "model_distribution_trust_not_configured";
    public const string CatalogInvalid = "model_distribution_catalog_invalid";
    public const string CatalogExpired = "model_distribution_catalog_expired";
    public const string SignatureInvalid = "model_distribution_signature_invalid";
    public const string CatalogRollback = "model_distribution_catalog_rollback";
    public const string ArtifactNotApproved = "model_distribution_artifact_not_approved";
    public const string ArtifactNotFound = "model_distribution_artifact_not_found";
    public const string ArtifactInvalid = "model_distribution_artifact_invalid";
    public const string Incompatible = "model_distribution_incompatible";
    public const string DownloadFailed = "model_distribution_download_failed";
    public const string InsufficientDisk = "model_distribution_insufficient_disk";
    public const string Busy = "model_distribution_busy";
}

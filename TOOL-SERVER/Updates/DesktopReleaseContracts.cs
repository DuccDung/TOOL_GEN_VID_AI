using TOOL_SERVER.Domain.Updates;

namespace TOOL_SERVER.Updates;

public sealed record AdminDesktopReleaseRequest(
    string Version,
    int BuildNumber,
    string Channel,
    string Platform,
    string? MinimumSupportedDesktopVersion,
    string? ReleaseNotes,
    bool IsMandatory,
    bool IsActive,
    DateTime? PublishedAtUtc,
    Guid? Revision = null);

public sealed record AdminDesktopReleasePublishRequest(Guid Revision);

public sealed record AdminDesktopArtifactResponse(
    Guid ArtifactId,
    string Kind,
    string FileName,
    long SizeBytes,
    string Sha256,
    DateTime CreatedAtUtc,
    string SourceKind = "Internal",
    string? DownloadUrl = null,
    string InstallMode = "Legacy",
    int DistributionProtocol = 1,
    bool MetadataRegisteredOnly = false,
    string? BoundPackageSha256 = null,
    string? ModelBundleId = null);

public sealed record AdminDesktopReleaseResponse(
    Guid ReleaseId,
    string Version,
    int BuildNumber,
    string Channel,
    string Platform,
    string? MinimumSupportedDesktopVersion,
    string? ReleaseNotes,
    bool IsMandatory,
    bool IsActive,
    DateTime PublishedAtUtc,
    IReadOnlyList<AdminDesktopArtifactResponse> Artifacts,
    DateTime? FirstPublishedAtUtc = null,
    Guid Revision = default);

public sealed record DesktopReleasePackage(
    AppRelease Release,
    AppReleaseArtifact Artifact,
    TOOL_SHARED.Contracts.Updates.DesktopSharedBootstrapperResponse? SharedBootstrapper = null,
    string? Profile = null,
    string? MinimumBootstrapperVersion = null,
    TOOL_SHARED.Contracts.Updates.DesktopPackageMetadataDocument? PackageMetadata = null);

public sealed record StoredDesktopArtifact(
    string FileName,
    string RelativePath,
    long SizeBytes,
    string Sha256);

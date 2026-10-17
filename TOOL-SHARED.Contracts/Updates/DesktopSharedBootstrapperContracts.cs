namespace TOOL_SHARED.Contracts.Updates;

public static class DesktopReleasePreparationStatuses
{
    public const string Queued = "Queued";
    public const string Downloading = "Downloading";
    public const string Inspecting = "Inspecting";
    public const string Ready = "Ready";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Published = "Published";
}

public sealed record DesktopReleasePreparationRequest(
    string Version, string DownloadUrl, Guid RequestId,
    string? ReleaseNotes = null, bool IsMandatory = false, Guid? Revision = null);

public sealed record DesktopReleasePreparationActionRequest(Guid Revision);

public sealed record DesktopReleasePreparationResponse(
    Guid PreparationId, string Version, string DownloadUrl, string? ReleaseNotes, bool IsMandatory,
    string Status, Guid Revision, DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    Guid? ReleaseId = null, string? FileName = null, long? SizeBytes = null, string? Sha256 = null,
    int? BuildNumber = null, long BytesReceived = 0, long? TotalBytes = null,
    string? ErrorCode = null, string? ErrorMessage = null, bool CanPublish = false,
    DesktopSharedBootstrapperResponse? Bootstrapper = null, Guid? ReleaseRevision = null,
    bool Published = false, int Generation = 1, bool IsActive = false);

public sealed record DesktopSharedBootstrapperResponse(
    Guid BootstrapperId, string Version, string FileName, string DownloadUrl,
    long SizeBytes, string Sha256, string ApiBaseUrl, string Profile, string Channel,
    string Platform, int DistributionProtocol, string ModelBundleId,
    string SupportedCatalogProtocols = "3");

public sealed record DesktopBootstrapperBindingInfo(
    int SchemaVersion, string Version, string ApiBaseUrl, string Profile, string Channel,
    string Platform, int DistributionProtocol, string ModelBundleId,
    string SupportedCatalogProtocols = "3");

public sealed record DesktopBootstrapperRegistrationRequest(DesktopBootstrapperBindingInfo Binding, bool Activate = false);
public sealed record DesktopBootstrapperActivationRequest(Guid Revision);
public sealed record DesktopBootstrapperAdminResponse(
    DesktopSharedBootstrapperResponse Bootstrapper, bool IsActive, Guid Revision, DateTime CreatedAtUtc, bool IsRevoked = false);

public sealed record DesktopSharedBootstrapperStatusResponse(
    bool SharedBootstrapperEnabled, bool ReleasePreparationEnabled, bool Configured, string Profile,
    string PublicationMode = DesktopPublicationModes.ServerInspection);

/// <summary>Build-produced distribution-profile.json inside an application ZIP. Read without executing the package.</summary>
public sealed record DesktopPackageDistributionProfile(
    int SchemaVersion, int DistributionProtocol, string Profile, string ServerBaseUrl,
    string Channel, string Platform, string ModelBundleId, string MinimumBootstrapperVersion);

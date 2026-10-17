namespace TOOL_SHARED.Contracts.Updates;

public static class DesktopPublicationModes
{
    public const string ServerInspection = "ServerInspection";
    public const string DirectCatalog = "DirectCatalog";
    public const string StandaloneCatalog = "StandaloneCatalog";
}

public static class DesktopDirectCatalogProtocol
{
    public const int Version = 4;
    public const string MinimumBootstrapperVersion = "1.2.0";
}

public static class DesktopDirectReleaseStatuses
{
    public const string Draft = "Draft";
    public const string Published = "Published";
}

public sealed record DesktopDirectReleaseRequest(
    string Version, string DownloadUrl, Guid RequestId,
    string? ReleaseNotes = null, bool IsMandatory = false, Guid? Revision = null);

public sealed record DesktopDirectReleaseActionRequest(Guid Revision);

/// <summary>Administrator-declared catalog identity. No server download or measured archive size is implied.</summary>
public sealed record DesktopDirectReleaseResponse(
    Guid ReleaseId, string Version, int DeclaredBuildNumber, string FileName,
    string DownloadUrl, string ExpectedSha256, string Profile, string Channel,
    string Platform, string ModelBundleId, Guid Revision, DateTime PublishedAtUtc,
    bool IsMandatory, string? ReleaseNotes, DesktopSharedBootstrapperResponse SharedBootstrapper,
    int CatalogProtocol = DesktopDirectCatalogProtocol.Version,
    int ApplicationDistributionProtocol = DesktopDistributionProtocol.Shared,
    string MinimumBootstrapperVersion = DesktopDirectCatalogProtocol.MinimumBootstrapperVersion);

public sealed record DesktopDirectReleaseListResponse(IReadOnlyList<DesktopDirectReleaseResponse> Releases);

public sealed record DesktopDirectUpdateCheckResponse(
    bool IsUpdateAvailable, bool IsMandatory, DesktopDirectReleaseResponse? Release);

public sealed record AdminDesktopDirectReleaseResponse(
    Guid ReleaseId, string Version, int DeclaredBuildNumber, string DownloadUrl,
    string ExpectedSha256, string Profile, string Channel, string Platform,
    string Status, bool IsActive, bool IsMandatory, string? ReleaseNotes, Guid Revision,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, DateTime? FirstPublishedAtUtc,
    DesktopSharedBootstrapperResponse? Bootstrapper = null, bool CanPublish = false,
    string? ErrorCode = null, string? ErrorMessage = null);

// Native IPC only. Ownership fields must never be sent to a WebView or the catalog API.
public sealed record DesktopDirectMaintenanceRequest(
    int SchemaVersion, string OperationId, string Nonce, string Action,
    DesktopDirectReleaseResponse Release, string TargetRoot,
    string CurrentVersion, int CurrentBuildNumber, string CurrentPackageSha256,
    int ParentProcessId, long ParentStartTimeUtcTicks, string UserSid,
    bool LaunchAfterInstall = true, DateTime? CreatedAtUtc = null);

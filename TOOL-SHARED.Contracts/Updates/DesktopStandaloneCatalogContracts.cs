namespace TOOL_SHARED.Contracts.Updates;

public static class DesktopStandaloneCatalogProtocol
{
    public const int Version = 5;
    public const string MinimumSetupVersion = "1.3.0";
}

public sealed record DesktopStandaloneReleaseRequest(
    string Version, string DownloadUrl, Guid RequestId,
    string? ReleaseNotes = null, bool IsMandatory = false, Guid? Revision = null);

public sealed record DesktopStandaloneActionRequest(Guid Revision);

/// <summary>Build-declared executable identity parsed from its URL; no server download or measured size is implied.</summary>
public sealed record DesktopStandaloneSetupResponse(
    string Version, string FileName, string DownloadUrl, string Sha256);

/// <summary>A catalog entry usable by a compatible running Setup without a registered bootstrapper.</summary>
public sealed record DesktopStandaloneReleaseResponse(
    Guid ReleaseId, string Version, int DeclaredBuildNumber, string FileName,
    string DownloadUrl, string ExpectedSha256, string ApiBaseUrl, string Profile, string Channel,
    string Platform, string ModelBundleId, Guid Revision, DateTime PublishedAtUtc,
    bool IsMandatory, string? ReleaseNotes, DesktopStandaloneSetupResponse? Setup = null,
    int CatalogProtocol = DesktopStandaloneCatalogProtocol.Version,
    int ApplicationDistributionProtocol = DesktopDistributionProtocol.Shared,
    string MinimumSetupVersion = DesktopStandaloneCatalogProtocol.MinimumSetupVersion);

public sealed record DesktopStandaloneReleaseListResponse(IReadOnlyList<DesktopStandaloneReleaseResponse> Releases);

public sealed record DesktopStandaloneUpdateCheckResponse(
    bool IsUpdateAvailable, bool IsMandatory, DesktopStandaloneReleaseResponse? Release);

public sealed record AdminDesktopStandaloneReleaseResponse(
    Guid ReleaseId, string Version, int DeclaredBuildNumber, string DownloadUrl,
    string ExpectedSha256, string Profile, string Channel, string Platform,
    string Status, bool IsActive, bool IsMandatory, string? ReleaseNotes, Guid Revision,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, DateTime? FirstPublishedAtUtc,
    bool CanPublish = false, string? ErrorCode = null, string? ErrorMessage = null);

// Native IPC only. Ownership fields must never be sent to a WebView or the catalog API.
public sealed record DesktopStandaloneMaintenanceRequest(
    int SchemaVersion, string OperationId, string Nonce, string Action,
    DesktopStandaloneReleaseResponse Release, string TargetRoot,
    string CurrentVersion, int CurrentBuildNumber, string CurrentPackageSha256,
    int ParentProcessId, long ParentStartTimeUtcTicks, string UserSid,
    bool LaunchAfterInstall = true, DateTime? CreatedAtUtc = null);

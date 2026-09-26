namespace TOOL_SHARED.Contracts.Updates;

public static class DesktopRepairErrorCodes
{
    public const string PackageNotFound = "desktop_repair_package_not_found";
    public const string Unavailable = "desktop_repair_unavailable";
    public const string NetworkFailed = "desktop_repair_network_failed";
    public const string AccessDenied = "desktop_repair_access_denied";
    public const string Failed = "desktop_repair_failed";
}

public sealed record DesktopRepairFailure(string Code, string Message, string Version, int BuildNumber);

public sealed record DesktopUpdateCheckResponse(
    bool IsUpdateAvailable,
    bool IsMandatory,
    DesktopReleaseResponse? Release);

public sealed record DesktopReleaseResponse(
    Guid ReleaseId,
    string ProductName,
    string Version,
    int BuildNumber,
    string Channel,
    string Platform,
    string? MinimumSupportedVersion,
    string? ReleaseNotes,
    DateTime PublishedAtUtc,
    string FileName,
    string DownloadUrl,
    long SizeBytes,
    string Sha256);

public sealed record DesktopReleaseListResponse(
    IReadOnlyList<DesktopReleaseResponse> Releases);

public sealed record DesktopUpdateProgress(
    string Stage,
    int Percent,
    string Message);

public sealed record DesktopUpdateManifest(
    string Product,
    string Version,
    int BuildNumber,
    string Platform,
    IReadOnlyList<string> ManagedFiles);

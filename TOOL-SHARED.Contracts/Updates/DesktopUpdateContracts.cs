namespace TOOL_SHARED.Contracts.Updates;

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

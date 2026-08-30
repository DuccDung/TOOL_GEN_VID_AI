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
    DateTime? PublishedAtUtc);

public sealed record AdminDesktopArtifactResponse(
    Guid ArtifactId,
    string Kind,
    string FileName,
    long SizeBytes,
    string Sha256,
    DateTime CreatedAtUtc);

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
    IReadOnlyList<AdminDesktopArtifactResponse> Artifacts);

public sealed record DesktopReleasePackage(
    AppRelease Release,
    AppReleaseArtifact Artifact);

public sealed record StoredDesktopArtifact(
    string FileName,
    string RelativePath,
    long SizeBytes,
    string Sha256);

namespace TOOL_SHARED.Contracts.Updates;

/// <summary>Small, immutable API artifact reference; inventories never travel over maintenance IPC.</summary>
public sealed record DesktopPackageMetadataArtifactResponse(
    int SchemaVersion, string DownloadUrl, long SizeBytes, string Sha256, string ArchiveSha256);

public sealed record DesktopPackageFileHash(string Path, long SizeBytes, string Sha256);

/// <summary>Server inspection of the original ZIP; file paths are relative to PayloadRoot.</summary>
public sealed record DesktopPackageMetadataDocument(
    int SchemaVersion, string ArchiveSha256, string PayloadRoot,
    DesktopUpdateManifest Manifest, DesktopPackageDistributionProfile DistributionProfile,
    IReadOnlyList<DesktopPackageFileHash> Files, long ExpandedSizeBytes, int EntryCount);

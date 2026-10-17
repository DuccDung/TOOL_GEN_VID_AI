namespace TOOL_SHARED.Contracts.Updates;

public static class DesktopArtifactSourceKinds
{
    public const string Internal = "Internal";
    public const string ExternalVps = "ExternalVps";
}

public static class DesktopInstallModes
{
    public const string Legacy = "Legacy";
    public const string StaticArchive = "StaticArchive";
}

public static class DesktopDistributionProtocol
{
    public const int Legacy = 1;
    public const int Current = 2;
    public const int Shared = 3;
}

public sealed record DesktopSetupFileMetadata(string FileName, long SizeBytes, string Sha256);

// Registration metadata supplied by a trusted release administrator. This is
// neither a signed attestation nor proof of the bytes currently on a remote host.
public sealed record RegisterExternalDesktopArtifactRequest(
    string Version, int BuildNumber, string Channel, string Platform,
    string DownloadUrl, string FileName, long SizeBytes, string Sha256,
    DesktopUpdateManifest PackageManifest, long ExpandedSizeBytes, int EntryCount,
    DesktopSetupFileMetadata Setup,
    string InstallMode = DesktopInstallModes.StaticArchive,
    int DistributionProtocol = DesktopDistributionProtocol.Current,
    string? ModelBundleId = null,
    int SchemaVersion = 1);

public sealed record DesktopSetupArtifactResponse(
    Guid ReleaseId, string Version, int BuildNumber, string Channel, string Platform,
    string PackageSha256, string FileName, string DownloadUrl, long SizeBytes, string Sha256,
    string InstallMode = DesktopInstallModes.StaticArchive,
    int DistributionProtocol = DesktopDistributionProtocol.Current,
    string? ModelBundleId = null);

public sealed record DesktopSetupBindingInfo(
    int SchemaVersion, string Version, int BuildNumber, string PackageSha256,
    string Channel, string Platform, string ServerBaseUrl, string InstallMode,
    int DistributionProtocol, string ModelBundleId);

// Native IPC only. Never send these local ownership/path fields to a WebView,
// metadata server, telemetry or command output.
public sealed record DesktopMaintenanceRequest(
    int SchemaVersion, string OperationId, string Nonce, string Action,
    DesktopReleaseResponse Release, string TargetRoot,
    string CurrentVersion, int CurrentBuildNumber, string CurrentPackageSha256,
    int ParentProcessId, long ParentStartTimeUtcTicks, string UserSid,
    bool LaunchAfterInstall = true, DateTime? CreatedAtUtc = null);

public sealed record DesktopMaintenanceAck(
    int SchemaVersion, string OperationId, string Nonce, Guid ReleaseId,
    string PackageSha256, string TargetRoot, int SetupProcessId,
    long SetupStartTimeUtcTicks, string UserSid, DateTime ExpiresAtUtc);

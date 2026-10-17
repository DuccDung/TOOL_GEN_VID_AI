namespace TOOL_SERVER.Models;

// Keep the scaffolded workflow context compatible with the same auth tables.
public partial class AppRelease
{
    public DateTime? FirstPublishedAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}

public partial class AppReleaseArtifact
{
    public string SourceKind { get; set; } = "Internal";
    public string? ExternalDownloadUrl { get; set; }
    public string InstallMode { get; set; } = "Legacy";
    public int DistributionProtocol { get; set; } = 1;
    public string? RegistrationEvidenceJson { get; set; }
    public string? BoundPackageSha256 { get; set; }
    public string? ModelBundleId { get; set; }
    public Guid? VerifiedPreparationId { get; set; }
}

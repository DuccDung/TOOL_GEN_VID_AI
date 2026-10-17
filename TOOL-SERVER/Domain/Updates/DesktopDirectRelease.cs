namespace TOOL_SERVER.Domain.Updates;

/// <summary>Administrator-approved download identity. The server never claims it downloaded or measured the ZIP.</summary>
public sealed class DesktopDirectRelease
{
    public Guid DirectReleaseId { get; set; } = Guid.NewGuid();
    public string Version { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ExpectedSha256 { get; set; } = string.Empty;
    public int DeclaredBuildNumber { get; set; }
    public string Profile { get; set; } = "Production";
    public string Channel { get; set; } = "Stable";
    public string Platform { get; set; } = "win-x64";
    public int CatalogProtocol { get; set; } = 4;
    public int ApplicationDistributionProtocol { get; set; } = 3;
    public string ModelBundleId { get; set; } = string.Empty;
    public string MinimumBootstrapperVersion { get; set; } = "1.2.0";
    public string? ReleaseNotes { get; set; }
    public bool IsMandatory { get; set; }
    public bool IsActive { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? FirstPublishedAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string ActorUserId { get; set; } = string.Empty;
    public Guid ClientRequestId { get; set; }
    public string CreateRequestFingerprint { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

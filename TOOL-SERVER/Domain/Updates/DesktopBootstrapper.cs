namespace TOOL_SERVER.Domain.Updates;

/// <summary>Immutable Setup bytes with a separately activated system-wide identity.</summary>
public sealed class DesktopBootstrapper
{
    public Guid BootstrapperId { get; set; } = Guid.NewGuid();
    public string Version { get; set; } = string.Empty;
    public string Profile { get; set; } = "Production";
    public string Platform { get; set; } = "win-x64";
    public string Channel { get; set; } = "Stable";
    public string ServerBaseUrl { get; set; } = string.Empty;
    public string ModelBundleId { get; set; } = string.Empty;
    public int DistributionProtocol { get; set; } = 3;
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string EmbeddedMetadataJson { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsRevoked { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
}

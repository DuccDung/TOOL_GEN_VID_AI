namespace TOOL_SERVER.Models;

public partial class AppReleaseArtifact
{
    public Guid AppReleaseArtifactId { get; set; }

    public Guid AppReleaseId { get; set; }

    public string Kind { get; set; } = null!;

    public string FileName { get; set; } = null!;

    public string RelativePath { get; set; } = null!;

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public virtual AppRelease AppRelease { get; set; } = null!;
}

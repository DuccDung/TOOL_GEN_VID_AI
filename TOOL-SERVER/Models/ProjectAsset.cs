namespace TOOL_SERVER.Models;

public sealed class ProjectAsset
{
    public Guid ProjectAssetId { get; set; }

    public Guid ProjectId { get; set; }

    public string AssetType { get; set; } = null!;

    public string AssetKey { get; set; } = string.Empty;

    public string Name { get; set; } = null!;

    public string CanonicalDescription { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string SourceKind { get; set; } = "Manual";

    public int? SourcePlanVersion { get; set; }

    public Guid? GeneratedByProviderRequestId { get; set; }

    public int CurrentVersion { get; set; }

    public DateTime? LockedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string CreatedByUserId { get; set; } = null!;

    public DateTime UpdatedAtUtc { get; set; }

    public string UpdatedByUserId { get; set; } = null!;

    public byte[] RowVersion { get; set; } = null!;

    public ICollection<ProjectAssetVersion> Versions { get; set; } = new List<ProjectAssetVersion>();

    public ICollection<SceneAssetAssignment> SceneAssignments { get; set; } = new List<SceneAssetAssignment>();
}

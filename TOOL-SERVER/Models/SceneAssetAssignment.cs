namespace TOOL_SERVER.Models;

public sealed class SceneAssetAssignment
{
    public Guid SceneId { get; set; }

    public Guid ProjectAssetId { get; set; }

    public string AssignedByUserId { get; set; } = null!;

    public DateTime AssignedAtUtc { get; set; }

    public Scene Scene { get; set; } = null!;

    public ProjectAsset ProjectAsset { get; set; } = null!;
}

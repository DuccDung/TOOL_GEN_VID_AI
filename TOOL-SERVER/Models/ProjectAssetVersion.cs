namespace TOOL_SERVER.Models;

public sealed class ProjectAssetVersion
{
    public Guid ProjectAssetVersionId { get; set; }

    public Guid ProjectAssetId { get; set; }

    public int Version { get; set; }

    public string AssetType { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string CanonicalDescription { get; set; } = null!;

    public DateTime LockedAtUtc { get; set; }

    public string LockedByUserId { get; set; } = null!;

    public ProjectAsset ProjectAsset { get; set; } = null!;

    public ICollection<ProviderRequestAssetVersion> ProviderRequestSnapshots { get; set; } = new List<ProviderRequestAssetVersion>();
}

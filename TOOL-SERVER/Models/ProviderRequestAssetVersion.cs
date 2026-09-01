namespace TOOL_SERVER.Models;

public sealed class ProviderRequestAssetVersion
{
    public Guid ProviderRequestId { get; set; }

    public Guid ProjectAssetVersionId { get; set; }

    public short AppliedOrder { get; set; }

    public ProviderRequest ProviderRequest { get; set; } = null!;

    public ProjectAssetVersion ProjectAssetVersion { get; set; } = null!;
}

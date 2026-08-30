namespace TOOL_SERVER.Models;

public sealed class GeneratedVideoOutput
{
    public Guid ProviderRequestId { get; set; }
    public string StorageKey { get; set; } = null!;
    public string MimeType { get; set; } = "video/mp4";
    public string Sha256 { get; set; } = null!;
    public long SizeBytes { get; set; }
    public string Status { get; set; } = "Ready";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? DownloadedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ProviderRequest ProviderRequest { get; set; } = null!;
}

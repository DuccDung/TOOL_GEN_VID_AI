namespace TOOL_SERVER.Models;

public class GeneratedImageOutput
{
    public Guid ProviderRequestId { get; set; }

    public byte[] Payload { get; set; } = [];

    public string MimeType { get; set; } = null!;

    public string Sha256 { get; set; } = null!;

    public long SizeBytes { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? DownloadedAtUtc { get; set; }

    public virtual ProviderRequest ProviderRequest { get; set; } = null!;
}

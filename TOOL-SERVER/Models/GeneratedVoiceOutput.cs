namespace TOOL_SERVER.Models;

public class GeneratedVoiceOutput
{
    public Guid ProviderRequestId { get; set; }

    public byte[] Payload { get; set; } = [];

    public string MimeType { get; set; } = null!;

    public string Sha256 { get; set; } = null!;

    public long SizeBytes { get; set; }

    public long DurationMs { get; set; }

    public int SampleRate { get; set; }

    public byte Channels { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? DownloadedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual ProviderRequest ProviderRequest { get; set; } = null!;
}

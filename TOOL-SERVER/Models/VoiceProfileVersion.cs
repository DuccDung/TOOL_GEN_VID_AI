namespace TOOL_SERVER.Models;

public partial class VoiceProfileVersion
{
    public Guid VoiceProfileVersionId { get; set; }

    public Guid VoiceProfileId { get; set; }

    public int Version { get; set; }

    public string ProviderCode { get; set; } = null!;

    public string ModelCode { get; set; } = null!;

    public string VoiceCode { get; set; } = null!;

    public string ProviderVoiceCode { get; set; } = null!;

    public string LanguageCode { get; set; } = null!;

    public decimal SpeakingRate { get; set; }

    public string VoiceInstructions { get; set; } = null!;

    public string SnapshotHash { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public DateTime? SupersededAtUtc { get; set; }

    public Guid? PreviewProviderRequestId { get; set; }

    public string? PreviewSha256 { get; set; }

    public long? PreviewDurationMs { get; set; }

    public DateTime? PreviewExpiresAtUtc { get; set; }

    public string? ApprovedByUserId { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual VoiceProfile VoiceProfile { get; set; } = null!;

    public virtual ProviderRequest? PreviewProviderRequest { get; set; }
}

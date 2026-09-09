namespace TOOL_LOCAL.Data.Models;

public sealed class LipSyncGeneration
{
    public Guid LipSyncGenerationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid SceneId { get; set; }
    public Guid LipSyncInputSessionId { get; set; }
    public Guid ProviderRequestId { get; set; }
    public Guid VideoGenerationId { get; set; }
    public Guid VoiceGenerationId { get; set; }
    public int AttemptNumber { get; set; }
    public string Status { get; set; } = null!;
    public string PolicyVersion { get; set; } = null!;
    public long RequestedDurationMs { get; set; }
    public long? ActualDurationMs { get; set; }
    public string VideoSha256 { get; set; } = null!;
    public string AudioSha256 { get; set; } = null!;
    public string PreparedVideoSha256 { get; set; } = null!;
    public string PreparedAudioSha256 { get; set; } = null!;
    public Guid? OutputMediaAssetId { get; set; }
    public string? OutputSha256 { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? ApprovedByUserId { get; set; }
    public string? ReviewReason { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public Project Project { get; set; } = null!;
    public Scene Scene { get; set; } = null!;
    public LipSyncInputSession InputSession { get; set; } = null!;
    public ProviderRequest ProviderRequest { get; set; } = null!;
    public VideoGeneration VideoGeneration { get; set; } = null!;
    public VoiceGeneration VoiceGeneration { get; set; } = null!;
    public MediaAsset? OutputMediaAsset { get; set; }
    public ICollection<Scene> ApprovedScenes { get; set; } = new List<Scene>();
}

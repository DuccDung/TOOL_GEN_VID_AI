namespace TOOL_LOCAL.Data.Models;

public sealed class LipSyncInputSession
{
    public Guid LipSyncInputSessionId { get; set; }
    public Guid OrganizationId { get; set; }
    public string RequestedByUserId { get; set; } = null!;
    public Guid ProjectId { get; set; }
    public Guid SceneId { get; set; }
    public int ScenePlanVersion { get; set; }
    public Guid VideoGenerationId { get; set; }
    public Guid VoiceGenerationId { get; set; }
    public string VideoSha256 { get; set; } = null!;
    public string AudioSha256 { get; set; } = null!;
    public string PreparedVideoSha256 { get; set; } = null!;
    public string PreparedAudioSha256 { get; set; } = null!;
    public long DurationMs { get; set; }
    public string? VideoStorageKey { get; set; }
    public string? AudioStorageKey { get; set; }
    public long? VideoSizeBytes { get; set; }
    public long? AudioSizeBytes { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public Project Project { get; set; } = null!;
    public Scene Scene { get; set; } = null!;
    public VideoGeneration VideoGeneration { get; set; } = null!;
    public VoiceGeneration VoiceGeneration { get; set; } = null!;
    public ICollection<LipSyncGeneration> LipSyncGenerations { get; set; } = new List<LipSyncGeneration>();
}

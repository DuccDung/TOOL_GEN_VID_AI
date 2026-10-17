namespace TOOL_SERVER.Domain.Updates;

/// <summary>A durable form draft and inspection operation. No fictitious build is written to AppReleases.</summary>
public sealed class DesktopReleasePreparation
{
    public Guid PreparationId { get; set; } = Guid.NewGuid();
    public string ActorUserId { get; set; } = string.Empty;
    public Guid ClientRequestId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string? ReleaseNotes { get; set; }
    public bool IsMandatory { get; set; }
    public string Profile { get; set; } = "Production";
    public string Channel { get; set; } = "Stable";
    public string Platform { get; set; } = "win-x64";
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string InputFingerprint { get; set; } = string.Empty;
    public string CreateRequestFingerprint { get; set; } = string.Empty;
    public int Generation { get; set; } = 1;
    public string State { get; set; } = "Queued";
    public string Stage { get; set; } = "queued";
    public long BytesReceived { get; set; }
    public long? TotalBytes { get; set; }
    public int AttemptCount { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? HeartbeatAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public bool CancellationRequested { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? InspectionEvidenceJson { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public Guid? ReleaseId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

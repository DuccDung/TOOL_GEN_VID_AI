using System.Text.Json.Serialization;

namespace TOOL_LOCAL.Vietsub.Domain;

internal static class VietsubProjectStatuses
{
    public const string Draft = "DRAFT";
    public const string Ready = "READY";
    public const string Processing = "PROCESSING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
}

internal sealed class VietsubProjectManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Guid ProjectId { get; set; }

    public Guid OrganizationId { get; set; }

    public string OwnerUserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = VietsubProjectStatuses.Draft;

    public string SourceLanguageCode { get; set; } = "auto";

    public string TargetLanguageCode { get; set; } = "vi";

    public Guid? ActiveSubtitleTrackId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? LastOpenedAtUtc { get; set; }

    public bool LastCleanShutdown { get; set; } = true;

    public bool ServerSynchronized { get; set; }

    public string? ServerSyncErrorCode { get; set; }

    public VietsubMediaReference? SourceVideo { get; set; }

    [JsonIgnore]
    public bool RecoveryRequired { get; set; }
}

internal sealed record VietsubProjectSummary(
    Guid ProjectId,
    string Name,
    string Status,
    string SourceLanguageCode,
    string TargetLanguageCode,
    DateTime UpdatedAtUtc,
    bool NeedsRecovery,
    bool ServerSynchronized,
    string? ServerSyncErrorCode,
    VietsubMediaSummary? SourceVideo = null);

internal static class VietsubMediaImportModes
{
    public const string Copy = "COPY";
    public const string Link = "LINK";
}

internal sealed class VietsubMediaReference
{
    public Guid MediaId { get; set; } = Guid.NewGuid();

    public string ImportMode { get; set; } = VietsubMediaImportModes.Link;

    public string OriginalPath { get; set; } = string.Empty;

    public string? WorkspaceRelativePath { get; set; }

    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public DateTime SourceLastWriteAtUtc { get; set; }

    public VietsubMediaMetadata Metadata { get; set; } = new();
}

internal sealed class VietsubMediaMetadata
{
    public decimal DurationSeconds { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public decimal? FramesPerSecond { get; set; }

    public string? VideoCodec { get; set; }

    public string? AudioCodec { get; set; }

    public int? AudioSampleRate { get; set; }

    public bool HasVideo { get; set; }

    public bool HasAudio { get; set; }
}

internal sealed record VietsubMediaSummary(
    Guid MediaId,
    string FileName,
    string ImportMode,
    long SizeBytes,
    string Sha256,
    decimal DurationSeconds,
    int Width,
    int Height,
    decimal? FramesPerSecond,
    string? VideoCodec,
    string? AudioCodec,
    bool HasAudio,
    bool SourceAvailable,
    bool SourceChanged,
    string? SourceIssueCode,
    string PlaybackUrl,
    IReadOnlyList<string> ThumbnailUrls);

internal sealed class VietsubSubtitleTrack
{
    public Guid TrackId { get; set; } = Guid.NewGuid();

    public string DisplayName { get; set; } = string.Empty;

    public string LanguageCode { get; set; } = "vi";

    public string Source { get; set; } = "MANUAL";

    public int Revision { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<VietsubSubtitleCue> Cues { get; set; } = [];

    public List<VietsubSubtitleArtifact> Artifacts { get; set; } = [];
}

internal sealed class VietsubSubtitleCue
{
    public Guid CueId { get; set; } = Guid.NewGuid();

    public long StartMilliseconds { get; set; }

    public long EndMilliseconds { get; set; }

    public string Speaker { get; set; } = "speaker_1";

    public string OriginalText { get; set; } = string.Empty;

    public string TranslatedText { get; set; } = string.Empty;

    public bool OriginalLocked { get; set; }

    public bool TranslationLocked { get; set; }

    public string? QualityStatus { get; set; }

    public List<string> Warnings { get; set; } = [];

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

internal static class VietsubSubtitleArtifactStatuses
{
    public const string Ready = "READY";
    public const string Stale = "STALE";
}

internal sealed class VietsubSubtitleArtifact
{
    public Guid ArtifactId { get; set; } = Guid.NewGuid();

    public string ArtifactType { get; set; } = "SRT_ORIGINAL";

    public int TrackRevision { get; set; } = 1;

    public string WorkspaceRelativePath { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public string Status { get; set; } = VietsubSubtitleArtifactStatuses.Ready;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class MediaAsset
{
    public Guid MediaAssetId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? SceneId { get; set; }

    public string AssetType { get; set; } = null!;

    public string? DisplayName { get; set; }

    public string RelativePath { get; set; } = null!;

    public string MimeType { get; set; } = null!;

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = null!;

    public int? Width { get; set; }

    public int? Height { get; set; }

    public decimal? FrameRate { get; set; }

    public long? DurationMs { get; set; }

    public int? AudioSampleRate { get; set; }

    public string Status { get; set; } = null!;

    public string SourceType { get; set; } = null!;

    public string? SourceProviderCode { get; set; }

    public string? SourceExternalRequestId { get; set; }

    public Guid? SourceProviderRequestId { get; set; }

    public string? MetadataJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? VerifiedAtUtc { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual ICollection<CharacterReference> CharacterReferences { get; set; } = new List<CharacterReference>();

    public virtual ICollection<FinalVideo> FinalVideos { get; set; } = new List<FinalVideo>();

    public virtual ICollection<MusicAsset> MusicAssets { get; set; } = new List<MusicAsset>();

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<RenderJob> RenderJobs { get; set; } = new List<RenderJob>();

    public virtual Scene? Scene { get; set; }

    public virtual ICollection<SoundEffect> SoundEffects { get; set; } = new List<SoundEffect>();

    public virtual ICollection<Subtitle> Subtitles { get; set; } = new List<Subtitle>();

    public virtual ICollection<VideoGeneration> VideoGenerations { get; set; } = new List<VideoGeneration>();

    public virtual ICollection<VoiceGeneration> VoiceGenerations { get; set; } = new List<VoiceGeneration>();
}

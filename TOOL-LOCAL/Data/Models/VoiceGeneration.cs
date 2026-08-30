using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class VoiceGeneration
{
    public Guid VoiceGenerationId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ScriptId { get; set; }

    public Guid? SceneId { get; set; }

    public int? ScenePlanVersion { get; set; }

    public Guid ProviderRequestId { get; set; }

    public int Version { get; set; }

    public string VoiceCode { get; set; } = null!;

    public string? ProviderVoiceCode { get; set; }

    public string? NarrationHash { get; set; }

    public string? VoiceSnapshotJson { get; set; }

    public string LanguageCode { get; set; } = null!;

    public decimal SpeakingRate { get; set; }

    public string Status { get; set; } = null!;

    public long? DurationMs { get; set; }

    public string? WordTimingsJson { get; set; }

    public Guid? OutputMediaAssetId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual MediaAsset? OutputMediaAsset { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual ProviderRequest ProviderRequest { get; set; } = null!;

    public virtual Scene? Scene { get; set; }

    public virtual Script Script { get; set; } = null!;

    public virtual ICollection<Subtitle> Subtitles { get; set; } = new List<Subtitle>();
}

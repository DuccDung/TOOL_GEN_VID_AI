using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class Subtitle
{
    public Guid SubtitleId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? VoiceGenerationId { get; set; }

    public int Version { get; set; }

    public string Format { get; set; } = null!;

    public string LanguageCode { get; set; } = null!;

    public string? StyleJson { get; set; }

    public Guid MediaAssetId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual MediaAsset MediaAsset { get; set; } = null!;

    public virtual Project Project { get; set; } = null!;

    public virtual VoiceGeneration? VoiceGeneration { get; set; }
}

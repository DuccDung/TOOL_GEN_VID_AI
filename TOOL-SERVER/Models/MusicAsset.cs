using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class MusicAsset
{
    public Guid MusicAssetId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid MediaAssetId { get; set; }

    public string? Title { get; set; }

    public string SourceType { get; set; } = null!;

    public string? LicenseInfoJson { get; set; }

    public long TimelineStartMs { get; set; }

    public decimal GainDb { get; set; }

    public bool LoopEnabled { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual MediaAsset MediaAsset { get; set; } = null!;

    public virtual Project Project { get; set; } = null!;
}

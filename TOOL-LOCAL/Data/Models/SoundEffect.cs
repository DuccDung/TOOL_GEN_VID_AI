using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class SoundEffect
{
    public Guid SoundEffectId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? SceneId { get; set; }

    public Guid MediaAssetId { get; set; }

    public long CueTimeMs { get; set; }

    public decimal GainDb { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual MediaAsset MediaAsset { get; set; } = null!;

    public virtual Project Project { get; set; } = null!;

    public virtual Scene? Scene { get; set; }
}

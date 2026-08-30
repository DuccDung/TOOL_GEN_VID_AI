using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class FinalVideo
{
    public Guid FinalVideoId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid RenderJobId { get; set; }

    public Guid MediaAssetId { get; set; }

    public int Version { get; set; }

    public string Status { get; set; } = null!;

    public decimal? QualityScore { get; set; }

    public string? QualityReportJson { get; set; }

    public string? ExportedPath { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public DateTime? ExportedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual MediaAsset MediaAsset { get; set; } = null!;

    public virtual Project Project { get; set; } = null!;

    public virtual RenderJob RenderJob { get; set; } = null!;
}

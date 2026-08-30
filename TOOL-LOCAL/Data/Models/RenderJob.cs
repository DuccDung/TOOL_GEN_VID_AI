using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class RenderJob
{
    public Guid RenderJobId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? JobId { get; set; }

    public int Version { get; set; }

    public string Status { get; set; } = null!;

    public string ManifestJson { get; set; } = null!;

    public string ManifestHash { get; set; } = null!;

    public string? FfmpegVersion { get; set; }

    public decimal ProgressPercent { get; set; }

    public Guid? OutputMediaAssetId { get; set; }

    public string? TechnicalReportJson { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual ICollection<FinalVideo> FinalVideos { get; set; } = new List<FinalVideo>();

    public virtual Job? Job { get; set; }

    public virtual MediaAsset? OutputMediaAsset { get; set; }

    public virtual Project Project { get; set; } = null!;
}

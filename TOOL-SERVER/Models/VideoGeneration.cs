using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class VideoGeneration
{
    public Guid VideoGenerationId { get; set; }

    public Guid SceneId { get; set; }

    public Guid ScenePromptId { get; set; }

    public Guid? JobId { get; set; }

    public Guid ProviderRequestId { get; set; }

    public int AttemptNumber { get; set; }

    public string Status { get; set; } = null!;

    public long? Seed { get; set; }

    public long RequestedDurationMs { get; set; }

    public long? ActualDurationMs { get; set; }

    public string? InputReferenceAssetIdsJson { get; set; }

    public Guid? OutputMediaAssetId { get; set; }

    public decimal? QualityScore { get; set; }

    public string? QualityReportJson { get; set; }

    public string? RegenerationFeedbackJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual Job? Job { get; set; }

    public virtual MediaAsset? OutputMediaAsset { get; set; }

    public virtual ProviderRequest ProviderRequest { get; set; } = null!;

    public virtual Scene Scene { get; set; } = null!;

    public virtual ScenePrompt ScenePrompt { get; set; } = null!;

    public virtual ICollection<Scene> Scenes { get; set; } = new List<Scene>();
}

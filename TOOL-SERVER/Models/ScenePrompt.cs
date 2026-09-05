using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class ScenePrompt
{
    public Guid ScenePromptId { get; set; }

    public Guid SceneId { get; set; }

    public int Version { get; set; }

    public string PromptTemplateName { get; set; } = null!;

    public string PromptTemplateVersion { get; set; } = null!;

    public string CanonicalInputJson { get; set; } = null!;

    public string FinalPrompt { get; set; } = null!;

    public string? NegativePrompt { get; set; }

    public string? ProviderCode { get; set; }

    public string? ModelCode { get; set; }

    public string? ProviderPayloadJson { get; set; }

    public string PromptHash { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? QualityReportJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual Scene Scene { get; set; } = null!;

    public virtual ICollection<SceneFirstFrame> SceneFirstFrames { get; set; } = new List<SceneFirstFrame>();

    public virtual ICollection<VideoGeneration> VideoGenerations { get; set; } = new List<VideoGeneration>();
}

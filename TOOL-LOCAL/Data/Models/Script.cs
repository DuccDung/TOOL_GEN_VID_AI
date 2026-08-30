using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class Script
{
    public Guid ScriptId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? ConceptId { get; set; }

    public int Version { get; set; }

    public string StructureType { get; set; } = null!;

    public string? Title { get; set; }

    public string FullText { get; set; } = null!;

    public string? NarrationJson { get; set; }

    public string? DialogueJson { get; set; }

    public string StoryBeatsJson { get; set; } = null!;

    public long? EstimatedDurationMs { get; set; }

    public long? MeasuredVoiceDurationMs { get; set; }

    public decimal? QualityScore { get; set; }

    public string? QualityReportJson { get; set; }

    public string Status { get; set; } = null!;

    public string? ProviderCode { get; set; }

    public string? ModelCode { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual Concept? Concept { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<Scene> Scenes { get; set; } = new List<Scene>();

    public virtual ICollection<VoiceGeneration> VoiceGenerations { get; set; } = new List<VoiceGeneration>();
}

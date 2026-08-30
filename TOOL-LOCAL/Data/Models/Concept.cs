using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class Concept
{
    public Guid ConceptId { get; set; }

    public Guid ProjectId { get; set; }

    public int Version { get; set; }

    public string Title { get; set; } = null!;

    public string? SelectedHook { get; set; }

    public string? Angle { get; set; }

    public string? Audience { get; set; }

    public string? CallToAction { get; set; }

    public decimal? ViralScore { get; set; }

    public string? HooksJson { get; set; }

    public string? StrategyJson { get; set; }

    public string Status { get; set; } = null!;

    public string? ProviderCode { get; set; }

    public string? ModelCode { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<Script> Scripts { get; set; } = new List<Script>();
}

using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class StyleProfile
{
    public Guid StyleProfileId { get; set; }

    public Guid ProjectId { get; set; }

    public int Version { get; set; }

    public string Name { get; set; } = null!;

    public string VisualStyleJson { get; set; } = null!;

    public string? ColorStyleJson { get; set; }

    public string? CameraStyleJson { get; set; }

    public string? LightingStyleJson { get; set; }

    public string? EnvironmentJson { get; set; }

    public string? NegativeRulesJson { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<Scene> Scenes { get; set; } = new List<Scene>();
}

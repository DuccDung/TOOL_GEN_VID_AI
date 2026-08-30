using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class Approval
{
    public Guid ApprovalId { get; set; }

    public Guid ProjectId { get; set; }

    public string TargetType { get; set; } = null!;

    public Guid TargetId { get; set; }

    public int? TargetVersion { get; set; }

    public string Decision { get; set; } = null!;

    public string? Comment { get; set; }

    public string? ApprovedBy { get; set; }

    public DateTime DecidedAtUtc { get; set; }

    public virtual Project Project { get; set; } = null!;
}

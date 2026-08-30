using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class JobDependency
{
    public Guid JobDependencyId { get; set; }

    public Guid JobId { get; set; }

    public Guid DependsOnJobId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual Job DependsOnJob { get; set; } = null!;

    public virtual Job Job { get; set; } = null!;
}

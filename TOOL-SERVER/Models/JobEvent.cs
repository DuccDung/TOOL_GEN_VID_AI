using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class JobEvent
{
    public long JobEventId { get; set; }

    public Guid JobId { get; set; }

    public string EventType { get; set; } = null!;

    public string? FromStatus { get; set; }

    public string? ToStatus { get; set; }

    public string? Message { get; set; }

    public string? DataJson { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public virtual Job Job { get; set; } = null!;
}

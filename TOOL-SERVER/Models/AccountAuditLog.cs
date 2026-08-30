using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class AccountAuditLog
{
    public long AccountAuditLogId { get; set; }

    public string? UserId { get; set; }

    public string EventType { get; set; } = null!;

    public bool Succeeded { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? CorrelationId { get; set; }

    public string? DetailsJson { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}

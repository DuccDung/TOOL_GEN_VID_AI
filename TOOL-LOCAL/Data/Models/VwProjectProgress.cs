using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class VwProjectProgress
{
    public Guid ProjectId { get; set; }

    public string? RemoteUserId { get; set; }

    public Guid? RemoteDeviceId { get; set; }

    public string Name { get; set; } = null!;

    public string Topic { get; set; } = null!;

    public string Status { get; set; } = null!;

    public int TargetDurationSeconds { get; set; }

    public decimal EstimatedCost { get; set; }

    public decimal ActualCost { get; set; }

    public decimal? BudgetLimit { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public long TotalScenes { get; set; }

    public long ApprovedScenes { get; set; }

    public long FailedScenes { get; set; }

    public long PendingJobs { get; set; }

    public long RunningJobs { get; set; }

    public long WaitingProviderJobs { get; set; }

    public long FailedJobs { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

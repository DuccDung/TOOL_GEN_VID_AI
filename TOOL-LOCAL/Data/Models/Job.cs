using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class Job
{
    public Guid JobId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? SceneId { get; set; }

    public Guid? ParentJobId { get; set; }

    public string JobType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public int Priority { get; set; }

    public int Attempt { get; set; }

    public int MaxAttempts { get; set; }

    public decimal ProgressPercent { get; set; }

    public DateTime AvailableAtUtc { get; set; }

    public string? LockedBy { get; set; }

    public DateTime? LockedAtUtc { get; set; }

    public DateTime? HeartbeatAtUtc { get; set; }

    public DateTime? LeaseExpiresAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? PayloadJson { get; set; }

    public string? ResultJson { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual ICollection<Job> InverseParentJob { get; set; } = new List<Job>();

    public virtual ICollection<JobDependency> JobDependencyDependsOnJobs { get; set; } = new List<JobDependency>();

    public virtual ICollection<JobDependency> JobDependencyJobs { get; set; } = new List<JobDependency>();

    public virtual ICollection<JobEvent> JobEvents { get; set; } = new List<JobEvent>();

    public virtual Job? ParentJob { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<ProviderRequest> ProviderRequests { get; set; } = new List<ProviderRequest>();

    public virtual ICollection<RenderJob> RenderJobs { get; set; } = new List<RenderJob>();

    public virtual Scene? Scene { get; set; }

    public virtual ICollection<UsageCost> UsageCosts { get; set; } = new List<UsageCost>();

    public virtual ICollection<VideoGeneration> VideoGenerations { get; set; } = new List<VideoGeneration>();
}

using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class ProviderRequest
{
    public Guid ProviderRequestId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? SceneId { get; set; }

    public Guid? JobId { get; set; }

    public Guid? ProviderId { get; set; }

    public Guid? ProviderModelId { get; set; }

    public string RequestKind { get; set; } = null!;

    public string ProviderCode { get; set; } = null!;

    public string ModelCode { get; set; } = null!;

    public string? ExternalRequestId { get; set; }

    public string IdempotencyKey { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string RequestJson { get; set; } = null!;

    public string? ResponseJson { get; set; }

    public int PollCount { get; set; }

    public DateTime? LastPolledAtUtc { get; set; }

    public DateTime? NextPollAtUtc { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public decimal EstimatedCost { get; set; }

    public decimal ActualCost { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual Job? Job { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual Provider? Provider { get; set; }

    public virtual ProviderModel? ProviderModel { get; set; }

    public virtual Scene? Scene { get; set; }

    public virtual ICollection<UsageCost> UsageCosts { get; set; } = new List<UsageCost>();

    public virtual ICollection<VideoGeneration> VideoGenerations { get; set; } = new List<VideoGeneration>();

    public virtual ICollection<VoiceGeneration> VoiceGenerations { get; set; } = new List<VoiceGeneration>();
}

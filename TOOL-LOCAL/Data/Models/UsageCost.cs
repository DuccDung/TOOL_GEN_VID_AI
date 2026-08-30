using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class UsageCost
{
    public Guid UsageCostId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? SceneId { get; set; }

    public Guid? JobId { get; set; }

    public Guid? ProviderRequestId { get; set; }

    public string UsageKey { get; set; } = null!;

    public string CostKind { get; set; } = null!;

    public string? ProviderCode { get; set; }

    public string? ModelCode { get; set; }

    public string UsageType { get; set; } = null!;

    public decimal Quantity { get; set; }

    public string Unit { get; set; } = null!;

    public decimal UnitPrice { get; set; }

    public decimal TotalCost { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public string? RateSnapshotJson { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual Job? Job { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual ProviderRequest? ProviderRequest { get; set; }

    public virtual Scene? Scene { get; set; }
}

using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class CostRate
{
    public Guid CostRateId { get; set; }

    public Guid ProviderModelId { get; set; }

    public string UsageType { get; set; } = null!;

    public string Unit { get; set; } = null!;

    public decimal UnitPrice { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public DateTime EffectiveFromUtc { get; set; }

    public DateTime? EffectiveToUtc { get; set; }

    public bool IsActive { get; set; }

    public string? MetadataJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual ProviderModel ProviderModel { get; set; } = null!;
}

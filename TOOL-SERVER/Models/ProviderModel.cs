using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class ProviderModel
{
    public Guid ProviderModelId { get; set; }

    public Guid ProviderId { get; set; }

    public string ModelCode { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string Modality { get; set; } = null!;

    public bool IsEnabled { get; set; }

    public bool IsDefault { get; set; }

    public string? CapabilitiesJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual ICollection<CostRate> CostRates { get; set; } = new List<CostRate>();

    public virtual Provider Provider { get; set; } = null!;

    public virtual ICollection<ProviderRequest> ProviderRequests { get; set; } = new List<ProviderRequest>();
}

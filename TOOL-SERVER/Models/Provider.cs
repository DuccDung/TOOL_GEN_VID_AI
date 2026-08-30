using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class Provider
{
    public Guid ProviderId { get; set; }

    public string ProviderCode { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string? BaseUrl { get; set; }

    public bool IsEnabled { get; set; }

    public string? CapabilitiesJson { get; set; }

    public string? SecretReference { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual ICollection<ProviderModel> ProviderModels { get; set; } = new List<ProviderModel>();

    public virtual ICollection<ProviderRequest> ProviderRequests { get; set; } = new List<ProviderRequest>();
}

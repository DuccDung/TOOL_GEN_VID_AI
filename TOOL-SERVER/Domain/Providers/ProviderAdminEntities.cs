namespace TOOL_SERVER.Domain.Providers;

public sealed class AiProvider
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

    public byte[] RowVersion { get; set; } = [];

    public ICollection<AiProviderModel> Models { get; set; } = [];

    public ICollection<AiProviderCredential> Credentials { get; set; } = [];
}

public sealed class AiProviderModel
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

    public byte[] RowVersion { get; set; } = [];

    public AiProvider Provider { get; set; } = null!;

    public ICollection<AiCostRate> CostRates { get; set; } = [];
}

public sealed class AiProviderCredential
{
    public Guid ProviderCredentialId { get; set; }

    public Guid ProviderId { get; set; }

    public string Name { get; set; } = null!;

    public string AuthenticationType { get; set; } = null!;

    public string? HeaderName { get; set; }

    public string? TestPath { get; set; }

    public string EncryptedPayload { get; set; } = null!;

    public string SecretHint { get; set; } = null!;

    public bool IsActive { get; set; }

    public string TestStatus { get; set; } = ProviderCredentialTestStatuses.Unknown;

    public string? TestMessage { get; set; }

    public DateTime? LastTestedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public AiProvider Provider { get; set; } = null!;
}

public sealed class AiCostRate
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

    public AiProviderModel ProviderModel { get; set; } = null!;
}

public sealed class AiProviderRequestLog
{
    public Guid ProviderRequestId { get; set; }

    public Guid ProjectId { get; set; }

    public string RequestKind { get; set; } = null!;

    public string ProviderCode { get; set; } = null!;

    public string ModelCode { get; set; } = null!;

    public string Status { get; set; } = null!;

    public decimal EstimatedCost { get; set; }

    public decimal ActualCost { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

public static class ProviderCredentialAuthenticationTypes
{
    public const string Bearer = "Bearer";
    public const string Header = "Header";
}

public static class ProviderCredentialTestStatuses
{
    public const string Unknown = "Unknown";
    public const string Healthy = "Healthy";
    public const string Failed = "Failed";
}


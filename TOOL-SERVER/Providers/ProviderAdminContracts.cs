namespace TOOL_SERVER.Providers;

public sealed record AdminProviderResponse(
    Guid ProviderId,
    string ProviderCode,
    string DisplayName,
    string? BaseUrl,
    bool IsEnabled,
    string? CapabilitiesJson,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<AdminProviderModelResponse> Models);

public sealed record AdminProviderModelResponse(
    Guid ProviderModelId,
    Guid ProviderId,
    string ModelCode,
    string DisplayName,
    string Modality,
    bool IsEnabled,
    bool IsDefault,
    string? CapabilitiesJson,
    IReadOnlyList<AdminCostRateResponse> CostRates);

public sealed record AdminCostRateResponse(
    Guid CostRateId,
    string UsageType,
    string Unit,
    decimal UnitPrice,
    string CurrencyCode,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    bool IsActive,
    string? MetadataJson);

public sealed record CreateAdminCostRateRequest(
    string UsageType,
    string Unit,
    decimal UnitPrice,
    string CurrencyCode,
    DateTime? EffectiveFromUtc = null,
    string? MetadataJson = null);

public sealed record UpdateAdminProviderStateRequest(bool IsEnabled);

public sealed record UpdateAdminProviderModelStateRequest(
    bool IsEnabled,
    bool IsDefault = false);

public sealed record AdminRequestContext(
    string UserId,
    string? IpAddress,
    string? UserAgent,
    string CorrelationId);

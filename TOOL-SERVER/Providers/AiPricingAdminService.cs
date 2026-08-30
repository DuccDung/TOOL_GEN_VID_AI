using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Domain.Providers;
using TOOL_SERVER.Generation;

namespace TOOL_SERVER.Providers;

public interface IAiPricingAdminService
{
    Task<IReadOnlyList<AdminProviderResponse>> GetCatalogAsync(CancellationToken cancellationToken);
    Task<AdminProviderResponse> UpdateProviderStateAsync(Guid providerId, UpdateAdminProviderStateRequest request, AdminRequestContext context, CancellationToken cancellationToken);
    Task<AdminProviderModelResponse> UpdateModelStateAsync(Guid modelId, UpdateAdminProviderModelStateRequest request, AdminRequestContext context, CancellationToken cancellationToken);
    Task<AdminCostRateResponse> AddRateAsync(Guid modelId, CreateAdminCostRateRequest request, AdminRequestContext context, CancellationToken cancellationToken);
    Task DeactivateRateAsync(Guid rateId, AdminRequestContext context, CancellationToken cancellationToken);
}

internal sealed class AiPricingAdminService(
    ProviderAdminDbContext dbContext,
    TimeProvider timeProvider) : IAiPricingAdminService
{
    public async Task<IReadOnlyList<AdminProviderResponse>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var providers = await dbContext.Providers
            .AsNoTracking()
            .Include(x => x.Models)
                .ThenInclude(x => x.CostRates)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        return providers.Select(provider => new AdminProviderResponse(
            provider.ProviderId,
            provider.ProviderCode,
            provider.DisplayName,
            provider.BaseUrl,
            provider.IsEnabled,
            provider.CapabilitiesJson,
            provider.CreatedAtUtc,
            provider.UpdatedAtUtc,
            provider.Models.OrderBy(x => x.Modality).ThenBy(x => x.DisplayName).Select(ToModel).ToArray())).ToArray();
    }

    public async Task<AdminCostRateResponse> AddRateAsync(
        Guid modelId,
        CreateAdminCostRateRequest request,
        AdminRequestContext context,
        CancellationToken cancellationToken)
    {
        var usageType = ValidateUsageType(request.UsageType);
        var unit = ValidateUnit(usageType, request.Unit);
        if (request.UnitPrice <= 0 || request.UnitPrice > 1_000_000m)
        {
            throw new ArgumentException("Đơn giá AI phải lớn hơn 0.");
        }
        if (!request.CurrencyCode.Trim().Equals("USD", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Phiên bản hiện tại chỉ hỗ trợ USD.");
        }
        if (!string.IsNullOrWhiteSpace(request.MetadataJson))
        {
            try
            {
                JsonDocument.Parse(request.MetadataJson).Dispose();
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("MetadataJson không hợp lệ.", nameof(request), exception);
            }
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var model = await dbContext.ProviderModels
            .Include(x => x.Provider)
            .SingleOrDefaultAsync(x => x.ProviderModelId == modelId, cancellationToken)
            ?? throw new AccountApiException(StatusCodes.Status404NotFound, "provider_model_not_found", "Không tìm thấy model AI.");
        if (model.Provider.ProviderCode == ProviderCodes.Kling &&
            usageType == "VideoSecond" &&
            !KlingNativeAudioPolicy.MatchesRateMetadata(request.MetadataJson))
        {
            throw new ArgumentException(
                "Rate Kling MVP phải có metadata resolution=720p và nativeAudio=true.");
        }
        var now = UtcNow();
        var effectiveFrom = NormalizeEffectiveFrom(request.EffectiveFromUtc, now);
        var active = await dbContext.CostRates
            .Where(x => x.ProviderModelId == modelId && x.UsageType == usageType && x.IsActive)
            .ToListAsync(cancellationToken);
        if (active.Any(x => effectiveFrom <= x.EffectiveFromUtc))
        {
            throw new AccountApiException(
                StatusCodes.Status409Conflict,
                "cost_rate_effective_date_conflict",
                "Ngày hiệu lực mới phải sau ngày hiệu lực của đơn giá đang hoạt động.");
        }
        foreach (var current in active)
        {
            current.IsActive = false;
            current.EffectiveToUtc = effectiveFrom;
        }
        var rate = new AiCostRate
        {
            CostRateId = Guid.NewGuid(),
            ProviderModelId = modelId,
            UsageType = usageType,
            Unit = unit,
            UnitPrice = request.UnitPrice,
            CurrencyCode = "USD",
            EffectiveFromUtc = effectiveFrom,
            IsActive = true,
            MetadataJson = request.MetadataJson,
            CreatedAtUtc = UtcNow()
        };
        dbContext.CostRates.Add(rate);
        AddAudit(context, "AiCostRateCreated", new { rate.CostRateId, modelId, usageType, unit, request.UnitPrice });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToRate(rate);
    }

    public async Task<AdminProviderResponse> UpdateProviderStateAsync(
        Guid providerId,
        UpdateAdminProviderStateRequest request,
        AdminRequestContext context,
        CancellationToken cancellationToken)
    {
        var provider = await dbContext.Providers
            .Include(x => x.Models)
                .ThenInclude(x => x.CostRates)
            .SingleOrDefaultAsync(x => x.ProviderId == providerId, cancellationToken)
            ?? throw new AccountApiException(
                StatusCodes.Status404NotFound,
                "provider_not_found",
                "Không tìm thấy provider AI.");
        provider.IsEnabled = request.IsEnabled;
        provider.UpdatedAtUtc = UtcNow();
        AddAudit(context, "AiProviderStateUpdated", new { providerId, provider.ProviderCode, request.IsEnabled });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToProvider(provider);
    }

    public async Task<AdminProviderModelResponse> UpdateModelStateAsync(
        Guid modelId,
        UpdateAdminProviderModelStateRequest request,
        AdminRequestContext context,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var model = await dbContext.ProviderModels
            .Include(x => x.Provider)
            .Include(x => x.CostRates)
            .SingleOrDefaultAsync(x => x.ProviderModelId == modelId, cancellationToken)
            ?? throw new AccountApiException(
                StatusCodes.Status404NotFound,
                "provider_model_not_found",
                "Không tìm thấy model AI.");
        if (request.IsDefault && !request.IsEnabled)
        {
            throw new ArgumentException("Model mặc định phải ở trạng thái enabled.");
        }
        if (request.IsDefault)
        {
            var currentDefaults = await dbContext.ProviderModels
                .Where(x => x.ProviderId == model.ProviderId &&
                            x.Modality == model.Modality &&
                            x.IsDefault &&
                            x.ProviderModelId != modelId)
                .ToListAsync(cancellationToken);
            foreach (var current in currentDefaults)
            {
                current.IsDefault = false;
                current.UpdatedAtUtc = UtcNow();
            }
        }
        model.IsEnabled = request.IsEnabled;
        model.IsDefault = request.IsEnabled && request.IsDefault;
        model.UpdatedAtUtc = UtcNow();
        AddAudit(
            context,
            "AiProviderModelStateUpdated",
            new { modelId, model.Provider.ProviderCode, model.ModelCode, request.IsEnabled, request.IsDefault });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToModel(model);
    }

    public async Task DeactivateRateAsync(
        Guid rateId,
        AdminRequestContext context,
        CancellationToken cancellationToken)
    {
        var rate = await dbContext.CostRates.SingleOrDefaultAsync(x => x.CostRateId == rateId, cancellationToken)
            ?? throw new AccountApiException(StatusCodes.Status404NotFound, "cost_rate_not_found", "Không tìm thấy đơn giá AI.");
        rate.IsActive = false;
        rate.EffectiveToUtc ??= UtcNow();
        AddAudit(context, "AiCostRateDeactivated", new { rateId });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void AddAudit(AdminRequestContext context, string eventType, object data) =>
        dbContext.AccountAuditLogs.Add(new AccountAuditLog
        {
            UserId = context.UserId,
            EventType = eventType,
            Succeeded = true,
            DetailsJson = JsonSerializer.Serialize(data),
            IpAddress = context.IpAddress,
            UserAgent = context.UserAgent,
            CorrelationId = context.CorrelationId,
            OccurredAtUtc = UtcNow()
        });

    private static string ValidateUsageType(string value) => value.Trim() switch
    {
        "InputToken" => "InputToken",
        "OutputToken" => "OutputToken",
        "VideoSecond" => "VideoSecond",
        _ => throw new ArgumentException("UsageType chỉ hỗ trợ InputToken, OutputToken hoặc VideoSecond.")
    };

    private static string ValidateUnit(string usageType, string value)
    {
        var unit = value.Trim();
        if (usageType == "VideoSecond" && unit == "Second")
        {
            return unit;
        }
        if (usageType is "InputToken" or "OutputToken" && unit is "Token" or "1KTokens" or "MillionTokens")
        {
            return unit;
        }
        throw new ArgumentException("Đơn vị đơn giá AI không phù hợp với UsageType.");
    }

    internal static DateTime NormalizeEffectiveFrom(DateTime? requested, DateTime nowUtc)
    {
        var effectiveFrom = requested?.ToUniversalTime() ?? nowUtc;
        if (effectiveFrom > nowUtc)
        {
            throw new ArgumentException(
                "Phiên bản hiện tại chưa hỗ trợ đơn giá có ngày hiệu lực trong tương lai.");
        }
        return effectiveFrom;
    }

    private static AdminProviderModelResponse ToModel(AiProviderModel model) =>
        new(
            model.ProviderModelId,
            model.ProviderId,
            model.ModelCode,
            model.DisplayName,
            model.Modality,
            model.IsEnabled,
            model.IsDefault,
            model.CapabilitiesJson,
            model.CostRates.OrderByDescending(x => x.EffectiveFromUtc).Select(ToRate).ToArray());

    private static AdminProviderResponse ToProvider(AiProvider provider) =>
        new(
            provider.ProviderId,
            provider.ProviderCode,
            provider.DisplayName,
            provider.BaseUrl,
            provider.IsEnabled,
            provider.CapabilitiesJson,
            provider.CreatedAtUtc,
            provider.UpdatedAtUtc,
            provider.Models.OrderBy(x => x.Modality).ThenBy(x => x.DisplayName).Select(ToModel).ToArray());

    private static AdminCostRateResponse ToRate(AiCostRate rate) =>
        new(
            rate.CostRateId,
            rate.UsageType,
            rate.Unit,
            rate.UnitPrice,
            rate.CurrencyCode,
            rate.EffectiveFromUtc,
            rate.EffectiveToUtc,
            rate.IsActive,
            rate.MetadataJson);

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}

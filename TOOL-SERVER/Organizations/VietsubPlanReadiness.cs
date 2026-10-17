using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Vietsub.Translation;

namespace TOOL_SERVER.Organizations;

internal static class VietsubPlanReadiness
{
    public static async Task<OrganizationProvisioningReadiness> EvaluateAsync(AccountDbContext db,
        Guid organizationId, bool cloud, decimal? memberBudget, DateTime now,
        VietsubCloudTranslationOptions? options, CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == organizationId, ct);
        if (org is not { Status: "Active" }) return new(false, "Tổ chức chưa hoạt động.", "organization_not_ready", "organization");
        if (!cloud) return new(true, "Tool dịch Local không yêu cầu API key hoặc ngân sách AI.");
        if (options is not { Enabled: true } || string.IsNullOrWhiteSpace(options.ModelCode))
            return new(false, "Dịch Cloud chưa được bật hoặc chưa chọn model dịch trên server.", "CLOUD_DISABLED", "cloud-settings");
        if (db.Database.IsSqlServer() && await db.Database.SqlQueryRaw<int>(
                "SELECT CASE WHEN OBJECT_ID(N'vs.CloudTranslationJobs', N'U') IS NOT NULL AND OBJECT_ID(N'vs.CloudTranslationBatches', N'U') IS NOT NULL AND OBJECT_ID(N'vs.CloudTranslationAttempts', N'U') IS NOT NULL THEN 1 ELSE 0 END AS [Value]")
                .SingleAsync(ct) != 1)
            return new(false, "Database chưa có đủ bảng dịch Cloud. Cần áp migration Vietsub Cloud trước khi mở bán.", "CLOUD_CONFIGURATION_REQUIRED", "cloud-settings");
        if (org.MonthlyBudgetLimit <= 0 || memberBudget == 0)
            return new(false, "Cần ngân sách OpenAI lớn hơn 0 cho dịch Cloud.", "organization_budget_exceeded", "budget");
        var model = await db.ProviderModels.AsNoTracking().Include(x => x.Provider).SingleOrDefaultAsync(x =>
            x.ModelCode == options.ModelCode && x.Modality == "Text" && x.Provider.ProviderCode == "openai", ct);
        if (model is null || !model.IsEnabled || !model.Provider.IsEnabled || !VietsubCloudConfiguration.ValidModel(model))
            return new(false, "Model OpenAI dùng để dịch chưa sẵn sàng hoặc thiếu structured output.", "CLOUD_CONFIGURATION_REQUIRED",
                model?.Provider.IsEnabled == false ? "provider" : "model", model?.ProviderModelId);
        if (!await db.OrganizationProviderCredentials.AsNoTracking().AnyAsync(x => x.OrganizationId == organizationId &&
            x.ProviderId == model.ProviderId && x.Status == "Active", ct))
            return new(false, "Chưa có credential OpenAI Active cho dịch Cloud.", "credential_missing", "credential", model.ProviderModelId);
        var rates = await db.CostRates.AsNoTracking().Where(x => x.ProviderModelId == model.ProviderModelId && x.IsActive &&
            x.EffectiveFromUtc <= now && (x.EffectiveToUtc == null || x.EffectiveToUtc > now)).ToListAsync(ct);
        if (VietsubCloudConfiguration.SelectRates(rates.Select(VietsubCloudConfiguration.Metadata), org.CurrencyCode) is null)
            return new(false, "Cần đơn giá InputToken và OutputToken duy nhất, hợp lệ cho model dịch.", "pricing_not_configured", "pricing", model.ProviderModelId);
        var periods = await db.OrganizationBudgetPeriods.AsNoTracking().Where(x => x.OrganizationId == organizationId &&
            x.StartsAtUtc <= now && x.EndsAtUtc > now).ToListAsync(ct);
        if (periods.Count > 1 || (periods.Count == 1 && periods[0].HardLimit - periods[0].ActualCost - periods[0].ReservedCost <= 0))
            return new(false, "Ngân sách dịch Cloud của kỳ hiện tại không khả dụng.", "organization_budget_exceeded", "budget", model.ProviderModelId);
        return new(true, "OpenAI, đơn giá và ngân sách dịch Cloud đã sẵn sàng.", Target: "credential", ModelId: model.ProviderModelId);
    }
}

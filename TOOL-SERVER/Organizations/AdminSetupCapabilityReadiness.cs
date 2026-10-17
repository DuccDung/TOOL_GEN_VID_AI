using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Generation;

namespace TOOL_SERVER.Organizations;

// A plan-scoped profile: legacy plans keep their current provider/readiness rules.
internal static class AdminSetupCapabilityReadiness
{
    public const string Flag = "requiresOpenAiImageFalSetup";
    public static bool Required(string? flags)
    {
        try { using var doc = JsonDocument.Parse(flags ?? "{}"); return doc.RootElement.TryGetProperty(Flag, out var value) && value.ValueKind == JsonValueKind.True; }
        catch (JsonException) { return false; }
    }

    public static async Task<bool> ReadyAsync(AccountDbContext db, Guid orgId, decimal? memberBudget, DateTime now, CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == orgId, ct);
        if (org is not { Status: OrganizationStatuses.Active, MonthlyBudgetLimit: > 0, CurrencyCode: "USD" } || memberBudget == 0) return false;
        var policy = await db.OrganizationVideoPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == orgId && x.PolicyScope == "LongForm", ct);
        if (policy is not { IsActive: true, Resolution: "720p", NativeAudio: true }) return false;
        var catalog = await db.ProviderModels.AsNoTracking().Include(x => x.Provider).Where(x => x.IsEnabled && x.Provider.IsEnabled).ToListAsync(ct);
        var text = catalog.Where(x => x.Modality == "Text" && x.Provider.ProviderCode == "openai").OrderByDescending(x => x.IsDefault).ThenByDescending(x => x.UpdatedAtUtc).FirstOrDefault();
        var image = catalog.Where(x => x.Modality == "Image" && x.Provider.ProviderCode == "openai").OrderByDescending(x => x.IsDefault).ThenByDescending(x => x.UpdatedAtUtc).FirstOrDefault();
        var video = catalog.SingleOrDefault(x => x.ProviderModelId == policy.ProviderModelId && x.Provider.ProviderCode == "fal" && x.Modality == "Video");
        if (text is null || image is null || video is null || !FalVeoPolicy.IsApprovedEndpoint(video.ModelCode)) return false;
        var caps = VideoModelCapabilities.Parse(video.CapabilitiesJson, "fal");
        if (!caps.NativeAudio || !caps.ReferenceImage || !caps.Resolutions.Contains("720p") || !new[] { 4, 6, 8 }.All(caps.AllowedDurationsSeconds.Contains) || !caps.AspectRatios.Contains("16:9") || !caps.AspectRatios.Contains("9:16")) return false;
        foreach (var provider in new[] { text.Provider, video.Provider })
            if (!Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var uri) || !ProviderRuntimeResolver.IsAllowedBaseUri(provider.ProviderCode, uri) ||
                !await db.OrganizationProviderCredentials.AsNoTracking().AnyAsync(x => x.OrganizationId == orgId && x.ProviderId == provider.ProviderId && x.Status == ProviderCredentialStatuses.Active, ct)) return false;
        var ids = new[] { text.ProviderModelId, image.ProviderModelId, video.ProviderModelId };
        var rates = await db.CostRates.AsNoTracking().Where(x => ids.Contains(x.ProviderModelId) && x.IsActive).ToListAsync(ct);
        decimal quote = 0;
        foreach (var model in new[] { text, image, video })
        foreach (var usage in model.Modality == "Video" ? new[] { "VideoSecond" } : new[] { "InputToken", "OutputToken" })
        {
            var matches = rates.Where(x => x.ProviderModelId == model.ProviderModelId && x.UsageType == usage && x.UnitPrice > 0 && x.CurrencyCode == "USD" &&
                x.EffectiveFromUtc <= now && (x.EffectiveToUtc == null || x.EffectiveToUtc > now) &&
                (model.Modality == "Video" ? x.Unit == "Second" && FalVeoPolicy.MatchesRateMetadata(x.MetadataJson, model.ModelCode) : x.Unit is "Token" or "1KTokens" or "MillionTokens")).ToArray();
            if (matches.Length != 1) return false;
            var r = matches[0];
            quote += model.Modality == "Video" ? r.UnitPrice * 8 : r.UnitPrice * (r.Unit == "Token" ? 1000 : r.Unit == "1KTokens" ? 1 : .001m);
        }
        var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = await db.OrganizationBudgetPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == orgId && x.StartsAtUtc == month, ct);
        var remaining = period is null ? org.MonthlyBudgetLimit : period.HardLimit - period.ActualCost - period.ReservedCost;
        return remaining >= quote && (memberBudget is null || memberBudget >= quote);
    }
}

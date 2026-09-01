using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Domain.Providers;
using TOOL_SERVER.Providers;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_SERVER.Generation;

internal sealed record ProviderRuntimeConfiguration(
    Guid ProviderId,
    Guid ProviderModelId,
    Guid OrganizationProviderCredentialId,
    string ProviderCode,
    string ModelCode,
    Uri BaseUri,
    string AuthenticationType,
    string? HeaderName,
    string ApiKey,
    string? ModelCapabilitiesJson = null,
    string? ProviderDisplayName = null,
    string? ModelDisplayName = null);

internal interface IProviderRuntimeResolver
{
    Task<ProviderRuntimeConfiguration> ResolveAsync(
        Guid organizationId,
        string providerCode,
        string modality,
        Guid? credentialId,
        CancellationToken cancellationToken);

    Task<ProviderRuntimeConfiguration> ResolveModelAsync(
        Guid organizationId,
        string providerCode,
        string modality,
        string modelCode,
        Guid? credentialId,
        bool requireEnabled,
        CancellationToken cancellationToken) =>
        ResolveAsync(organizationId, providerCode, modality, credentialId, cancellationToken);

    Task<GenerationProviderStatusResponse> GetStatusAsync(Guid organizationId, CancellationToken cancellationToken);
}

internal sealed class ProviderRuntimeResolver(
    ProviderAdminDbContext dbContext,
    AiGovernanceDbContext governanceDbContext,
    IProviderCredentialProtector credentialProtector) : IProviderRuntimeResolver
{
    public async Task<ProviderRuntimeConfiguration> ResolveAsync(
        Guid organizationId,
        string providerCode,
        string modality,
        Guid? credentialId,
        CancellationToken cancellationToken)
        => await ResolveCoreAsync(
            organizationId,
            providerCode,
            modality,
            null,
            credentialId,
            true,
            cancellationToken);

    public Task<ProviderRuntimeConfiguration> ResolveModelAsync(
        Guid organizationId,
        string providerCode,
        string modality,
        string modelCode,
        Guid? credentialId,
        bool requireEnabled,
        CancellationToken cancellationToken) =>
        ResolveCoreAsync(
            organizationId,
            providerCode,
            modality,
            modelCode,
            credentialId,
            requireEnabled,
            cancellationToken);

    private async Task<ProviderRuntimeConfiguration> ResolveCoreAsync(
        Guid organizationId,
        string providerCode,
        string modality,
        string? modelCode,
        Guid? credentialId,
        bool requireEnabled,
        CancellationToken cancellationToken)
    {
        var provider = await dbContext.Providers
            .AsNoTracking()
            .Include(x => x.Models)
            .SingleOrDefaultAsync(
                x => x.ProviderCode == providerCode && (!requireEnabled || x.IsEnabled),
                cancellationToken);
        if (provider is null)
        {
            throw ConfigurationError(providerCode, $"Provider {providerCode} chưa được bật trong Admin Console.");
        }

        var model = provider.Models
            .Where(x => (!requireEnabled || x.IsEnabled) &&
                        x.Modality == modality &&
                        (modelCode == null || x.ModelCode == modelCode))
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefault();
        if (model is null)
        {
            throw ConfigurationError(providerCode, $"Provider {providerCode} chưa có model {modality} đang hoạt động.");
        }

        var credentialQuery = governanceDbContext.OrganizationProviderCredentials
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ProviderId == provider.ProviderId);
        var credential = credentialId is { } selectedCredentialId
            ? await credentialQuery.SingleOrDefaultAsync(
                x => x.OrganizationProviderCredentialId == selectedCredentialId &&
                     (x.Status == ProviderCredentialStatuses.Active ||
                      x.Status == ProviderCredentialStatuses.Retiring),
                cancellationToken)
            : await credentialQuery
                .Where(x => x.Status == ProviderCredentialStatuses.Active)
                .OrderByDescending(x => x.Version)
                .FirstOrDefaultAsync(cancellationToken);
        if (credential is null)
        {
            throw ConfigurationError(providerCode, $"Provider {providerCode} chưa có API key đang hoạt động.");
        }

        if (!Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var baseUri) ||
            !IsAllowedBaseUri(providerCode, baseUri))
        {
            throw ConfigurationError(providerCode, $"Base URL của provider {providerCode} không hợp lệ.");
        }

        string apiKey;
        try
        {
            apiKey = credentialProtector.Unprotect(credential.EncryptedPayload);
        }
        catch (Exception exception) when (exception is not AccountApiException)
        {
            throw ConfigurationError(
                providerCode,
                $"Không thể giải mã API key của {providerCode}; hãy lưu lại credential trong Admin Console.");
        }

        return new ProviderRuntimeConfiguration(
            provider.ProviderId,
            model.ProviderModelId,
            credential.OrganizationProviderCredentialId,
            provider.ProviderCode,
            model.ModelCode,
            baseUri,
            provider.ProviderCode == ProviderCodes.Fal
                ? ProviderCredentialAuthenticationTypes.Key
                : ProviderCredentialAuthenticationTypes.Bearer,
            null,
            apiKey,
            model.CapabilitiesJson,
            provider.DisplayName,
            model.DisplayName);
    }

    public async Task<GenerationProviderStatusResponse> GetStatusAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var providers = await dbContext.Providers
            .AsNoTracking()
            .Include(x => x.Models)
            .Where(x => (x.ProviderCode == ProviderCodes.OpenAi ||
                         x.ProviderCode == ProviderCodes.Kling ||
                         x.ProviderCode == ProviderCodes.BytePlus ||
                         x.ProviderCode == ProviderCodes.Fal) && x.IsEnabled)
            .ToListAsync(cancellationToken);

        var providerIds = providers.Select(x => x.ProviderId).ToArray();
        var configuredProviderIds = await governanceDbContext.OrganizationProviderCredentials
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId &&
                        providerIds.Contains(x.ProviderId) &&
                        x.Status == ProviderCredentialStatuses.Active)
            .Select(x => x.ProviderId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var openAi = ReadyProvider(providers, configuredProviderIds, ProviderCodes.OpenAi, "Text");
        var openAiImage = ReadyProvider(providers, configuredProviderIds, ProviderCodes.OpenAi, "Image");
        var openAiVoice = ReadyProvider(providers, configuredProviderIds, ProviderCodes.OpenAi, "Voice");
        var kling = ReadyProvider(providers, configuredProviderIds, ProviderCodes.Kling, "Video");
        var policy = await governanceDbContext.OrganizationVideoPolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId &&
                     x.PolicyScope == OrganizationVideoPolicyScopes.LongForm &&
                     x.IsActive,
                cancellationToken);
        policy ??= await governanceDbContext.OrganizationVideoPolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId &&
                     x.PolicyScope == OrganizationVideoPolicyScopes.Default &&
                     x.IsActive,
                cancellationToken);
        var policyProvider = policy is null
            ? null
            : providers.SingleOrDefault(x => x.ProviderId == policy.ProviderId);
        var policyModel = policyProvider?.Models.SingleOrDefault(
            x => x.ProviderModelId == policy!.ProviderModelId && x.IsEnabled && x.Modality == "Video");
        var videoReady = policyProvider is not null &&
                         policyModel is not null &&
                         configuredProviderIds.Contains(policyProvider.ProviderId);
        return new GenerationProviderStatusResponse(
            openAi.Ready,
            openAi.Model,
            kling.Ready,
            kling.Model,
            OpenAiImageReady: openAiImage.Ready,
            OpenAiImageModel: openAiImage.Model,
            OpenAiVoiceReady: openAiVoice.Ready,
            OpenAiVoiceModel: openAiVoice.Model,
            VideoReady: videoReady,
            VideoProviderCode: policyProvider?.ProviderCode,
            VideoProviderName: policyProvider?.DisplayName,
            VideoModel: policyModel?.ModelCode,
            VideoNativeAudio: policy?.NativeAudio ?? true,
            VideoResolution: policy?.Resolution ?? "720p");
    }

    private static (bool Ready, string? Model) ReadyProvider(
        IReadOnlyCollection<AiProvider> providers,
        IReadOnlyCollection<Guid> configuredProviderIds,
        string providerCode,
        string modality)
    {
        var provider = providers.SingleOrDefault(x => x.ProviderCode == providerCode);
        var model = provider?.Models
            .Where(x => x.IsEnabled && x.Modality == modality)
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefault();
        return (provider is not null && configuredProviderIds.Contains(provider.ProviderId) && model is not null, model?.ModelCode);
    }

    private static AccountApiException ConfigurationError(string providerCode, string message) =>
        new(
            StatusCodes.Status503ServiceUnavailable,
            $"{providerCode}_not_configured",
            message);

    internal static bool IsAllowedBaseUri(string providerCode, Uri baseUri) =>
        baseUri.Scheme == Uri.UriSchemeHttps &&
        baseUri.Port == 443 &&
        providerCode switch
        {
            ProviderCodes.OpenAi => baseUri.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase),
            ProviderCodes.Kling => baseUri.Host.Equals("api-singapore.klingai.com", StringComparison.OrdinalIgnoreCase),
            ProviderCodes.BytePlus => baseUri.Host.Equals("ark.ap-southeast.bytepluses.com", StringComparison.OrdinalIgnoreCase),
            ProviderCodes.Fal => baseUri.Host.Equals("queue.fal.run", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
}

internal static class ProviderCodes
{
    public const string OpenAi = "openai";
    public const string Kling = "kling";
    public const string BytePlus = "byteplus";
    public const string Fal = "fal";
}

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Providers;
using TOOL_SERVER.Providers;

namespace TOOL_SERVER.Generation;

internal static class ProviderCatalogBootstrapper
{
    private const int CatalogWriteMaxAttempts = 3;
    private const int CatalogWriteRetryDelayMilliseconds = 50;

    private static readonly ProviderSeed[] Catalog =
    [
        new(
            ProviderCodes.OpenAi,
            "OpenAI",
            "https://api.openai.com/v1/",
            "{\"responses\":true,\"imageGeneration\":true,\"speechGeneration\":true}",
            true,
            [
                new(
                    "gpt-5.6-luna",
                    "GPT-5.6 Luna",
                    "Text",
                    "{\"api\":\"responses\",\"structuredOutput\":true}",
                    true),
                new(
                    "gpt-image-2",
                    "GPT-Image-2",
                    "Image",
                    "{\"api\":\"images/generations\",\"n\":1,\"size\":\"1024x1024\",\"defaultQuality\":\"medium\",\"outputFormat\":\"png\"}",
                    true),
                new(
                    "gpt-4o-mini-tts",
                    "GPT-4o Mini TTS",
                    "Voice",
                    "{\"api\":\"audio/speech\",\"responseFormat\":\"wav\",\"maxInputTokens\":2000,\"usageSource\":\"estimated\"}",
                    true)
            ]),
        new(
            ProviderCodes.Kling,
            "Kling AI",
            "https://api-singapore.klingai.com/",
            "{\"videoGeneration\":true}",
            true,
            [
                new(
                    "kling-3.0",
                    "Kling 3.0",
                    "Video",
                    "{\"endpoint\":\"text-to-video/kling-3.0\",\"imageToVideoEndpoint\":\"image-to-video/kling-3.0\",\"characterReferenceMode\":\"firstFrame\",\"durations\":[3,4,5,6,7,8,9,10,11,12,13,14,15],\"minDurationSeconds\":3,\"maxDurationSeconds\":15,\"resolutions\":[\"720p\"],\"aspectRatios\":[\"16:9\",\"9:16\",\"1:1\"],\"framesPerSecond\":24,\"nativeAudio\":true,\"defaultAudio\":\"native\",\"billingVariant\":\"720p-native-audio\",\"billingUsageType\":\"VideoSecond\"}",
                    true)
            ]),
        new(
            ProviderCodes.BytePlus,
            "BytePlus ModelArk",
            "https://ark.ap-southeast.bytepluses.com/api/v3/",
            "{\"videoGeneration\":true,\"asyncTasks\":true,\"nativeAudio\":true}",
            false,
            [
                new(
                    "dreamina-seedance-2-0-260128",
                    "Dreamina Seedance 2.0",
                    "Video",
                    "{\"endpoint\":\"contents/generations/tasks\",\"durations\":[4,5,6,7,8,9,10,11,12,13,14,15],\"minDurationSeconds\":4,\"maxDurationSeconds\":15,\"resolutions\":[\"720p\"],\"aspectRatios\":[\"16:9\",\"9:16\",\"1:1\"],\"framesPerSecond\":24,\"nativeAudio\":true,\"billingUsageType\":\"OutputToken\",\"billingUnit\":\"MillionTokens\",\"referenceImage\":true}",
                    false),
                new(
                    "dreamina-seedance-2-5-260628",
                    "Dreamina Seedance 2.5",
                    "Video",
                    "{\"endpoint\":\"contents/generations/tasks\",\"durations\":[4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30],\"minDurationSeconds\":4,\"maxDurationSeconds\":30,\"resolutions\":[\"720p\"],\"aspectRatios\":[\"16:9\",\"9:16\",\"1:1\"],\"framesPerSecond\":24,\"nativeAudio\":true,\"billingUsageType\":\"OutputToken\",\"billingUnit\":\"MillionTokens\",\"referenceImage\":true}",
                    false)
            ]),
        new(
            ProviderCodes.Fal,
            "fal",
            "https://queue.fal.run/",
            "{\"videoGeneration\":true,\"asyncQueue\":true,\"nativeAudio\":true,\"defaultEndpointId\":\"fal-ai/veo3.1/image-to-video\"}",
            false,
            [
                new(
                    FalVeoPolicy.StandardEndpointId,
                    "Veo 3.1 Standard · Image to Video",
                    "Video",
                    "{\"endpointId\":\"fal-ai/veo3.1/image-to-video\",\"tier\":\"standard\",\"durations\":[4,6,8],\"resolutions\":[\"720p\"],\"aspectRatios\":[\"16:9\",\"9:16\"],\"framesPerSecond\":24,\"nativeAudio\":true,\"referenceImage\":true,\"imageToVideoOnly\":true,\"maximumReferenceImageBytes\":8388608,\"billingUsageType\":\"VideoSecond\",\"billingUnit\":\"Second\",\"autoFix\":false}",
                    false,
                    true),
                new(
                    FalVeoPolicy.FastEndpointId,
                    "Veo 3.1 Fast · Image to Video",
                    "Video",
                    "{\"endpointId\":\"fal-ai/veo3.1/fast/image-to-video\",\"tier\":\"fast\",\"durations\":[4,6,8],\"resolutions\":[\"720p\"],\"aspectRatios\":[\"16:9\",\"9:16\"],\"framesPerSecond\":24,\"nativeAudio\":true,\"referenceImage\":true,\"imageToVideoOnly\":true,\"maximumReferenceImageBytes\":8388608,\"billingUsageType\":\"VideoSecond\",\"billingUnit\":\"Second\",\"autoFix\":false}",
                    false)
            ])
    ];

    public static async Task EnsureAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetService<ILoggerFactory>()?
            .CreateLogger("TOOL_SERVER.Generation.ProviderCatalogBootstrapper");
        var now = DateTime.UtcNow;

        foreach (var provider in Catalog)
        {
            await EnsureProviderWithRetryAsync(
                services,
                provider,
                now,
                logger,
                cancellationToken);
        }
    }

    private static async Task EnsureProviderWithRetryAsync(
        IServiceProvider services,
        ProviderSeed seed,
        DateTime now,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= CatalogWriteMaxAttempts; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ProviderAdminDbContext>();
                if (await EnsureProviderAsync(dbContext, seed, now, cancellationToken))
                {
                    return;
                }

                logger?.LogWarning(
                    "Provider catalog row for {ProviderCode} changed while the bootstrap update was running on attempt {Attempt}. Reloading the catalog before retrying.",
                    seed.Code,
                    attempt);
            }
            catch (DbUpdateConcurrencyException exception) when (attempt < CatalogWriteMaxAttempts)
            {
                logger?.LogWarning(
                    exception,
                    "Provider catalog bootstrap concurrency conflict for {ProviderCode} on attempt {Attempt}. Reloading the catalog before retrying.",
                    seed.Code,
                    attempt);
            }
            catch (DbUpdateException exception) when (
                attempt < CatalogWriteMaxAttempts &&
                IsUniqueConstraintViolation(exception))
            {
                logger?.LogWarning(
                    exception,
                    "Provider catalog bootstrap insert conflict for {ProviderCode} on attempt {Attempt}. Reloading the catalog before retrying.",
                    seed.Code,
                    attempt);
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(CatalogWriteRetryDelayMilliseconds * attempt),
                cancellationToken);
        }

        throw new InvalidOperationException(
            $"Provider catalog bootstrap could not stabilize provider '{seed.Code}' after {CatalogWriteMaxAttempts} attempts.");
    }

    private static async Task<bool> EnsureProviderAsync(
        ProviderAdminDbContext dbContext,
        ProviderSeed seed,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var provider = await dbContext.Providers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProviderCode == seed.Code, cancellationToken);

        var defaultModalities = (await dbContext.ProviderModels
                .AsNoTracking()
                .Where(x => x.IsDefault)
                .Select(x => x.Modality)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (provider is null)
        {
            provider = new AiProvider
            {
                ProviderId = Guid.NewGuid(),
                ProviderCode = seed.Code,
                DisplayName = seed.DisplayName,
                BaseUrl = seed.BaseUrl,
                IsEnabled = seed.EnabledByDefault,
                CapabilitiesJson = seed.CapabilitiesJson,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            foreach (var modelSeed in seed.Models)
            {
                var isDefault = modelSeed.DefaultWhenDisabled ||
                                modelSeed.EnabledByDefault && defaultModalities.Add(modelSeed.Modality);
                provider.Models.Add(new AiProviderModel
                {
                    ProviderModelId = Guid.NewGuid(),
                    ProviderId = provider.ProviderId,
                    ModelCode = modelSeed.Code,
                    DisplayName = modelSeed.DisplayName,
                    Modality = modelSeed.Modality,
                    IsEnabled = modelSeed.EnabledByDefault,
                    IsDefault = isDefault,
                    CapabilitiesJson = modelSeed.CapabilitiesJson,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }

            dbContext.Providers.Add(provider);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (!string.Equals(provider.CapabilitiesJson, seed.CapabilitiesJson, StringComparison.Ordinal) &&
            await UpdateProviderCapabilitiesAsync(dbContext, provider.ProviderId, seed.CapabilitiesJson, now, cancellationToken) != 1)
        {
            return false;
        }

        var models = await dbContext.ProviderModels
            .AsNoTracking()
            .Where(x => x.ProviderId == provider.ProviderId)
            .ToListAsync(cancellationToken);

        foreach (var modelSeed in seed.Models)
        {
            var model = models.FirstOrDefault(x =>
                string.Equals(x.ModelCode, modelSeed.Code, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Modality, modelSeed.Modality, StringComparison.OrdinalIgnoreCase));
            if (model is null)
            {
                var isDefault = modelSeed.DefaultWhenDisabled ||
                                modelSeed.EnabledByDefault && defaultModalities.Add(modelSeed.Modality);
                dbContext.ProviderModels.Add(new AiProviderModel
                {
                    ProviderModelId = Guid.NewGuid(),
                    ProviderId = provider.ProviderId,
                    ModelCode = modelSeed.Code,
                    DisplayName = modelSeed.DisplayName,
                    Modality = modelSeed.Modality,
                    IsEnabled = modelSeed.EnabledByDefault,
                    IsDefault = isDefault,
                    CapabilitiesJson = modelSeed.CapabilitiesJson,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
            else if (!string.Equals(model.CapabilitiesJson, modelSeed.CapabilitiesJson, StringComparison.Ordinal) &&
                     await UpdateModelCapabilitiesAsync(dbContext, model.ProviderModelId, modelSeed.CapabilitiesJson, now, cancellationToken) != 1)
            {
                return false;
            }
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private static async Task<int> UpdateProviderCapabilitiesAsync(
        ProviderAdminDbContext dbContext,
        Guid providerId,
        string capabilitiesJson,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Capabilities are bootstrap-owned. A set-based update avoids carrying a stale
        // RowVersion while leaving admin-managed fields such as IsEnabled untouched.
        if (dbContext.Database.IsRelational())
        {
            return await dbContext.Providers
                .Where(x => x.ProviderId == providerId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.CapabilitiesJson, capabilitiesJson)
                        .SetProperty(x => x.UpdatedAtUtc, now),
                    cancellationToken);
        }

        var provider = await dbContext.Providers
            .SingleOrDefaultAsync(x => x.ProviderId == providerId, cancellationToken);
        if (provider is null)
        {
            return 0;
        }

        provider.CapabilitiesJson = capabilitiesJson;
        provider.UpdatedAtUtc = now;
        return 1;
    }

    private static async Task<int> UpdateModelCapabilitiesAsync(
        ProviderAdminDbContext dbContext,
        Guid providerModelId,
        string capabilitiesJson,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Keep existing model state/rates intact and update only bootstrap-owned metadata.
        if (dbContext.Database.IsRelational())
        {
            return await dbContext.ProviderModels
                .Where(x => x.ProviderModelId == providerModelId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.CapabilitiesJson, capabilitiesJson)
                        .SetProperty(x => x.UpdatedAtUtc, now),
                    cancellationToken);
        }

        var model = await dbContext.ProviderModels
            .SingleOrDefaultAsync(x => x.ProviderModelId == providerModelId, cancellationToken);
        if (model is null)
        {
            return 0;
        }

        model.CapabilitiesJson = capabilitiesJson;
        model.UpdatedAtUtc = now;
        return 1;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2601 or 2627 })
            {
                return true;
            }
        }

        return false;
    }

    private sealed record ProviderSeed(
        string Code,
        string DisplayName,
        string BaseUrl,
        string CapabilitiesJson,
        bool EnabledByDefault,
        IReadOnlyList<ProviderModelSeed> Models);

    private sealed record ProviderModelSeed(
        string Code,
        string DisplayName,
        string Modality,
        string CapabilitiesJson,
        bool EnabledByDefault,
        bool DefaultWhenDisabled = false);
}

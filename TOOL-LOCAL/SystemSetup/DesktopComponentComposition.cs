using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;
using TOOL_LOCAL.ModelDistribution;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Contracts.ModelDistribution;

namespace TOOL_LOCAL.SystemSetup;

internal static class DesktopComponentComposition
{
    public static bool IsLocalTranslationEnabled(DesktopFeatureOptions features) =>
        features.VietsubEnabled && features.VietsubLocalTranslationEnabled;

    public static QwenGgufVietsubTranslationProvider? CreateTranslationProvider(
        DesktopFeatureOptions features,
        Func<QwenGgufVietsubTranslationProvider> createProvider) =>
        IsLocalTranslationEnabled(features) ? createProvider() : null;

    public static VietsubVoiceComponentStore CreateVoiceComponents(
        VietsubAppPaths paths, DesktopFeatureOptions features,
        TOOL_LOCAL.ModelDistribution.DesktopModelDistributionClient? distribution = null,
        string? staticCacheRoot = null)
    {
        if (staticCacheRoot is null)
            return new(paths, features.VietsubEnabled && features.VietsubLocalVoiceEnabled,
                useUserComponentsRoot: true, distribution: distribution);
        var cached = ApprovedStaticModelBundle.DescribeCachedPayload(staticCacheRoot);
        return new(paths, features.VietsubEnabled && features.VietsubLocalVoiceEnabled,
            useUserComponentsRoot: true, requireUserComponentsRoot: true, allowUpstreamDownloads: false,
            offlineBundle: new PiperOfflineBundle(cached.PiperBundleDirectory, PiperOfflineBundle.ApprovedDefinition()),
            staticBundleSource: async (progress, token) =>
            {
                using var source = new StaticModelArchiveStore();
                var payload = await source.EnsurePayloadAsync(staticCacheRoot,
                    new SetupProgress<StaticModelSetupProgress>(p => progress?.Report(new(p.Stage,
                        p.TotalBytes > 0 ? 100d * p.BytesProcessed / p.TotalBytes : 0,
                        p.Message, p.BytesProcessed, p.TotalBytes))), token);
                return new PiperOfflineBundle(payload.PiperBundleDirectory, PiperOfflineBundle.ApprovedDefinition());
            });
    }

    internal static VietsubTranslationComponentStore CreateTranslationStore(
        string? staticCacheRoot, DesktopModelDistributionClient? distribution)
    {
        var definition = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
        if (staticCacheRoot is null) return new(definition, distribution: distribution);
        var cached = ApprovedStaticModelBundle.DescribeCachedPayload(staticCacheRoot);
        return new(definition, componentsRoot: StaticModelRuntimePolicy.TranslationRoot,
            approvedLocalModelCandidates: [cached.QwenModelPath], allowUpstreamDownload: false,
            staticModelSource: async token =>
            {
                using var source = new StaticModelArchiveStore();
                return (await source.EnsurePayloadAsync(staticCacheRoot, null, token)).QwenModelPath;
            });
    }
}

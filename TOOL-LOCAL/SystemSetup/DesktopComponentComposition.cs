using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;

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
        VietsubAppPaths paths, DesktopFeatureOptions features) =>
        new(paths, features.VietsubEnabled && features.VietsubLocalVoiceEnabled,
            useUserComponentsRoot: true);
}

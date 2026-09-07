namespace TOOL_LOCAL.Vietsub.Translation;

internal static class VietsubTranslationRuntimeStatusNames
{
    public const string Ready = "READY";
    public const string Disabled = "DISABLED";
    public const string NotInstalled = "NOT_INSTALLED";
    public const string Invalid = "INVALID";
    public const string UnsupportedHardware = "UNSUPPORTED_HARDWARE";
    public const string Busy = "BUSY";
}

internal sealed record VietsubTranslationRuntimeStatus(
    string Status,
    bool Ready,
    string? EngineId,
    string? EngineVersion,
    IReadOnlyList<string> SourceLanguages,
    bool SupportsSceneContext,
    bool SupportsReviewPass,
    string Message,
    string? ErrorCode = null,
    string? RuntimeProfileId = null,
    bool LowMemoryMode = false,
    bool RequiresResourceConfirmation = false,
    string? ResourceWarningCode = null,
    string? ResourceWarningMessage = null);

internal sealed class VietsubTranslationProviderRegistry(
    IEnumerable<IVietsubLocalTranslationProvider>? providers = null,
    bool featureEnabled = true)
{
    private const string FeatureDisabledMessage =
        "Đây không phải lỗi. Phiên bản ứng dụng này chưa mở tính năng dịch local vì engine vẫn đang được kiểm tra chất lượng và độ ổn định.";

    private readonly IReadOnlyList<IVietsubLocalTranslationProvider> _providers =
        (providers ?? []).ToArray();

    public IReadOnlyList<VietsubTranslationRuntimeStatus> GetStatuses()
    {
        if (!featureEnabled)
        {
            return
            [
                new VietsubTranslationRuntimeStatus(
                    VietsubTranslationRuntimeStatusNames.Disabled,
                    false,
                    null,
                    null,
                    [],
                    false,
                    false,
                    FeatureDisabledMessage,
                    VietsubTranslationErrorCodes.FeatureDisabled)
            ];
        }

        if (_providers.Count == 0)
        {
            return
            [
                new VietsubTranslationRuntimeStatus(
                    VietsubTranslationRuntimeStatusNames.NotInstalled,
                    false,
                    null,
                    null,
                    [],
                    false,
                    false,
                    "Chưa có engine dịch local nào được phê duyệt và cài đặt.",
                    VietsubTranslationErrorCodes.RuntimeNotInstalled)
            ];
        }

        return _providers.Select(provider => provider is IVietsubManagedLocalTranslationProvider managed
            ? managed.GetRuntimeStatus()
            : new VietsubTranslationRuntimeStatus(
                VietsubTranslationRuntimeStatusNames.Ready,
                true,
                provider.Capabilities.EngineId,
                provider.Capabilities.EngineVersion,
                provider.Capabilities.SupportedSourceLanguages,
                provider.Capabilities.SupportsSceneContext,
                provider.Capabilities.SupportsReviewPass,
                provider.Capabilities.SupportsSceneContext || provider.Capabilities.SupportsReviewPass
                    ? "Engine local khai báo hỗ trợ xử lý theo scene/review."
                    : "Engine local chỉ hỗ trợ dịch cơ bản theo câu."))
            .ToArray();
    }

    public async Task InstallDefaultAsync(
        IProgress<VietsubTranslationRuntimeInstallProgress>? progress,
        CancellationToken cancellationToken,
        bool resourceWarningAccepted = false)
    {
        EnsureFeatureEnabled();
        var provider = _providers.OfType<IVietsubManagedLocalTranslationProvider>().FirstOrDefault()
            ?? throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.RuntimeNotInstalled,
                "Bản desktop này không có component dịch local được duyệt để cài đặt.");
        await provider.InstallAsync(progress, cancellationToken, resourceWarningAccepted);
    }

    public IVietsubLocalTranslationProvider ResolveForStart(
        string sourceLanguageCode,
        string enginePolicy)
    {
        EnsureFeatureEnabled();
        var sourceLanguage = VietsubTranslationLimits.NormalizeSourceLanguage(sourceLanguageCode);
        if (_providers.Count == 0)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.RuntimeNotInstalled,
                "Chưa có engine dịch local sẵn sàng.");
        }

        var policy = VietsubTranslationEnginePolicies.Normalize(enginePolicy);
        if (policy == VietsubTranslationEnginePolicies.NotSelected)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.ModelNotReady,
                "Hãy chọn chính sách engine dịch local trước khi chạy.");
        }

        var provider = _providers.FirstOrDefault(candidate =>
            candidate.Capabilities.SupportedSourceLanguages.Contains(sourceLanguage, StringComparer.Ordinal)
            && string.Equals(candidate.Capabilities.TargetLanguageCode, "vi", StringComparison.Ordinal));
        if (provider is null)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.RuntimeNotInstalled,
                $"Chưa có engine dịch local sẵn sàng cho cặp {sourceLanguage} -> vi.");
        }

        EnsureProviderReady(provider);

        if (policy == VietsubTranslationEnginePolicies.ContextualRequired
            && !provider.Capabilities.SupportsSceneContext
            && !provider.Capabilities.SupportsReviewPass)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.ModelNotReady,
                "Engine đang cài chỉ hỗ trợ dịch cơ bản, không đạt policy dịch theo ngữ cảnh.");
        }

        return provider;
    }

    public IVietsubLocalTranslationProvider ResolveSnapshot(
        string engineId,
        string engineVersion,
        string sourceLanguageCode,
        string? runtimeProfileId = null)
    {
        EnsureFeatureEnabled();
        var sourceLanguage = VietsubTranslationLimits.NormalizeSourceLanguage(sourceLanguageCode);
        var provider = _providers.SingleOrDefault(provider =>
                string.Equals(provider.Capabilities.EngineId, engineId, StringComparison.Ordinal)
                && string.Equals(provider.Capabilities.EngineVersion, engineVersion, StringComparison.Ordinal)
                && provider.Capabilities.SupportedSourceLanguages.Contains(sourceLanguage, StringComparer.Ordinal))
            ?? throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.JobNotResumable,
                "Engine/version của translation job không còn sẵn sàng; không tự đổi model khi resume.");
        if (provider is IVietsubManagedLocalTranslationProvider managed
            && !string.IsNullOrWhiteSpace(runtimeProfileId))
        {
            if (!managed.TrySelectRuntimeProfile(runtimeProfileId))
            {
                throw new VietsubTranslationException(
                    VietsubTranslationErrorCodes.JobNotResumable,
                    "Profile tài nguyên của translation job không còn sẵn sàng; không tự đổi profile khi resume.");
            }

            var status = managed.GetRuntimeStatus(selectLowerMemoryProfile: false);
            if (!string.Equals(managed.RuntimeProfileId, runtimeProfileId, StringComparison.Ordinal))
            {
                throw new VietsubTranslationException(
                    VietsubTranslationErrorCodes.JobNotResumable,
                    "Máy hiện không còn đủ tài nguyên cho profile đã snapshot của translation job.");
            }
            if (!status.Ready)
            {
                throw new VietsubTranslationException(
                    status.ErrorCode == VietsubTranslationErrorCodes.RuntimeNotInstalled
                        ? VietsubTranslationErrorCodes.JobNotResumable
                        : status.ErrorCode ?? VietsubTranslationErrorCodes.ModelNotReady,
                    status.Message,
                    retryable: status.Status == VietsubTranslationRuntimeStatusNames.Busy);
            }

            return provider;
        }

        EnsureProviderReady(provider, resumable: true);
        return provider;
    }

    private static void EnsureProviderReady(
        IVietsubLocalTranslationProvider provider,
        bool resumable = false)
    {
        if (provider is not IVietsubManagedLocalTranslationProvider managed)
        {
            return;
        }

        var status = managed.GetRuntimeStatus();
        if (status.Ready)
        {
            return;
        }

        throw new VietsubTranslationException(
            resumable && status.ErrorCode == VietsubTranslationErrorCodes.RuntimeNotInstalled
                ? VietsubTranslationErrorCodes.JobNotResumable
                : status.ErrorCode ?? VietsubTranslationErrorCodes.ModelNotReady,
            status.Message,
            retryable: status.Status == VietsubTranslationRuntimeStatusNames.Busy);
    }

    private void EnsureFeatureEnabled()
    {
        if (featureEnabled)
        {
            return;
        }

        throw new VietsubTranslationException(
            VietsubTranslationErrorCodes.FeatureDisabled,
            FeatureDisabledMessage);
    }
}

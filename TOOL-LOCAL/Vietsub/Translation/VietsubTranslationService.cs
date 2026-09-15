using System.Text.Json;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_LOCAL.Vietsub.Translation;

internal sealed record VietsubTranslationGlossaryInput(
    Guid EntryId,
    string SourceText,
    string TargetText,
    string? Note);

internal sealed record VietsubTranslationSettingsInput(
    string SourceLanguageCode,
    string TargetLanguageCode,
    string EnginePolicy,
    int ContextCueCount,
    int SceneMaximumTargetCues,
    int SceneGapMilliseconds,
    double MaximumCharactersPerSecond,
    string ContextSummary,
    string CharacterInstructions,
    string StyleInstructions,
    IReadOnlyList<VietsubTranslationGlossaryInput> Glossary);

internal sealed record VietsubStartTranslationInput(
    string RunMode,
    Guid ExpectedTrackId,
    int ExpectedTrackRevision,
    bool ConfirmResourceWarning = false);

internal sealed record VietsubInstallTranslationRuntimeInput(
    bool ConfirmResourceWarning = false);

internal sealed record VietsubTranslationSettingsSnapshot(
    string SourceLanguageCode,
    string TargetLanguageCode,
    string EnginePolicy,
    int ContextCueCount,
    int SceneMaximumTargetCues,
    int SceneGapMilliseconds,
    double MaximumCharactersPerSecond,
    string ContextSummary,
    string CharacterInstructions,
    string StyleInstructions,
    IReadOnlyList<VietsubTranslationGlossaryEntry> Glossary);

internal sealed record VietsubTranslationJobParameters(
    int StrategyVersion,
    string RunMode,
    Guid InputTrackId,
    int InputRevision,
    string EngineId,
    string EngineVersion,
    string ConfigurationFingerprint,
    VietsubTranslationSettingsSnapshot Settings,
    string? RuntimeProfileId = null,
    bool ResourceWarningAccepted = false)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static VietsubTranslationJobParameters Parse(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<VietsubTranslationJobParameters>(json, JsonOptions)
                ?? throw new JsonException("Translation job parameters rỗng.");
            if (value.StrategyVersion is not (1 or 2 or 3)
                || value.InputTrackId == Guid.Empty
                || value.InputRevision < 1
                || string.IsNullOrWhiteSpace(value.EngineId)
                || string.IsNullOrWhiteSpace(value.EngineVersion)
                || value.Settings is null)
            {
                throw new JsonException("Translation job parameters không hợp lệ.");
            }
            _ = VietsubTranslationLimits.NormalizeSourceLanguage(value.Settings.SourceLanguageCode);
            _ = VietsubTranslationLimits.NormalizeTargetLanguage(value.Settings.TargetLanguageCode);
            _ = VietsubTranslationRunModes.Normalize(value.RunMode);
            if (value.ConfigurationFingerprint.Length != 64
                || !value.ConfigurationFingerprint.All(Uri.IsHexDigit))
            {
                throw new JsonException("Configuration fingerprint không hợp lệ.");
            }
            if (value.StrategyVersion >= 2
                && value.RuntimeProfileId is not (
                    VietsubTranslationWorkerProfiles.StandardProfileId
                    or VietsubTranslationWorkerProfiles.LowMemoryProfileId))
            {
                throw new JsonException("Runtime profile của translation job không hợp lệ.");
            }

            return value.StrategyVersion switch
            {
                1 => value with
                {
                    RuntimeProfileId = VietsubTranslationWorkerProfiles.StandardProfileId,
                    ResourceWarningAccepted = false
                },
                2 => value with { ResourceWarningAccepted = false },
                _ => value
            };
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.JobNotResumable,
                "Metadata translation job không thể phục hồi.",
                innerException: exception);
        }
    }
}

internal sealed class VietsubTranslationService(
    IVietsubLocalJobAuthorizer authorizer,
    VietsubSubtitleStore subtitleStore,
    VietsubTranslationProviderRegistry providerRegistry,
    VietsubJobManager jobManager)
{
    private const string OcrSubtitleTrackSource = "PADDLE_OCR_LOCAL";
    internal TOOL_LOCAL.SystemSetup.SystemSetupCoordinator? SetupCoordinator { get; set; }
    private const string OcrRequiredMessage = "Bạn cần quét OCR nhận dạng phụ đề trước khi dịch.";

    public IReadOnlyList<VietsubTranslationRuntimeStatus> GetRuntimeStatuses() =>
        providerRegistry.GetStatuses();

    public VietsubTranslationRuntimeStatus GetRuntimeStatus()
    {
        var statuses = GetRuntimeStatuses();
        return statuses.FirstOrDefault(status => status.Ready) ?? statuses.First();
    }

    public async Task<VietsubTranslationRuntimeStatus> InstallRuntimeAsync(
        VietsubProjectSession session,
        string userId,
        Guid organizationId,
        VietsubInstallTranslationRuntimeInput input,
        IProgress<VietsubTranslationRuntimeInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        await AuthorizeAsync(userId, organizationId, session.Manifest, cancellationToken);
        if (SetupCoordinator is { } setup)
        {
            try
            {
                await setup.PrepareLegacyAsync("qwen", input.ConfirmResourceWarning,
                    p => progress?.Report(new(p.Stage ?? "VERIFY", p.Percent ?? 0, "Đang chuẩn bị engine dịch local.",
                        p.BytesProcessed ?? 0, p.TotalBytes ?? 0)), cancellationToken);
                return GetRuntimeStatus();
            }
            catch (TOOL_LOCAL.SystemSetup.SetupException e) { throw new VietsubTranslationException(e.Code, e.Message); }
        }
        var status = GetRuntimeStatus();
        var resourceWarningAccepted = ResolveResourceWarningAcceptance(
            status,
            input.ConfirmResourceWarning);
        try
        {
            using var runtimeLease = TOOL_LOCAL.SystemSetup.RuntimeUseGate.Shared.Acquire(exclusive: true);
            status = GetRuntimeStatus();
            resourceWarningAccepted = ResolveResourceWarningAcceptance(
                status,
                input.ConfirmResourceWarning);
            await providerRegistry.InstallDefaultAsync(
                progress,
                cancellationToken,
                resourceWarningAccepted);
            return GetRuntimeStatus();
        }
        catch (TOOL_LOCAL.SystemSetup.SetupException exception)
        {
            throw new VietsubTranslationException(exception.Code, exception.Message);
        }
    }

    public async Task<VietsubTranslationSettings> UpdateSettingsAsync(
        VietsubProjectSession session,
        string userId,
        Guid organizationId,
        VietsubTranslationSettingsInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        await AuthorizeAsync(userId, organizationId, session.Manifest, cancellationToken);
        var settings = NormalizeSettings(input, session.Manifest);
        await session.UpdateAsync(manifest =>
        {
            manifest.TranslationSettings = settings;
            manifest.SourceLanguageCode = settings.SourceLanguageCode;
            manifest.TargetLanguageCode = "vi";
        }, cancellationToken);
        await session.FlushAsync(cancellationToken);
        return settings;
    }

    public async Task<VietsubJobSummary> StartAsync(
        VietsubProjectSession session,
        string userId,
        Guid organizationId,
        VietsubStartTranslationInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var project = session.Manifest;
        await AuthorizeAsync(userId, organizationId, project, cancellationToken);
        var runMode = VietsubTranslationRunModes.NormalizeRequired(input.RunMode);
        if (input.ExpectedTrackId == Guid.Empty || input.ExpectedTrackRevision < 1)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.SourceTrackRequired,
                OcrRequiredMessage);
        }

        var tracks = await subtitleStore.LoadTracksAsync(project.ProjectId, cancellationToken);
        var track = tracks.SingleOrDefault(candidate => candidate.TrackId == input.ExpectedTrackId);
        if (track is null
            || project.ActiveSubtitleTrackId != input.ExpectedTrackId
            || !string.Equals(track.Source, OcrSubtitleTrackSource, StringComparison.Ordinal)
            || track.Cues.Count == 0)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.SourceTrackRequired,
                OcrRequiredMessage);
        }
        if (track.Revision != input.ExpectedTrackRevision)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.TrackChanged,
                "Phụ đề nguồn đã thay đổi. Hãy tải lại rồi dịch lại.");
        }

        var trackLanguage = VietsubTranslationLimits.NormalizeSourceLanguage(track.LanguageCode);
        var settings = CreateEffectiveSettings(project.TranslationSettings, trackLanguage);

        var provider = providerRegistry.ResolveForStart(settings.SourceLanguageCode, settings.EnginePolicy);
        var capabilities = provider.Capabilities;
        var runtimeProfileId = provider is IVietsubManagedLocalTranslationProvider managed
            ? managed.RuntimeProfileId
            : null;
        var resourceWarningAccepted = provider is IVietsubManagedLocalTranslationProvider managedProvider
            ? ResolveResourceWarningAcceptance(
                managedProvider.GetRuntimeStatus(selectLowerMemoryProfile: false),
                input.ConfirmResourceWarning)
            : false;
        var snapshot = CreateSnapshot(settings, capabilities);
        var configurationFingerprint = VietsubTranslationFingerprintBuilder.BuildConfigurationFingerprint(
            new VietsubTranslationConfigurationSnapshot(
                snapshot.SourceLanguageCode,
                snapshot.TargetLanguageCode,
                capabilities.EngineId,
                capabilities.EngineVersion,
                snapshot.MaximumCharactersPerSecond,
                snapshot.ContextSummary,
                snapshot.CharacterInstructions,
                snapshot.StyleInstructions,
                snapshot.Glossary,
                "project-memory-v1",
                RuntimeProfileId: runtimeProfileId));
        var parameters = new VietsubTranslationJobParameters(
            runtimeProfileId is null ? 1 : 3,
            runMode,
            track.TrackId,
            track.Revision,
            capabilities.EngineId,
            capabilities.EngineVersion,
            configurationFingerprint,
            snapshot,
            runtimeProfileId,
            resourceWarningAccepted);

        VietsubJobSummary job;
        try
        {
            job = await jobManager.EnqueueAsync(
                project.ProjectId,
                VietsubJobTypes.TranslateLocal,
                ["TRANSLATION_PREPARE", "TRANSLATION_EXECUTE", "TRANSLATION_WRITE_ARTIFACT"],
                parameters.ToJson(),
                track.TrackId,
                track.Revision,
                maxAttempts: 3,
                startImmediately: false,
                cancellationToken: cancellationToken);
        }
        catch (VietsubJobException exception) when (exception.Code == "vietsub_job_already_active")
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.JobConflict,
                "Project đã có một local job đang chờ, chạy hoặc tạm dừng.",
                innerException: exception);
        }

        try
        {
            await session.UpdateAsync(manifest => manifest.Status = VietsubProjectStatuses.Processing, cancellationToken);
            await session.FlushAsync(cancellationToken);
            await jobManager.StartAsync(project.ProjectId, job.Id, cancellationToken);
            return job;
        }
        catch
        {
            try
            {
                await jobManager.CancelAsync(project.ProjectId, job.Id, CancellationToken.None);
            }
            catch (Exception)
            {
            }
            try
            {
                await session.UpdateAsync(
                    manifest => manifest.Status = VietsubProjectStatuses.Ready,
                    CancellationToken.None);
                await session.FlushAsync(CancellationToken.None);
            }
            catch (Exception)
            {
            }
            throw;
        }
    }

    private static VietsubTranslationSettings NormalizeSettings(
        VietsubTranslationSettingsInput input,
        VietsubProjectManifest project)
    {
        var settings = new VietsubTranslationSettings
        {
            SourceLanguageCode = input.SourceLanguageCode,
            TargetLanguageCode = input.TargetLanguageCode,
            EnginePolicy = input.EnginePolicy,
            ContextCueCount = input.ContextCueCount,
            SceneMaximumTargetCues = input.SceneMaximumTargetCues,
            SceneGapMilliseconds = input.SceneGapMilliseconds,
            MaximumCharactersPerSecond = input.MaximumCharactersPerSecond,
            ContextSummary = input.ContextSummary,
            CharacterInstructions = input.CharacterInstructions,
            StyleInstructions = input.StyleInstructions,
            Glossary = (input.Glossary ?? []).Select(entry => new VietsubTranslationGlossarySetting
            {
                EntryId = entry.EntryId == Guid.Empty ? Guid.NewGuid() : entry.EntryId,
                SourceText = entry.SourceText,
                TargetText = entry.TargetText,
                Note = entry.Note
            }).ToList()
        };
        settings.Normalize(project.SourceLanguageCode, project.TargetLanguageCode);
        if (string.IsNullOrWhiteSpace(settings.SourceLanguageCode))
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.SourceLanguageRequired,
                "Hãy chọn tiếng Anh hoặc tiếng Trung làm ngôn ngữ nguồn.");
        }
        return settings;
    }

    private static VietsubTranslationSettings CreateEffectiveSettings(
        VietsubTranslationSettings? configured,
        string trackLanguage)
    {
        configured ??= new VietsubTranslationSettings();
        var settings = new VietsubTranslationSettings
        {
            SourceLanguageCode = trackLanguage,
            TargetLanguageCode = "vi",
            EnginePolicy = VietsubTranslationEnginePolicies.Normalize(configured.EnginePolicy)
                == VietsubTranslationEnginePolicies.NotSelected
                    ? VietsubTranslationEnginePolicies.ContextualRequired
                    : configured.EnginePolicy,
            ContextCueCount = configured.ContextCueCount,
            SceneMaximumTargetCues = configured.SceneMaximumTargetCues,
            SceneGapMilliseconds = configured.SceneGapMilliseconds,
            MaximumCharactersPerSecond = configured.MaximumCharactersPerSecond,
            ContextSummary = configured.ContextSummary,
            CharacterInstructions = configured.CharacterInstructions,
            StyleInstructions = configured.StyleInstructions,
            Glossary = (configured.Glossary ?? []).Select(entry => new VietsubTranslationGlossarySetting
            {
                EntryId = entry.EntryId,
                SourceText = entry.SourceText,
                TargetText = entry.TargetText,
                Note = entry.Note
            }).ToList()
        };
        settings.Normalize(trackLanguage, "vi");
        return settings;
    }

    private static VietsubTranslationSettingsSnapshot CreateSnapshot(
        VietsubTranslationSettings settings,
        VietsubLocalTranslationCapabilities capabilities) => new(
            settings.SourceLanguageCode,
            settings.TargetLanguageCode,
            settings.EnginePolicy,
            VietsubTranslationLimits.ResolveContextCueCount(settings.ContextCueCount, capabilities),
            VietsubTranslationLimits.ResolveMaximumTargetCues(settings.SceneMaximumTargetCues, capabilities),
            settings.SceneGapMilliseconds,
            settings.MaximumCharactersPerSecond,
            settings.ContextSummary,
            settings.CharacterInstructions,
            settings.StyleInstructions,
            settings.Glossary.Select(entry => new VietsubTranslationGlossaryEntry(
                entry.EntryId,
                entry.SourceText,
                entry.TargetText,
                entry.Note)).ToArray());

    private static bool ResolveResourceWarningAcceptance(
        VietsubTranslationRuntimeStatus status,
        bool confirmed)
    {
        if (status.ErrorCode == VietsubTranslationErrorCodes.RuntimeUnsupportedPlatform)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.RuntimeUnsupportedPlatform,
                status.Message);
        }

        if (!status.RequiresResourceConfirmation)
        {
            // A successful native preflight also admits the operation if RAM changes
            // between job creation and worker startup. Hard blockers remain non-overridable.
            return true;
        }

        if (!confirmed)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.ResourceConfirmationRequired,
                status.ResourceWarningMessage
                    ?? "Tài nguyên RAM hiện thấp hơn mức khuyến nghị. Hãy xác nhận nếu bạn vẫn muốn tiếp tục.");
        }

        return true;
    }

    private async Task AuthorizeAsync(
        string userId,
        Guid organizationId,
        VietsubProjectManifest project,
        CancellationToken cancellationToken)
    {
        try
        {
            await authorizer.AuthorizeAsync(userId, organizationId, project, cancellationToken);
        }
        catch (VietsubLocalJobAuthorizationException exception)
        {
            throw new VietsubTranslationException(
                exception.Code == VietsubLocalJobAuthorizationErrorCodes.LicenseRequired
                    ? VietsubTranslationErrorCodes.LicenseRequired
                    : VietsubTranslationErrorCodes.AccessDenied,
                exception.Message,
                innerException: exception);
        }
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;

namespace TOOL_LOCAL.Vietsub.Translation;

internal sealed record VietsubTranslationCheckpoint(
    int StrategyVersion,
    int LastCompletedScene,
    int TotalItems,
    int CompletedItems,
    int ReviewItems,
    int InvalidItems,
    int StaleItems,
    int FailedItems,
    int MemoryHits,
    int CacheHits,
    int ProviderCalls);

internal sealed class VietsubTranslationJobExecutor(
    VietsubProjectStore projectStore,
    VietsubSubtitleStore subtitleStore,
    VietsubTranslationStore translationStore,
    VietsubTranslationProviderRegistry providerRegistry,
    VietsubJobStore jobStore,
    VietsubAppPaths paths) : IVietsubJobExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string JobType => VietsubJobTypes.TranslateLocal;

    public async Task ExecuteAsync(
        VietsubJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteCoreAsync(context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (VietsubTranslationException exception)
        {
            throw new VietsubJobExecutionException(
                exception.Code,
                exception.Message,
                exception.Retryable,
                exception);
        }
    }

    private async Task ExecuteCoreAsync(
        VietsubJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var parameters = VietsubTranslationJobParameters.Parse(context.Job.ParametersJson);
        var provider = providerRegistry.ResolveSnapshot(
            parameters.EngineId,
            parameters.EngineVersion,
            parameters.Settings.SourceLanguageCode,
            parameters.RuntimeProfileId);
        await context.ReportProgressAsync(
            new VietsubJobProgressUpdate(
                "TRANSLATION_PREPARE",
                15,
                1,
                "Đang kiểm tra track, fingerprint và checkpoint dịch local."),
            cancellationToken);

        var project = await projectStore.LoadForBackgroundJobAsync(context.Job.ProjectId, cancellationToken);
        var tracks = await subtitleStore.LoadTracksAsync(project.ProjectId, cancellationToken);
        var track = tracks.SingleOrDefault(candidate => candidate.TrackId == parameters.InputTrackId)
            ?? throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.JobNotResumable,
                "Input track đã snapshot của translation job không còn tồn tại.");
        var existingItems = await translationStore.LoadJobItemsAsync(
            project.ProjectId,
            context.Job.Id,
            cancellationToken);
        if (existingItems.Count == 0 && track.Revision != parameters.InputRevision)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.TrackChanged,
                "Input track đã thay đổi trước khi translation job bắt đầu.");
        }

        EnsureConfigurationFingerprint(parameters);
        var orderedCues = track.Cues
            .OrderBy(cue => cue.StartMilliseconds)
            .ThenBy(cue => cue.EndMilliseconds)
            .ToArray();
        var currentFingerprints = orderedCues
            .Select((cue, index) => new
            {
                cue.CueId,
                Fingerprint = VietsubTranslationFingerprintBuilder.BuildCueFingerprint(
                    cue,
                    index,
                    orderedCues,
                    parameters.Settings.ContextCueCount,
                    parameters.ConfigurationFingerprint)
            })
            .ToDictionary(item => item.CueId, item => item.Fingerprint);
        Dictionary<Guid, string> fingerprints;
        HashSet<Guid> targetCueIds;
        var resumeGuards = new List<(VietsubTranslationJobItem Item, string Status)>();
        var totalItems = existingItems.Count;
        if (existingItems.Count == 0)
        {
            fingerprints = currentFingerprints;
            targetCueIds = await SelectTargetsAsync(
                project.ProjectId,
                track,
                parameters.RunMode,
                fingerprints,
                cancellationToken);
        }
        else
        {
            fingerprints = existingItems.ToDictionary(item => item.CueId, item => item.InputFingerprint);
            var cuesById = track.Cues.ToDictionary(cue => cue.CueId);
            targetCueIds = existingItems
                .Where(item => item.Status is not (
                    VietsubTranslationJobItemStatuses.Completed
                    or VietsubTranslationJobItemStatuses.Review
                    or VietsubTranslationJobItemStatuses.Stale
                    or VietsubTranslationJobItemStatuses.SkippedLocked))
                .Select(item => item.CueId)
                .ToHashSet();
            foreach (var item in existingItems.Where(item => targetCueIds.Contains(item.CueId)))
            {
                if (!cuesById.TryGetValue(item.CueId, out var cue)
                    || !currentFingerprints.TryGetValue(item.CueId, out var currentFingerprint)
                    || !string.Equals(currentFingerprint, item.InputFingerprint, StringComparison.Ordinal))
                {
                    targetCueIds.Remove(item.CueId);
                    resumeGuards.Add((item, VietsubTranslationJobItemStatuses.Stale));
                }
                else if (cue.TranslationLocked || cue.TranslationSource == VietsubTranslationSources.Manual)
                {
                    targetCueIds.Remove(item.CueId);
                    resumeGuards.Add((item, VietsubTranslationJobItemStatuses.SkippedLocked));
                }
            }
        }

        var scenes = VietsubTranslationScenePlanner.Plan(
            track.Cues,
            targetCueIds,
            VietsubTranslationLimits.ResolveMaximumTargetCues(
                parameters.Settings.SceneMaximumTargetCues,
                provider.Capabilities),
            VietsubTranslationLimits.ResolveContextCueCount(
                parameters.Settings.ContextCueCount,
                provider.Capabilities),
            parameters.Settings.SceneGapMilliseconds,
            parameters.Settings.MaximumCharactersPerSecond,
            maximumSceneSourceCharacters: VietsubTranslationLimits.ResolveMaximumSourceCharacters(
                VietsubTranslationLimits.DefaultMaximumSceneSourceCharacters,
                provider.Capabilities));
        var seeds = scenes.SelectMany(scene => scene.TargetCueIds.Select(cueId =>
            new VietsubTranslationJobItemSeed(
                cueId,
                scene.SceneNumber,
                scene.ChapterNumber,
                fingerprints[cueId]))).ToArray();
        if (existingItems.Count == 0)
        {
            await translationStore.UpsertJobItemsAsync(project.ProjectId, context.Job.Id, seeds, cancellationToken);
            totalItems = seeds.Length;
        }

        var state = RestoreCheckpoint(context.Job.CheckpointJson, totalItems);
        foreach (var (item, status) in resumeGuards)
        {
            state = state with { StaleItems = state.StaleItems + 1 };
            await translationStore.UpdateJobItemAsync(
                project.ProjectId,
                context.Job.Id,
                item.CueId,
                item.InputFingerprint,
                status,
                JsonSerializer.Serialize(state, JsonOptions),
                errorCode: VietsubTranslationErrorCodes.TrackChanged,
                cancellationToken: cancellationToken);
        }

        await context.ReportProgressAsync(
            new VietsubJobProgressUpdate(
                "TRANSLATION_PREPARE",
                100,
                5,
                $"Đã lập {scenes.Count} scene cho {totalItems} cue."),
            cancellationToken);

        foreach (var scene in scenes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var persistedItems = (await translationStore.LoadJobItemsAsync(
                    project.ProjectId,
                    context.Job.Id,
                    cancellationToken))
                .ToDictionary(item => item.CueId);
            var pendingAliases = scene.Cues
                .Where(cue => cue.IsTarget)
                .Where(cue => !persistedItems.TryGetValue(cue.CueId, out var item)
                    || item.InputFingerprint != fingerprints[cue.CueId]
                    || item.Status is not (VietsubTranslationJobItemStatuses.Completed
                        or VietsubTranslationJobItemStatuses.Review
                        or VietsubTranslationJobItemStatuses.SkippedLocked))
                .Select(cue => cue.CueAlias)
                .ToHashSet(StringComparer.Ordinal);
            if (pendingAliases.Count == 0)
            {
                state = state with { LastCompletedScene = Math.Max(state.LastCompletedScene, scene.SceneNumber) };
                await context.SaveCheckpointAsync(
                    JsonSerializer.Serialize(state, JsonOptions),
                    cancellationToken);
                continue;
            }

            var results = new List<VietsubTranslationItemResult>();
            var unresolvedAliases = new HashSet<string>(pendingAliases, StringComparer.Ordinal);
            foreach (var input in scene.Cues.Where(cue => cue.IsTarget && pendingAliases.Contains(cue.CueAlias)))
            {
                var memory = await translationStore.FindApprovedMemoryAsync(
                    project.ProjectId,
                    parameters.Settings.SourceLanguageCode,
                    parameters.Settings.TargetLanguageCode,
                    input.OriginalText,
                    fingerprints[input.CueId],
                    cancellationToken);
                if (memory is null)
                {
                    continue;
                }

                results.Add(new VietsubTranslationItemResult(
                    input.CueAlias,
                    memory.TranslatedText,
                    null,
                    []));
                unresolvedAliases.Remove(input.CueAlias);
                await translationStore.MarkMemoryUsedAsync(project.ProjectId, memory.EntryId, cancellationToken);
                state = state with { MemoryHits = state.MemoryHits + 1 };
            }

            if (unresolvedAliases.Count > 0)
            {
                var providerInputs = scene.Cues
                    .Select(cue => cue with { IsTarget = unresolvedAliases.Contains(cue.CueAlias) })
                    .ToArray();
                var request = new VietsubTranslationSceneRequest(
                    project.Name,
                    parameters.Settings.SourceLanguageCode,
                    parameters.Settings.TargetLanguageCode,
                    parameters.Settings.ContextSummary,
                    parameters.Settings.CharacterInstructions,
                    parameters.Settings.StyleInstructions,
                    parameters.Settings.Glossary,
                    [],
                    providerInputs,
                    VietsubTranslationPass.Translate,
                    VietsubTranslationScenePlanner.BuildChapterContext(scene, track.Cues),
                    parameters.ConfigurationFingerprint,
                    parameters.ResourceWarningAccepted);
                var expectedAliases = request.Cues
                    .Where(cue => cue.IsTarget)
                    .Select(cue => cue.CueAlias)
                    .ToArray();
                var sceneInputFingerprint = BuildSceneInputFingerprint(
                    expectedAliases.Select(alias =>
                    {
                        var cueId = request.Cues.Single(cue => cue.CueAlias == alias).CueId;
                        return fingerprints[cueId];
                    }));
                VietsubTranslationSceneResult? providerResult = null;
                if (parameters.RunMode != VietsubTranslationRunModes.RestartUnlocked)
                {
                    var cacheKey = VietsubTranslationStore.BuildCacheKey(
                        parameters.EngineId,
                        parameters.EngineVersion,
                        parameters.ConfigurationFingerprint,
                        sceneInputFingerprint);
                    var cache = await translationStore.TryGetCacheAsync(project.ProjectId, cacheKey, cancellationToken);
                    if (cache is not null)
                    {
                        try
                        {
                            providerResult = JsonSerializer.Deserialize<VietsubTranslationSceneResult>(
                                cache.ResultJson,
                                JsonOptions);
                            if (providerResult is not null)
                            {
                                EnsureProviderSnapshot(providerResult, parameters);
                                VietsubTranslationResultValidator.EnsureValid(providerResult, expectedAliases);
                                state = state with { CacheHits = state.CacheHits + expectedAliases.Length };
                            }
                        }
                        catch (Exception exception) when (exception is JsonException or VietsubTranslationException)
                        {
                            providerResult = null;
                        }
                    }
                }

                if (providerResult is null)
                {
                    foreach (var input in request.Cues.Where(cue => cue.IsTarget))
                    {
                        await translationStore.UpdateJobItemAsync(
                            project.ProjectId,
                            context.Job.Id,
                            input.CueId,
                            fingerprints[input.CueId],
                            VietsubTranslationJobItemStatuses.Running,
                            JsonSerializer.Serialize(state, JsonOptions),
                            cancellationToken: cancellationToken);
                    }
                    try
                    {
                        providerResult = await provider.TranslateAsync(request, cancellationToken);
                        EnsureProviderSnapshot(providerResult, parameters);
                        VietsubTranslationResultValidator.EnsureValid(providerResult, expectedAliases);
                        await translationStore.SaveCacheAsync(
                            project.ProjectId,
                            parameters.EngineId,
                            parameters.EngineVersion,
                            parameters.ConfigurationFingerprint,
                            sceneInputFingerprint,
                            JsonSerializer.Serialize(providerResult, JsonOptions),
                            cancellationToken);
                        state = state with { ProviderCalls = state.ProviderCalls + 1 };
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        var invalidResult = exception is VietsubTranslationException translationException
                            && translationException.Code == VietsubTranslationErrorCodes.ResultInvalid;
                        var itemErrorCode = exception is VietsubTranslationException knownException
                            ? knownException.Code
                            : VietsubTranslationErrorCodes.ProcessFailed;
                        foreach (var input in request.Cues.Where(cue => cue.IsTarget))
                        {
                            state = invalidResult
                                ? state with { InvalidItems = state.InvalidItems + 1 }
                                : state with { FailedItems = state.FailedItems + 1 };
                            await translationStore.UpdateJobItemAsync(
                                project.ProjectId,
                                context.Job.Id,
                                input.CueId,
                                fingerprints[input.CueId],
                                invalidResult
                                    ? VietsubTranslationJobItemStatuses.Invalid
                                    : VietsubTranslationJobItemStatuses.Failed,
                                JsonSerializer.Serialize(state, JsonOptions),
                                errorCode: invalidResult
                                    ? VietsubTranslationErrorCodes.ResultInvalid
                                    : itemErrorCode,
                                cancellationToken: CancellationToken.None);
                        }

                        if (exception is VietsubTranslationException known)
                        {
                            throw known;
                        }
                        throw new VietsubTranslationException(
                            VietsubTranslationErrorCodes.ProcessFailed,
                            "Engine dịch local gặp lỗi khi xử lý scene.",
                            retryable: true,
                            innerException: exception);
                    }
                }
                results.AddRange(providerResult.Items);
            }

            foreach (var item in results.OrderBy(result =>
                         scene.Cues.Single(cue => cue.CueAlias == result.CueAlias).StartMilliseconds))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var input = scene.Cues.Single(cue => cue.CueAlias == item.CueAlias);
                var sourceCue = track.Cues.Single(cue => cue.CueId == input.CueId);
                var currentCue = await LoadCurrentCueForApplyAsync(
                    project.ProjectId,
                    parameters.InputTrackId,
                    sourceCue.CueId,
                    parameters.Settings.ContextCueCount,
                    parameters.ConfigurationFingerprint,
                    cancellationToken);
                if (currentCue.Cue.TranslationLocked
                    || currentCue.Cue.TranslationSource == VietsubTranslationSources.Manual
                    || !string.Equals(
                        currentCue.Fingerprint,
                        fingerprints[sourceCue.CueId],
                        StringComparison.Ordinal))
                {
                    state = state with { StaleItems = state.StaleItems + 1 };
                    await translationStore.UpdateJobItemAsync(
                        project.ProjectId,
                        context.Job.Id,
                        sourceCue.CueId,
                        fingerprints[sourceCue.CueId],
                        VietsubTranslationJobItemStatuses.Stale,
                        JsonSerializer.Serialize(state, JsonOptions),
                        errorCode: VietsubTranslationErrorCodes.TrackChanged,
                        cancellationToken: cancellationToken);
                    continue;
                }

                var assessment = VietsubTranslationQualityValidator.Assess(
                    currentCue.Cue.OriginalText,
                    item.TranslatedText,
                    currentCue.Cue.EndMilliseconds - currentCue.Cue.StartMilliseconds,
                    parameters.Settings.Glossary,
                    parameters.Settings.MaximumCharactersPerSecond,
                    item.Confidence,
                    item.Warnings,
                    parameters.Settings.SourceLanguageCode,
                    input.SuggestedMaximumCharacters);
                if (!assessment.IsValid)
                {
                    state = state with { InvalidItems = state.InvalidItems + 1 };
                    await translationStore.UpdateJobItemAsync(
                        project.ProjectId,
                        context.Job.Id,
                        sourceCue.CueId,
                        fingerprints[sourceCue.CueId],
                        VietsubTranslationJobItemStatuses.Invalid,
                        JsonSerializer.Serialize(state, JsonOptions),
                        item.TranslatedText,
                        item.Confidence,
                        assessment.Warnings,
                        assessment.FailureCode,
                        cancellationToken);
                    continue;
                }

                var qualityStatus = assessment.Warnings.Count == 0
                    ? VietsubTranslationQualityStatuses.Valid
                    : VietsubTranslationQualityStatuses.Review;
                var committedState = qualityStatus == VietsubTranslationQualityStatuses.Review
                    ? state with { ReviewItems = state.ReviewItems + 1 }
                    : state with { CompletedItems = state.CompletedItems + 1 };
                var committed = await translationStore.TryCommitCueResultAsync(
                    project.ProjectId,
                    context.Job.Id,
                    new VietsubTranslationCueCommit(
                        sourceCue.CueId,
                        currentCue.Cue.UpdatedAtUtc,
                        currentCue.Cue.OriginalText,
                        currentCue.Cue.StartMilliseconds,
                        currentCue.Cue.EndMilliseconds,
                        currentCue.Cue.Speaker,
                        fingerprints[sourceCue.CueId],
                        item.TranslatedText,
                        qualityStatus,
                        item.Confidence,
                        assessment.Warnings,
                        parameters.EngineId,
                        parameters.EngineVersion),
                    JsonSerializer.Serialize(committedState, JsonOptions),
                    cancellationToken);
                if (!committed)
                {
                    state = state with { StaleItems = state.StaleItems + 1 };
                    await translationStore.UpdateJobItemAsync(
                        project.ProjectId,
                        context.Job.Id,
                        sourceCue.CueId,
                        fingerprints[sourceCue.CueId],
                        VietsubTranslationJobItemStatuses.Stale,
                        JsonSerializer.Serialize(state, JsonOptions),
                        errorCode: VietsubTranslationErrorCodes.TrackChanged,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    state = committedState;
                }
            }

            state = state with { LastCompletedScene = scene.SceneNumber };
            await context.SaveCheckpointAsync(
                JsonSerializer.Serialize(state, JsonOptions),
                cancellationToken);
            var processed = state.CompletedItems + state.ReviewItems + state.InvalidItems + state.StaleItems;
            var progress = totalItems == 0 ? 95 : 5 + 85d * processed / totalItems;
            await context.ReportProgressAsync(
                new VietsubJobProgressUpdate(
                    "TRANSLATION_EXECUTE",
                    totalItems == 0 ? 100 : 100d * processed / totalItems,
                    Math.Clamp(progress, 5, 90),
                    $"Đã xử lý {processed}/{totalItems} cue dịch local.",
                    JsonSerializer.Serialize(state, JsonOptions)),
                cancellationToken);
        }

        await context.ReportProgressAsync(
            new VietsubJobProgressUpdate(
                "TRANSLATION_WRITE_ARTIFACT",
                20,
                92,
                "Đang ghi SRT tiếng Việt."),
            cancellationToken);
        var finalTrack = (await subtitleStore.LoadTracksAsync(project.ProjectId, cancellationToken))
            .Single(candidate => candidate.TrackId == parameters.InputTrackId);
        await VietsubTranslatedArtifactWriter.WriteAsync(paths, subtitleStore, project.ProjectId, finalTrack, cancellationToken);
        await jobStore.BindOutputTrackAsync(project.ProjectId, context.Job.Id, finalTrack.TrackId, cancellationToken);
        stopwatch.Stop();
        var metrics = JsonSerializer.Serialize(new
        {
            totalItems,
            state.CompletedItems,
            state.ReviewItems,
            state.InvalidItems,
            state.StaleItems,
            state.MemoryHits,
            state.CacheHits,
            state.ProviderCalls,
            engineId = parameters.EngineId,
            engineVersion = parameters.EngineVersion,
            elapsedMilliseconds = stopwatch.ElapsedMilliseconds
        }, JsonOptions);
        await context.ReportProgressAsync(
            new VietsubJobProgressUpdate(
                "TRANSLATION_WRITE_ARTIFACT",
                100,
                100,
                "Đã dịch và ghi SRT tiếng Việt.",
                JsonSerializer.Serialize(state, JsonOptions),
                metrics),
            cancellationToken);
    }

    private async Task<HashSet<Guid>> SelectTargetsAsync(
        Guid projectId,
        VietsubSubtitleTrack track,
        string runMode,
        IReadOnlyDictionary<Guid, string> fingerprints,
        CancellationToken cancellationToken)
    {
        var normalizedMode = VietsubTranslationRunModes.Normalize(runMode);
        IReadOnlyDictionary<Guid, string> latestStatuses = new Dictionary<Guid, string>();
        if (normalizedMode == VietsubTranslationRunModes.RetryFailed)
        {
            latestStatuses = await translationStore.LoadLatestItemStatusesAsync(
                projectId,
                track.TrackId,
                cancellationToken);
        }

        return track.Cues.Where(cue =>
            !cue.TranslationLocked
            && cue.TranslationSource != VietsubTranslationSources.Manual
            && normalizedMode switch
            {
                VietsubTranslationRunModes.RestartUnlocked => true,
                VietsubTranslationRunModes.RetryFailed => latestStatuses.TryGetValue(cue.CueId, out var status)
                    && status is VietsubTranslationJobItemStatuses.Failed or VietsubTranslationJobItemStatuses.Invalid,
                _ => string.IsNullOrWhiteSpace(cue.TranslatedText)
                    || (cue.TranslationSource == VietsubTranslationSources.CloudAuto
                        ? cue.TranslationSourceFingerprint != VietsubCloudTranslationService.Fingerprint(track.TrackId, cue)
                        : cue.TranslationSource != VietsubTranslationSources.LocalAuto
                            || cue.TranslationSourceFingerprint != fingerprints[cue.CueId])
                    || cue.QualityStatus is not (VietsubTranslationQualityStatuses.Valid
                        or VietsubTranslationQualityStatuses.Review)
            }).Select(cue => cue.CueId).ToHashSet();
    }

    private static void EnsureConfigurationFingerprint(VietsubTranslationJobParameters parameters)
    {
        var actual = VietsubTranslationFingerprintBuilder.BuildConfigurationFingerprint(
            new VietsubTranslationConfigurationSnapshot(
                parameters.Settings.SourceLanguageCode,
                parameters.Settings.TargetLanguageCode,
                parameters.EngineId,
                parameters.EngineVersion,
                parameters.Settings.MaximumCharactersPerSecond,
                parameters.Settings.ContextSummary,
                parameters.Settings.CharacterInstructions,
                parameters.Settings.StyleInstructions,
                parameters.Settings.Glossary,
                "project-memory-v1",
                RuntimeProfileId: parameters.StrategyVersion >= 2
                    ? parameters.RuntimeProfileId
                    : null));
        if (!string.Equals(actual, parameters.ConfigurationFingerprint, StringComparison.Ordinal))
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.JobNotResumable,
                "Configuration snapshot của translation job không còn hợp lệ.");
        }
    }

    private static void EnsureProviderSnapshot(
        VietsubTranslationSceneResult result,
        VietsubTranslationJobParameters parameters)
    {
        if (!string.Equals(result.EngineId, parameters.EngineId, StringComparison.Ordinal)
            || !string.Equals(result.EngineVersion, parameters.EngineVersion, StringComparison.Ordinal))
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.ResultInvalid,
                "Engine trả result không khớp engine/version đã snapshot cho job.");
        }
    }

    private static VietsubTranslationCheckpoint RestoreCheckpoint(string? json, int totalItems)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new VietsubTranslationCheckpoint(1, 0, totalItems, 0, 0, 0, 0, 0, 0, 0, 0);
        }
        try
        {
            var checkpoint = JsonSerializer.Deserialize<VietsubTranslationCheckpoint>(json, JsonOptions);
            return checkpoint is { StrategyVersion: 1 } && checkpoint.TotalItems == totalItems
                ? checkpoint
                : new VietsubTranslationCheckpoint(1, 0, totalItems, 0, 0, 0, 0, 0, 0, 0, 0);
        }
        catch (JsonException)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.JobNotResumable,
                "Checkpoint translation job bị hỏng.");
        }
    }

    private static string BuildSceneInputFingerprint(IEnumerable<string> fingerprints)
    {
        var payload = string.Join('\n', fingerprints);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private async Task<CurrentCueForApply> LoadCurrentCueForApplyAsync(
        Guid projectId,
        Guid trackId,
        Guid cueId,
        int contextCueCount,
        string configurationFingerprint,
        CancellationToken cancellationToken)
    {
        var orderedCues = (await subtitleStore.LoadCueContextAsync(projectId, trackId, cueId,
            contextCueCount, cancellationToken)).ToArray();
        var cueIndex = Array.FindIndex(orderedCues, candidate => candidate.CueId == cueId);
        if (cueIndex < 0)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.JobNotResumable,
                "Cue nguồn đã bị xóa trong lúc translation job đang chạy.");
        }

        var cue = orderedCues[cueIndex];
        return new CurrentCueForApply(
            cue,
            VietsubTranslationFingerprintBuilder.BuildCueFingerprint(
                cue,
                cueIndex,
                orderedCues,
                contextCueCount,
                configurationFingerprint));
    }

    private sealed record CurrentCueForApply(
        VietsubSubtitleCue Cue,
        string Fingerprint);
}

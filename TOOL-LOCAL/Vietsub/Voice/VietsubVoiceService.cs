using System.Security.Cryptography;
using System.Text.Json;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;

namespace TOOL_LOCAL.Vietsub.Voice;

internal sealed class VietsubVoiceService(
    IVietsubLocalJobAuthorizer authorizer,
    VietsubSubtitleStore subtitleStore,
    VietsubVoiceStore voiceStore,
    VietsubAppPaths paths,
    VietsubVoiceComponentStore components,
    VietsubVoicePlaybackRegistry playbackRegistry,
    VietsubVoiceTimelineRenderer timelineRenderer,
    VietsubJobManager jobManager,
    VietsubKokoroRuntime? kokoroRuntime = null)
{
    internal TOOL_LOCAL.SystemSetup.SystemSetupCoordinator? SetupCoordinator { get; set; }
    public async Task<int> SetCueVoiceEnabledAsync(VietsubProjectSession session, string userId,
        Guid organizationId, Guid trackId, int revision, IReadOnlyList<Guid> cueIds,
        bool enabled, CancellationToken token)
    {
        await AuthorizeAsync(session.Manifest, userId, organizationId, token);
        if (session.Manifest.ActiveSubtitleTrackId != trackId)
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.TrackChanged, "Track phụ đề đang chọn đã thay đổi.");
        var nextRevision = await subtitleStore.SetVoiceEnabledAsync(session.Manifest.ProjectId,
            trackId, revision, cueIds, enabled, token);
        if (nextRevision != revision) playbackRegistry.ClearProject(session.Manifest.ProjectId);
        return nextRevision;
    }

    public VietsubVoicePlaybackRegistry PlaybackRegistry => playbackRegistry;

    public async Task<VietsubVoiceSettings> SelectVoiceAsync(VietsubProjectSession session,
        string userId, Guid organizationId, string voiceId, CancellationToken token)
    {
        await AuthorizeAsync(session.Manifest, userId, organizationId, token);
        if (!components.FeatureEnabled)
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.FeatureDisabled,
                "Tạo giọng local đang bị khóa bởi feature flag.");
        if (voiceId != VietsubVoiceCatalog.PiperVoiceId
            && VietsubVoiceModelCatalog.Find(voiceId) is null)
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.ModelNotApproved,
                "Giọng local không thuộc danh mục đã duyệt.");
        if (await jobManager.HasActiveAsync(session.Manifest.ProjectId, token))
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.JobConflict,
                "Hãy chờ tác vụ Vietsub hiện tại kết thúc trước khi đổi giọng.");
        await session.UpdateAsync(manifest =>
        {
            manifest.VoiceSettings.EngineId = voiceId == VietsubVoiceCatalog.PiperVoiceId
                ? VietsubVoiceEngines.Piper : VietsubVoiceEngines.Kokoro;
            manifest.VoiceSettings.ModelId = voiceId == VietsubVoiceCatalog.PiperVoiceId
                ? VietsubVoiceCatalog.PiperModelId : VietsubVoiceModelCatalog.ModelId;
            manifest.VoiceSettings.VoiceId = voiceId;
            manifest.VoiceSettings.Normalize();
        }, token);
        await session.FlushAsync(token);
        playbackRegistry.ClearProject(session.Manifest.ProjectId);
        return session.Manifest.VoiceSettings;
    }

    public VietsubVoiceRuntimeStatus GetRuntimeStatus() => components.GetStatus();

    public async Task<IReadOnlyList<VietsubVoiceModelStatus>> GetModelStatusesAsync(
        VietsubProjectSession session, string userId, Guid organizationId, CancellationToken token)
    {
        await AuthorizeAsync(session.Manifest, userId, organizationId, token);
        return await Task.Run(() => components.GetModelStatuses().Select(model =>
        {
            var runtime = model.EngineId == VietsubVoiceEngines.Piper
                ? components.GetStatus()
                : kokoroRuntime?.GetStatus(model.VoiceId);
            return model with { SynthesisReady = runtime?.Ready == true,
                SynthesisMessage = runtime?.Message ?? "Runtime Kokoro chưa khả dụng." };
        }).ToArray(), token);
    }

    public async Task<VietsubVoiceModelStatus> InstallModelAsync(
        VietsubProjectSession session,
        string userId,
        Guid organizationId,
        string voiceId,
        IProgress<VietsubVoiceModelInstallProgress>? progress,
        CancellationToken token)
    {
        await AuthorizeAsync(session.Manifest, userId, organizationId, token);
        try
        {
            using var runtimeLease = TOOL_LOCAL.SystemSetup.RuntimeUseGate.Shared.Acquire(exclusive: true);
            var model = await components.InstallModelAsync(voiceId, progress, token);
            if (model.EngineId == VietsubVoiceEngines.Kokoro)
            {
                if (kokoroRuntime is null)
                    throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeNotInstalled,
                        "Runtime Kokoro chưa được tích hợp.");
                var runtime = await kokoroRuntime.InstallAsync(voiceId, progress, token);
                return model with { SynthesisReady = runtime.Ready, SynthesisMessage = runtime.Message };
            }
            var piper = components.GetStatus();
            return model with { SynthesisReady = piper.Ready, SynthesisMessage = piper.Message };
        }
        catch (TOOL_LOCAL.SystemSetup.SetupException exception)
        {
            throw new VietsubVoiceException(exception.Code, exception.Message);
        }
    }

    public async Task<VietsubVoiceRuntimeStatus> InstallRuntimeAsync(
        VietsubProjectSession session,
        string userId,
        Guid organizationId,
        IProgress<VietsubVoiceRuntimeInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        await AuthorizeAsync(session.Manifest, userId, organizationId, cancellationToken);
        if (SetupCoordinator is { } setup)
        {
            try
            {
                await setup.PrepareLegacyAsync("piper", false,
                    p => progress?.Report(new(p.Stage ?? "VERIFY", p.Percent ?? 0, "Đang chuẩn bị giọng Việt.",
                        p.BytesProcessed ?? 0, p.TotalBytes ?? 0)), cancellationToken);
                return components.GetStatus();
            }
            catch (TOOL_LOCAL.SystemSetup.SetupException e) { throw new VietsubVoiceException(e.Code, e.Message); }
        }
        try
        {
            using var runtimeLease = TOOL_LOCAL.SystemSetup.RuntimeUseGate.Shared.Acquire(exclusive: true);
            return await components.InstallAsync(progress, cancellationToken);
        }
        catch (TOOL_LOCAL.SystemSetup.SetupException exception)
        {
            throw new VietsubVoiceException(exception.Code, exception.Message);
        }
    }

    public Task<VietsubVoiceWorkspaceSummary> GetWorkspaceAsync(
        VietsubProjectManifest project,
        CancellationToken cancellationToken)
    {
        var snapshot = JsonSerializer.Deserialize<VietsubProjectManifest>(JsonSerializer.SerializeToUtf8Bytes(project))!;
        return Task.Run(() => GetWorkspaceCoreAsync(snapshot, cancellationToken), cancellationToken);
    }

    private async Task<VietsubVoiceWorkspaceSummary> GetWorkspaceCoreAsync(
        VietsubProjectManifest project, CancellationToken cancellationToken)
    {
        var activeTrack = project.ActiveSubtitleTrackId is Guid trackId
            ? (await subtitleStore.LoadSummariesAsync(project.ProjectId, cancellationToken))
                .SingleOrDefault(track => track.TrackId == trackId)
            : null;
        var trackRevision = activeTrack?.Revision;
        var workspace = await voiceStore.LoadWorkspaceAsync(
            project.ProjectId,
            project.ActiveSubtitleTrackId,
            trackRevision,
            project.VoiceSettings,
            cancellationToken);
        if (workspace.Timeline is null && activeTrack is not null
            && await voiceStore.HasTimelineHistoryAsync(project.ProjectId, activeTrack.TrackId, cancellationToken))
        {
            try
            {
                var track = (await subtitleStore.LoadTracksAsync(project.ProjectId, cancellationToken))
                    .SingleOrDefault(value => value.TrackId == activeTrack.TrackId);
                if (track is not null && track.Revision == activeTrack.Revision
                    && await TryRebuildTimelineFromCachedPhrasesAsync(project, track, cancellationToken))
                {
                    workspace = await voiceStore.LoadWorkspaceAsync(
                        project.ProjectId,
                        activeTrack.TrackId,
                        activeTrack.Revision,
                        project.VoiceSettings,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Khôi phục cache là best-effort; trạng thái workspace vẫn phải tải được để
                // người dùng có thể chủ động tạo lại voice nếu file cũ hoặc FFmpeg không còn hợp lệ.
            }
        }
        var enabledCount = activeTrack?.VoiceEnabledCueCount ?? 0;
        workspace = workspace with
        {
            EnabledCueCount = enabledCount,
            Timeline = enabledCount > 0 ? workspace.Timeline : null,
            RequiresRebuild = enabledCount > 0 && workspace.Timeline is null && activeTrack is not null
                && await voiceStore.HasTimelineHistoryAsync(project.ProjectId, activeTrack.TrackId, cancellationToken)
        };
        if (workspace.Timeline is not { } timeline)
        {
            playbackRegistry.ClearProject(project.ProjectId);
            return workspace;
        }

        var absolutePath = paths.GetProjectPath(
            project.ProjectId,
            timeline.RelativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries));
        playbackRegistry.RegisterCurrent(new(
            project.ProjectId,
            timeline.ArtifactId,
            timeline.TrackId,
            timeline.TrackRevision,
            absolutePath,
            timeline.SizeBytes,
            timeline.Sha256));
        return workspace with
        {
            TimelinePlaybackUrl = VietsubVoicePlaybackRegistry.CreateUrl(
                project.ProjectId,
                timeline.ArtifactId,
                timeline.Sha256)
        };
    }

    public async Task<bool> TryCarryForwardTimelineAfterCueExtensionAsync(
        VietsubProjectManifest project,
        Guid cueId,
        VietsubVoiceArtifact sourceTimeline,
        VietsubTimelineCueTimingUpdate update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sourceTimeline);
        ArgumentNullException.ThrowIfNull(update);
        if (!update.Changed
            || update.StartMilliseconds != update.PreviousStartMilliseconds
            || update.EndMilliseconds < update.PreviousEndMilliseconds
            || sourceTimeline.ArtifactKind != VietsubVoiceArtifactKinds.Timeline
            || sourceTimeline.Status != VietsubVoiceArtifactStatuses.Ready
            || sourceTimeline.TrackId != project.ActiveSubtitleTrackId
            || sourceTimeline.TrackRevision != update.TrackRevision - 1
            || sourceTimeline.EngineId != project.VoiceSettings.EngineId
            || sourceTimeline.ModelId != project.VoiceSettings.ModelId
            || sourceTimeline.VoiceId != project.VoiceSettings.VoiceId
            || !sourceTimeline.CueIds.Contains(cueId))
        {
            return false;
        }

        var currentTrack = (await subtitleStore.LoadTracksAsync(project.ProjectId, cancellationToken))
            .SingleOrDefault(track => track.TrackId == sourceTimeline.TrackId);
        if (currentTrack?.Revision != update.TrackRevision)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var currentTimeline = sourceTimeline with
        {
            ArtifactId = Guid.NewGuid(),
            TrackRevision = update.TrackRevision,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        return await voiceStore.SaveArtifactAsync(
            project.ProjectId,
            currentTimeline,
            update.TrackRevision,
            cancellationToken);
    }

    public async Task<VietsubJobSummary> StartAsync(
        VietsubProjectSession session,
        string userId,
        Guid organizationId,
        VietsubStartVoiceInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var project = session.Manifest;
        await AuthorizeAsync(project, userId, organizationId, cancellationToken);
        if (!components.FeatureEnabled)
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.FeatureDisabled,
                "Tạo giọng local đang bị khóa bởi feature flag.");
        project.VoiceSettings.Normalize();
        var runtime = project.VoiceSettings.EngineId == VietsubVoiceEngines.Piper
            ? components.GetStatus()
            : kokoroRuntime?.GetStatus(project.VoiceSettings.VoiceId)
                ?? throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeNotInstalled,
                    "Runtime Kokoro chưa được tích hợp.");
        if (!runtime.Ready)
        {
            throw new VietsubVoiceException(runtime.ErrorCode ?? VietsubVoiceErrorCodes.RuntimeNotInstalled, runtime.Message);
        }
        if (input.ExpectedTrackId == Guid.Empty || input.ExpectedTrackRevision < 1
            || project.ActiveSubtitleTrackId != input.ExpectedTrackId)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.TrackRequired, "Hãy chọn subtitle track đã dịch trước khi tạo giọng.");
        }
        var tracks = await subtitleStore.LoadTracksAsync(project.ProjectId, cancellationToken);
        var track = tracks.SingleOrDefault(item => item.TrackId == input.ExpectedTrackId)
            ?? throw new VietsubVoiceException(VietsubVoiceErrorCodes.TrackRequired, "Không tìm thấy subtitle track đang dùng.");
        if (track.Revision != input.ExpectedTrackRevision)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.TrackChanged, "Phụ đề đã thay đổi. Hãy tải lại trước khi tạo giọng.");
        }
        VietsubVoiceTranslationPolicy.EnsureComplete(track);

        var snapshot = CreateSettingsSnapshot(project.VoiceSettings);
        var parameters = new VietsubVoiceJobParameters(
            snapshot.EngineId == VietsubVoiceEngines.Piper ? 2 : 3,
            track.TrackId,
            track.Revision,
            VietsubVoiceFingerprintBuilder.BuildConfigurationFingerprint(snapshot),
            snapshot,
            VietsubVoiceFingerprintBuilder.BuildSelectionFingerprint(track.Cues));

        VietsubJobSummary job;
        try
        {
            job = await jobManager.EnqueueAsync(
                project.ProjectId,
                VietsubJobTypes.SynthesizeVoiceLocal,
                ["VOICE_PREPARE", "VOICE_SYNTHESIZE", "VOICE_TIMELINE", "VOICE_PUBLISH"],
                parameters.ToJson(),
                track.TrackId,
                track.Revision,
                maxAttempts: 3,
                startImmediately: false,
                cancellationToken: cancellationToken);
        }
        catch (VietsubJobException exception) when (exception.Code == "vietsub_job_already_active")
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.JobConflict, "Project đang có local job khác chưa kết thúc.", innerException: exception);
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
            try { await jobManager.CancelAsync(project.ProjectId, job.Id, CancellationToken.None); } catch { }
            try
            {
                await session.UpdateAsync(manifest => manifest.Status = VietsubProjectStatuses.Ready, CancellationToken.None);
                await session.FlushAsync(CancellationToken.None);
            }
            catch { }
            throw;
        }
    }

    private async Task<bool> TryRebuildTimelineFromCachedPhrasesAsync(
        VietsubProjectManifest project,
        VietsubSubtitleTrack track,
        CancellationToken cancellationToken)
    {
        var sourceTimeline = await voiceStore.FindLatestTimelineBeforeRevisionAsync(
            project.ProjectId,
            track.TrackId,
            track.Revision,
            cancellationToken);
        if (sourceTimeline is null) return false;

        var orderedCues = track.Cues
            .Where(cue => cue.VoiceEnabled && !string.IsNullOrWhiteSpace(cue.TranslatedText))
            .OrderBy(cue => cue.StartMilliseconds)
            .ThenBy(cue => cue.EndMilliseconds)
            .ThenBy(cue => cue.CueId)
            .ToArray();
        var orderedCueIds = orderedCues.Select(cue => cue.CueId).ToArray();
        if (orderedCueIds.Length == 0 || track.Cues.Any(cue => cue.VoiceEnabled && string.IsNullOrWhiteSpace(cue.TranslatedText)))
        {
            return false;
        }

        var snapshot = CreateSettingsSnapshot(project.VoiceSettings);
        var configurationFingerprint = VietsubVoiceFingerprintBuilder.BuildConfigurationFingerprint(snapshot);
        var cueById = orderedCues.ToDictionary(cue => cue.CueId);
        var cueIndexById = orderedCueIds
            .Select((cueId, index) => (cueId, index))
            .ToDictionary(item => item.cueId, item => item.index);
        var candidatesByStart = new Dictionary<int, List<CachedPhraseCandidate>>();
        var cachedArtifacts = await voiceStore.LoadReadyPhraseArtifactsAsync(
            project.ProjectId,
            track.TrackId,
            cancellationToken);
        foreach (var artifact in cachedArtifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(artifact.PhraseId)
                || artifact.CueIds.Count == 0
                || artifact.CueIds.Any(cueId => !cueById.ContainsKey(cueId)))
            {
                continue;
            }

            var indices = artifact.CueIds.Select(cueId => cueIndexById[cueId]).ToArray();
            var firstIndex = indices[0];
            if (indices.Where((value, offset) => value != firstIndex + offset).Any())
            {
                continue;
            }

            var cues = artifact.CueIds.Select(cueId => cueById[cueId]).ToArray();
            if (cues.Length > 1 && track.Cues.Any(cue => !cue.VoiceEnabled
                && cue.StartMilliseconds < cues[^1].EndMilliseconds && cue.EndMilliseconds > cues[0].StartMilliseconds))
                continue;
            if (cues.Any(cue => !string.Equals(cue.Speaker, cues[0].Speaker, StringComparison.Ordinal)))
            {
                continue;
            }
            var phrase = new VietsubVoicePhrase(
                artifact.PhraseId,
                artifact.CueIds,
                cues[0].Speaker,
                string.Join(' ', cues.Select(cue => cue.TranslatedText.Trim())),
                cues[0].StartMilliseconds,
                cues[^1].EndMilliseconds);
            var currentFingerprint = VietsubVoiceFingerprintBuilder.BuildPhraseFingerprint(phrase, snapshot);
            if (!FixedHashEquals(currentFingerprint, artifact.ContentFingerprint))
            {
                continue;
            }

            var absolutePath = ResolveArtifactPath(project.ProjectId, artifact.RelativePath);
            if (!await IsArtifactUsableAsync(absolutePath, artifact, cancellationToken))
            {
                continue;
            }
            var metadata = VietsubWavInspector.Inspect(absolutePath, snapshot.TrimSilence);
            var candidate = new CachedPhraseCandidate(
                firstIndex,
                artifact.CueIds.Count,
                new(VietsubVoicePhrasePlanner.ApplySelectionBoundaries([phrase], track.Cues)[0], artifact, absolutePath, metadata));
            if (!candidatesByStart.TryGetValue(firstIndex, out var candidates))
            {
                candidates = [];
                candidatesByStart[firstIndex] = candidates;
            }
            candidates.Add(candidate);
        }

        var reachable = new bool[orderedCues.Length + 1];
        var choice = new CachedPhraseCandidate?[orderedCues.Length];
        reachable[orderedCues.Length] = true;
        for (var index = orderedCues.Length - 1; index >= 0; index--)
        {
            if (!candidatesByStart.TryGetValue(index, out var candidates)) continue;
            foreach (var candidate in candidates
                         .OrderByDescending(item => item.CueCount)
                         .ThenByDescending(item => item.Audio.Artifact.UpdatedAtUtc))
            {
                var nextIndex = index + candidate.CueCount;
                if (nextIndex <= orderedCues.Length && reachable[nextIndex])
                {
                    choice[index] = candidate;
                    reachable[index] = true;
                    break;
                }
            }
        }
        if (!reachable[0]) return false;

        var phraseAudio = new List<VietsubVoicePhraseAudio>();
        for (var index = 0; index < orderedCues.Length;)
        {
            var candidate = choice[index];
            if (candidate is null) return false;
            phraseAudio.Add(candidate.Audio);
            index += candidate.CueCount;
        }

        var requestedDuration = project.SourceVideo is null
            ? phraseAudio.Max(item => item.Phrase.EndMilliseconds)
            : (long)Math.Ceiling(project.SourceVideo.Metadata.DurationSeconds * 1000m);
        VietsubVoiceTimelineRenderResult? rendered = null;
        try
        {
            rendered = await timelineRenderer.RenderAsync(
                project.ProjectId,
                Guid.NewGuid(),
                track.TrackId,
                track.Revision,
                phraseAudio,
                snapshot,
                requestedDuration,
                cancellationToken);
            await voiceStore.SaveTimingDiagnosticsAsync(
                project.ProjectId,
                track.TrackId,
                track.Revision,
                rendered.Diagnostics,
                cancellationToken);

            var now = DateTime.UtcNow;
            var artifact = new VietsubVoiceArtifact(
                Guid.NewGuid(),
                track.TrackId,
                track.Revision,
                VietsubVoiceArtifactKinds.Timeline,
                null,
                Path.GetRelativePath(paths.GetProjectDirectory(project.ProjectId), rendered.AbsolutePath),
                new FileInfo(rendered.AbsolutePath).Length,
                await Sha256Async(rendered.AbsolutePath, cancellationToken),
                VietsubVoiceFingerprintBuilder.BuildTimelineFingerprint(configurationFingerprint, phraseAudio.Select(item => item.Artifact)),
                snapshot.EngineId,
                snapshot.EngineVersion,
                snapshot.ModelId,
                snapshot.ModelVersion,
                snapshot.VoiceId,
                rendered.Metadata.DurationMilliseconds,
                rendered.Metadata.SampleRate,
                rendered.Metadata.Channels,
                VietsubVoiceArtifactStatuses.Ready,
                rendered.Diagnostics.Any(item => item.Status == VietsubVoiceTimingStatuses.ReviewRequired)
                    ? VietsubVoiceTimingStatuses.ReviewRequired
                    : rendered.Diagnostics.Any(item => item.Status == VietsubVoiceTimingStatuses.Compressed)
                        ? VietsubVoiceTimingStatuses.Compressed
                        : VietsubVoiceTimingStatuses.Natural,
                now,
                now,
                orderedCueIds);
            if (await voiceStore.SaveArtifactAsync(
                    project.ProjectId,
                    artifact,
                    track.Revision,
                    cancellationToken))
            {
                return true;
            }
            TryDelete(rendered.AbsolutePath);
            return false;
        }
        catch
        {
            if (rendered is not null) TryDelete(rendered.AbsolutePath);
            throw;
        }
    }

    private static VietsubVoiceSettingsSnapshot CreateSettingsSnapshot(VietsubVoiceSettings settings)
    {
        settings.Normalize();
        var piper = settings.EngineId == VietsubVoiceEngines.Piper;
        return new(
            settings.EngineId,
            piper ? VietsubVoiceCatalog.PiperEngineVersion : VietsubVoiceCatalog.KokoroEngineVersion,
            settings.ModelId,
            piper ? VietsubVoiceCatalog.PiperModelVersion : VietsubVoiceModelCatalog.Revision,
            settings.VoiceId,
            settings.MaximumPhraseGapMilliseconds,
            settings.MaximumPhraseDurationMilliseconds,
            settings.MaximumPhraseCharacters,
            settings.MaximumBorrowedGapMilliseconds,
            settings.PreferredMaximumTempo,
            settings.MaximumTempo,
            settings.TrimSilence);
    }

    private string ResolveArtifactPath(Guid projectId, string relativePath)
    {
        if (Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidOperationException("Voice artifact path không hợp lệ.");
        }
        return paths.GetProjectPath(
            projectId,
            relativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries));
    }

    private static async Task<bool> IsArtifactUsableAsync(
        string absolutePath,
        VietsubVoiceArtifact artifact,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(absolutePath);
            return info.Exists
                && info.Length == artifact.SizeBytes
                && FixedHashEquals(await Sha256Async(absolutePath, cancellationToken), artifact.Sha256);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            return false;
        }
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static bool FixedHashEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record CachedPhraseCandidate(
        int StartIndex,
        int CueCount,
        VietsubVoicePhraseAudio Audio);

    private async Task AuthorizeAsync(
        VietsubProjectManifest project,
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await authorizer.AuthorizeAsync(userId, organizationId, project, cancellationToken);
        }
        catch (VietsubLocalJobAuthorizationException exception)
        {
            throw new VietsubVoiceException(
                exception.Code == VietsubLocalJobAuthorizationErrorCodes.LicenseRequired
                    ? VietsubVoiceErrorCodes.LicenseRequired
                    : VietsubVoiceErrorCodes.AccessDenied,
                exception.Message,
                innerException: exception);
        }
    }
}

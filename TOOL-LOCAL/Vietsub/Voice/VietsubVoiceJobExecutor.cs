using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_LOCAL.Vietsub.Voice;

internal sealed record VietsubVoiceCheckpoint(
    int StrategyVersion,
    int TotalPhrases,
    int CompletedPhrases,
    int CacheHits,
    string Stage);

internal sealed class VietsubVoiceJobExecutor(
    VietsubProjectStore projectStore,
    VietsubSubtitleStore subtitleStore,
    VietsubVoiceStore voiceStore,
    IVietsubVoiceSynthesizer synthesizer,
    VietsubVoiceTimelineRenderer timelineRenderer,
    VietsubJobStore jobStore,
    VietsubAppPaths paths) : IVietsubJobExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string JobType => VietsubJobTypes.SynthesizeVoiceLocal;

    public async Task ExecuteAsync(VietsubJobExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteCoreAsync(context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (VietsubVoiceException exception)
        {
            throw new VietsubJobExecutionException(exception.Code, exception.Message, exception.Retryable, exception);
        }
    }

    private async Task ExecuteCoreAsync(VietsubJobExecutionContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var parameters = VietsubVoiceJobParameters.Parse(context.Job.ParametersJson);
        await context.ReportProgressAsync(
            new("VOICE_PREPARE", 20, 2, "Đang kiểm tra revision, bản dịch và cache giọng local."),
            cancellationToken);

        var project = await projectStore.LoadForBackgroundJobAsync(context.Job.ProjectId, cancellationToken);
        var tracks = await subtitleStore.LoadTracksAsync(project.ProjectId, cancellationToken);
        var track = tracks.SingleOrDefault(item => item.TrackId == parameters.InputTrackId)
            ?? throw new VietsubVoiceException(VietsubVoiceErrorCodes.JobNotResumable, "Subtitle track của voice job không còn tồn tại.");
        if (track.Revision != parameters.InputRevision)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.TrackChanged, "Phụ đề đã thay đổi. Hãy tạo voice job mới từ revision hiện tại.");
        }
        VietsubVoiceTranslationPolicy.EnsureComplete(track);
        if ((parameters.StrategyVersion == 1 && track.Cues.Any(cue => !cue.VoiceEnabled))
            || (parameters.StrategyVersion == 2 && parameters.SelectionFingerprint
                != VietsubVoiceFingerprintBuilder.BuildSelectionFingerprint(track.Cues)))
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.TrackChanged, "Lựa chọn câu tạo giọng đã thay đổi. Hãy tạo tác vụ mới.");
        var expectedConfiguration = VietsubVoiceFingerprintBuilder.BuildConfigurationFingerprint(parameters.Settings);
        if (!FixedHashEquals(expectedConfiguration, parameters.ConfigurationFingerprint))
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.JobNotResumable, "Fingerprint cấu hình voice job không còn hợp lệ.");
        }

        var phrases = VietsubVoicePhrasePlanner.Plan(track.Cues, parameters.Settings);
        if (phrases.Count == 0)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.TranslationRequired, "Không có bản dịch tiếng Việt để tạo giọng.");
        }

        var phraseArtifacts = new Dictionary<string, VietsubVoiceArtifact>(StringComparer.Ordinal);
        var pending = new List<(VietsubVoicePhrase Phrase, string Fingerprint, VietsubVoiceSynthesisItem Item)>();
        var cacheHits = 0;
        var tempDirectory = paths.GetProjectPath(project.ProjectId, "temp", $"voice-{context.Job.Id:N}", "phrases");
        var outputDirectory = paths.GetProjectPath(project.ProjectId, "voice", track.TrackId.ToString("N"), $"revision-{track.Revision}");
        Directory.CreateDirectory(tempDirectory);
        Directory.CreateDirectory(outputDirectory);
        try
        {
            for (var index = 0; index < phrases.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var phrase = phrases[index];
                var fingerprint = VietsubVoiceFingerprintBuilder.BuildPhraseFingerprint(phrase, parameters.Settings);
                var reusable = await voiceStore.FindReusablePhraseAsync(project.ProjectId, fingerprint, cancellationToken);
                if (reusable is not null && await IsArtifactUsableAsync(project.ProjectId, reusable, cancellationToken))
                {
                    phraseArtifacts[phrase.PhraseId] = reusable;
                    cacheHits++;
                    continue;
                }

                var partial = Path.Combine(tempDirectory, $"{index:D6}-{Guid.NewGuid():N}.partial.wav");
                pending.Add((phrase, fingerprint, new(index, phrase.PhraseId, phrase.Text, partial)));
            }

            var completed = cacheHits;
            await context.SaveCheckpointAsync(
                JsonSerializer.Serialize(new VietsubVoiceCheckpoint(1, phrases.Count, completed, cacheHits, "SYNTHESIS"), JsonOptions),
                cancellationToken);
            await context.ReportProgressAsync(new("VOICE_SYNTHESIZE", completed * 100d / phrases.Count,
                5 + completed * 65d / phrases.Count, $"Đã phục hồi {cacheHits}/{phrases.Count} đoạn giọng đã tạo."), cancellationToken);
            if (pending.Count > 0)
            {
                await synthesizer.SynthesizeIncrementallyAsync(
                    pending.Select(item => item.Item).ToArray(),
                    async item =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entry = pending.Single(value => value.Item.Index == item.Index);
                        var metadata = VietsubWavInspector.Inspect(item.OutputPath, parameters.Settings.TrimSilence);
                        var finalPath = Path.Combine(outputDirectory, $"phrase-{entry.Phrase.PhraseId}-{entry.Fingerprint[..12]}-{Guid.NewGuid():N}.wav");
                        File.Move(item.OutputPath, finalPath);
                        var now = DateTime.UtcNow;
                        var artifact = new VietsubVoiceArtifact(
                            Guid.NewGuid(),
                            track.TrackId,
                            track.Revision,
                            VietsubVoiceArtifactKinds.Phrase,
                            entry.Phrase.PhraseId,
                            Path.GetRelativePath(paths.GetProjectDirectory(project.ProjectId), finalPath),
                            new FileInfo(finalPath).Length,
                            await Sha256Async(finalPath, cancellationToken),
                            entry.Fingerprint,
                            parameters.Settings.EngineId,
                            parameters.Settings.EngineVersion,
                            parameters.Settings.ModelId,
                            parameters.Settings.ModelVersion,
                            parameters.Settings.VoiceId,
                            metadata.DurationMilliseconds,
                            metadata.SampleRate,
                            metadata.Channels,
                            VietsubVoiceArtifactStatuses.Ready,
                            null,
                            now,
                            now,
                            entry.Phrase.CueIds);
                        if (!await voiceStore.SaveArtifactAsync(project.ProjectId, artifact, track.Revision, cancellationToken))
                        {
                            TryDelete(finalPath);
                            throw new VietsubVoiceException(VietsubVoiceErrorCodes.TrackChanged, "Phụ đề đã thay đổi trước khi lưu voice artifact.");
                        }
                        phraseArtifacts[entry.Phrase.PhraseId] = artifact;
                        completed++;
                        var checkpoint = JsonSerializer.Serialize(
                            new VietsubVoiceCheckpoint(1, phrases.Count, completed, cacheHits, "SYNTHESIS"),
                            JsonOptions);
                        await context.SaveCheckpointAsync(checkpoint, cancellationToken);
                        await context.ReportProgressAsync(
                            new(
                                "VOICE_SYNTHESIZE",
                                completed * 100d / phrases.Count,
                                5 + completed * 65d / phrases.Count,
                                $"Đã tạo hoặc phục hồi {completed}/{phrases.Count} phrase.",
                                checkpoint),
                            cancellationToken);
                    },
                    cancellationToken);
            }

            if (phraseArtifacts.Count != phrases.Count)
            {
                throw new VietsubVoiceException(VietsubVoiceErrorCodes.ResultInvalid, "Không thu thập đủ voice artifact cho timeline.", true);
            }

            var audio = new List<VietsubVoicePhraseAudio>(phrases.Count);
            foreach (var phrase in phrases)
            {
                var artifact = phraseArtifacts[phrase.PhraseId];
                var absolute = ResolveArtifactPath(project.ProjectId, artifact.RelativePath);
                audio.Add(new(phrase, artifact, absolute, VietsubWavInspector.Inspect(absolute, parameters.Settings.TrimSilence)));
            }
            // A checkpoint forces the throttled progress writer to persist this stage even if rendering fails immediately.
            var timelineCheckpoint = JsonSerializer.Serialize(
                new VietsubVoiceCheckpoint(1, phrases.Count, completed, cacheHits, "TIMELINE"), JsonOptions);
            await context.ReportProgressAsync(new("VOICE_TIMELINE", 5, 72,
                "Đang phân tích khoảng lặng và độ dài giọng đọc.", timelineCheckpoint), cancellationToken);
            var requestedDuration = project.SourceVideo is null
                ? phrases.Max(item => item.EndMilliseconds)
                : (long)Math.Ceiling(project.SourceVideo.Metadata.DurationSeconds * 1000m);
            var timeline = await timelineRenderer.RenderAsync(
                project.ProjectId,
                context.Job.Id,
                track.TrackId,
                track.Revision,
                audio,
                parameters.Settings,
                requestedDuration,
                cancellationToken,
                (done, total, token) => context.ReportProgressAsync(new("VOICE_TIMELINE", done * 100d / total,
                    72 + done * 23d / total, $"Đang ghép giọng: {done}/{total} đoạn."), token).AsTask());
            await context.ReportProgressAsync(new("VOICE_TIMELINE", 100, 96,
                "Đang xác minh timeline giọng trước khi lưu.", timelineCheckpoint), cancellationToken);
            await voiceStore.SaveTimingDiagnosticsAsync(
                project.ProjectId,
                track.TrackId,
                track.Revision,
                timeline.Diagnostics,
                cancellationToken);

            var timelineFingerprint = VietsubVoiceFingerprintBuilder.BuildTimelineFingerprint(
                parameters.ConfigurationFingerprint,
                phraseArtifacts.Values);
            var timelineNow = DateTime.UtcNow;
            var timelineArtifact = new VietsubVoiceArtifact(
                Guid.NewGuid(),
                track.TrackId,
                track.Revision,
                VietsubVoiceArtifactKinds.Timeline,
                null,
                Path.GetRelativePath(paths.GetProjectDirectory(project.ProjectId), timeline.AbsolutePath),
                new FileInfo(timeline.AbsolutePath).Length,
                await Sha256Async(timeline.AbsolutePath, cancellationToken),
                timelineFingerprint,
                parameters.Settings.EngineId,
                parameters.Settings.EngineVersion,
                parameters.Settings.ModelId,
                parameters.Settings.ModelVersion,
                parameters.Settings.VoiceId,
                timeline.Metadata.DurationMilliseconds,
                timeline.Metadata.SampleRate,
                timeline.Metadata.Channels,
                VietsubVoiceArtifactStatuses.Ready,
                timeline.Diagnostics.Any(item => item.Status == VietsubVoiceTimingStatuses.ReviewRequired)
                    ? VietsubVoiceTimingStatuses.ReviewRequired
                    : timeline.Diagnostics.Any(item => item.Status == VietsubVoiceTimingStatuses.Compressed)
                    ? VietsubVoiceTimingStatuses.Compressed
                    : VietsubVoiceTimingStatuses.Natural,
                timelineNow,
                timelineNow,
                phrases.SelectMany(item => item.CueIds).Distinct().ToArray());
            if (!await voiceStore.SaveArtifactAsync(project.ProjectId, timelineArtifact, track.Revision, cancellationToken))
            {
                TryDelete(timeline.AbsolutePath);
                throw new VietsubVoiceException(VietsubVoiceErrorCodes.TrackChanged, "Phụ đề đã thay đổi trước khi publish timeline giọng Việt.");
            }

            await jobStore.BindOutputTrackAsync(project.ProjectId, context.Job.Id, track.TrackId, cancellationToken);
            var metrics = JsonSerializer.Serialize(new
            {
                totalPhrases = phrases.Count,
                cacheHits,
                generatedPhrases = phrases.Count - cacheHits,
                timelineDurationMilliseconds = timeline.Metadata.DurationMilliseconds,
                elapsedMilliseconds = stopwatch.ElapsedMilliseconds
            }, JsonOptions);
            var finalCheckpoint = JsonSerializer.Serialize(
                new VietsubVoiceCheckpoint(1, phrases.Count, phrases.Count, cacheHits, "COMPLETED"),
                JsonOptions);
            await context.ReportProgressAsync(
                new("VOICE_PUBLISH", 100, 100, "Đã tạo xong timeline giọng Việt.", finalCheckpoint, metrics),
                cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private async Task<bool> IsArtifactUsableAsync(
        Guid projectId,
        VietsubVoiceArtifact artifact,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = ResolveArtifactPath(projectId, artifact.RelativePath);
            var info = new FileInfo(path);
            return info.Exists && info.Length == artifact.SizeBytes
                && FixedHashEquals(await Sha256Async(path, cancellationToken), artifact.Sha256);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private string ResolveArtifactPath(Guid projectId, string relativePath)
    {
        if (Path.IsPathFullyQualified(relativePath)) throw new InvalidOperationException("Voice artifact path không hợp lệ.");
        return paths.GetProjectPath(
            projectId,
            relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries));
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static bool FixedHashEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Media;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;

namespace TOOL_LOCAL.Vietsub.Ocr;

internal sealed record VietsubOcrCheckpoint(
    int StrategyVersion,
    long LastProcessedMilliseconds,
    long LastCommittedMilliseconds,
    int TrackRevision,
    int RecognizedFrames,
    int ReusedFrames,
    VietsubOcrAccumulatorSnapshot Accumulator);

internal sealed class VietsubOcrJobExecutor(
    VietsubProjectStore projectStore,
    IVietsubOcrSourceResolver mediaImportService,
    IVietsubOcrFrameReader frameReader,
    IVietsubOcrRecognizer recognizer,
    VietsubSubtitleStore subtitleStore,
    VietsubJobStore jobStore,
    VietsubAppPaths paths) : IVietsubJobExecutor, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const long CheckpointIntervalMilliseconds = 15_000;
    private const string TrackSource = "PADDLE_OCR_LOCAL";

    public string JobType => VietsubJobTypes.OcrLocal;

    public ValueTask DisposeAsync() => recognizer.DisposeAsync();

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
        catch (VietsubOcrException exception)
        {
            throw new VietsubJobExecutionException(exception.Code, exception.Message, innerException: exception);
        }
    }

    private async Task ExecuteCoreAsync(
        VietsubJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        var parameters = ParseAndValidateParameters(context.Job.ParametersJson);
        await context.ReportProgressAsync(
            new VietsubJobProgressUpdate(
                "OCR_PREPARE",
                20,
                1,
                "Đang kiểm tra video nguồn và runtime OCR."),
            cancellationToken);
        var project = await projectStore.LoadForBackgroundJobAsync(
            context.Job.ProjectId,
            cancellationToken);
        var media = project.SourceVideo;
        if (media is null
            || media.MediaId != parameters.MediaId
            || !string.Equals(media.Sha256, parameters.SourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.SourceChanged,
                "Video nguồn không còn khớp snapshot của OCR job.");
        }
        var sourcePath = await mediaImportService.ResolveVerifiedSourcePathAsync(
            project.ProjectId,
            media,
            cancellationToken);
        var runtime = await recognizer.GetRuntimeStatusAsync(cancellationToken);
        if (!runtime.Ready)
        {
            throw new VietsubOcrException(
                runtime.ErrorCode ?? VietsubOcrErrorCodes.RuntimeInvalid,
                runtime.Message);
        }
        if (!runtime.AvailableLanguages.Contains(parameters.LanguageCode, StringComparer.Ordinal))
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.LanguageNotSupported,
                "Runtime OCR chưa có model cho ngôn ngữ đã chọn.");
        }
        await context.ReportProgressAsync(
            new VietsubJobProgressUpdate(
                "OCR_PREPARE",
                100,
                2,
                "Runtime OCR đã sẵn sàng; đang mở luồng frame."),
            cancellationToken);

        var track = await GetOrCreateOutputTrackAsync(context.Job, parameters, cancellationToken);
        var checkpoint = ParseCheckpoint(context.Job.CheckpointJson, track);
        var resumeOverlap = parameters.Profile.SampleIntervalMilliseconds * 2L;
        var startMilliseconds = Math.Max(
            0,
            (checkpoint?.LastProcessedMilliseconds ?? 0) - resumeOverlap);
        VietsubOcrCueAccumulator accumulator;
        try
        {
            accumulator = checkpoint is null
                ? new VietsubOcrCueAccumulator(parameters.Profile.SampleIntervalMilliseconds)
                : VietsubOcrCueAccumulator.Restore(
                    parameters.Profile.SampleIntervalMilliseconds,
                    checkpoint.Accumulator);
        }
        catch (ArgumentException exception)
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.JobNotResumable,
                "Checkpoint OCR không thể phục hồi trạng thái gom cue.",
                exception);
        }
        var changeTracker = new VietsubOcrFrameChangeTracker(
            VietsubOcrFrameChangeTracker.ResolveMaximumReuseFrames(parameters.Profile),
            parameters.Profile.ChangeThreshold);
        var totalEstimatedFrames = Math.Max(
            1L,
            (long)Math.Ceiling(parameters.DurationSeconds * 1000m / parameters.Profile.SampleIntervalMilliseconds));
        var recognizedFrames = checkpoint?.RecognizedFrames ?? 0;
        var reusedFrames = checkpoint?.ReusedFrames ?? 0;
        var recognitionCalls = 0;
        var inferenceElapsed = TimeSpan.Zero;
        var diagnosticsBaseline = recognizer.GetDiagnostics();
        var processedFrames = Math.Max(
            0L,
            startMilliseconds / parameters.Profile.SampleIntervalMilliseconds);
        var lastCommittedMilliseconds = checkpoint?.LastCommittedMilliseconds ?? 0;
        VietsubOcrRecognitionResult? lastRecognition = null;
        var framePipelineReady = false;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await foreach (var frame in frameReader.ReadAsync(
                               sourcePath,
                               parameters.SourceWidth,
                               parameters.SourceHeight,
                               parameters.RotationDegrees,
                               parameters.Region,
                               parameters.Profile,
                               startMilliseconds,
                               cancellationToken))
            {
                processedFrames++;
                if (!framePipelineReady)
                {
                    framePipelineReady = true;
                    await context.ReportProgressAsync(
                        new VietsubJobProgressUpdate(
                            "OCR_EXTRACT_FRAMES",
                            100,
                            3,
                            "Đã mở luồng frame OCR từ video nguồn."),
                        cancellationToken);
                }
                var decision = changeTracker.Analyze(
                    VietsubOcrFrameChangeTracker.BuildSignature(frame),
                    frame.TimestampMilliseconds);
                if (decision.Kind == VietsubOcrFrameDecisionKind.Recognize)
                {
                    var inferenceStarted = Stopwatch.GetTimestamp();
                    lastRecognition = await recognizer.RecognizeAsync(
                        frame,
                        parameters.LanguageCode,
                        cancellationToken);
                    inferenceElapsed += Stopwatch.GetElapsedTime(inferenceStarted);
                    recognitionCalls++;
                    recognizedFrames++;
                    accumulator.Add(
                        decision.TimestampMilliseconds,
                        lastRecognition.Text,
                        lastRecognition.Confidence);
                }
                else if (decision.Kind == VietsubOcrFrameDecisionKind.Reuse && lastRecognition is not null)
                {
                    reusedFrames++;
                    accumulator.Add(
                        decision.TimestampMilliseconds,
                        lastRecognition.Text,
                        lastRecognition.Confidence);
                }

                var percent = Math.Clamp(
                    3 + processedFrames * 87d / totalEstimatedFrames,
                    3,
                    90);
                var message = BuildProgressMessage(
                    processedFrames,
                    totalEstimatedFrames,
                    recognizedFrames,
                    reusedFrames,
                    stopwatch.Elapsed);
                var shouldCheckpoint = frame.TimestampMilliseconds - lastCommittedMilliseconds
                    >= CheckpointIntervalMilliseconds;
                if (shouldCheckpoint)
                {
                    await CommitAsync(
                        context,
                        track,
                        accumulator,
                        parameters,
                        frame.TimestampMilliseconds,
                        recognizedFrames,
                        reusedFrames,
                        percent,
                        message,
                        cancellationToken);
                    lastCommittedMilliseconds = frame.TimestampMilliseconds;
                }
                else
                {
                    await context.ReportProgressAsync(
                        new VietsubJobProgressUpdate(
                            "OCR_RECOGNIZE",
                            percent / 0.9,
                            percent,
                            message),
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            var snapshot = accumulator.Snapshot();
            await CommitAsync(
                context,
                track,
                accumulator,
                parameters,
                snapshot.LastTimestampMilliseconds,
                recognizedFrames,
                reusedFrames,
                Math.Clamp(
                    3 + processedFrames * 87d / totalEstimatedFrames,
                    3,
                    90),
                "Đã lưu checkpoint OCR.",
                CancellationToken.None);
            throw;
        }

        MergeCues(track, accumulator.Complete(), parameters.Profile.SampleIntervalMilliseconds, parameters.DurationSeconds);
        if (track.Cues.Count == 0)
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.TextNotDetected,
                "Không phát hiện được phụ đề đủ tin cậy trong vùng đã chọn.");
        }
        await SaveTrackRevisionAsync(project.ProjectId, track, cancellationToken);
        await context.ReportProgressAsync(
            new VietsubJobProgressUpdate(
                "OCR_BUILD_CUES",
                100,
                95,
                $"Đã tạo {track.Cues.Count} đoạn phụ đề."),
            cancellationToken);

        await WriteSrtArtifactAsync(project.ProjectId, track, cancellationToken);
        var diagnostics = recognizer.GetDiagnostics();
        var mediaDuration = TimeSpan.FromSeconds((double)parameters.DurationSeconds);
        var metricsJson = JsonSerializer.Serialize(new
        {
            processedFrames,
            recognizedFrames,
            reusedFrames,
            cueCount = track.Cues.Count,
            elapsedMilliseconds = (long)stopwatch.Elapsed.TotalMilliseconds,
            inferenceMilliseconds = (long)inferenceElapsed.TotalMilliseconds,
            averageInferenceMilliseconds = recognitionCalls == 0
                ? 0
                : inferenceElapsed.TotalMilliseconds / recognitionCalls,
            realTimeFactor = mediaDuration.TotalMilliseconds <= 0
                ? 0
                : stopwatch.Elapsed.TotalMilliseconds / mediaDuration.TotalMilliseconds,
            directRecognitionFrames = Math.Max(
                0,
                diagnostics.DirectRecognitionFrames - diagnosticsBaseline.DirectRecognitionFrames),
            fullDetectionFrames = Math.Max(
                0,
                diagnostics.FullDetectionFrames - diagnosticsBaseline.FullDetectionFrames),
            strategyVersion = parameters.StrategyVersion
        }, JsonOptions);
        var finalCheckpoint = CreateCheckpoint(
            parameters,
            accumulator,
            Math.Max(lastCommittedMilliseconds, (long)(parameters.DurationSeconds * 1000)),
            track,
            recognizedFrames,
            reusedFrames);
        await context.ReportProgressAsync(
            new VietsubJobProgressUpdate(
                "OCR_WRITE_ARTIFACT",
                100,
                100,
                "Đã ghi track và SRT OCR.",
                JsonSerializer.Serialize(finalCheckpoint, JsonOptions),
                metricsJson),
            cancellationToken);
    }

    private async Task<VietsubSubtitleTrack> GetOrCreateOutputTrackAsync(
        VietsubLocalJob job,
        VietsubOcrJobParameters parameters,
        CancellationToken cancellationToken)
    {
        var tracks = await subtitleStore.LoadTracksAsync(job.ProjectId, cancellationToken);
        if (job.OutputTrackId is Guid outputTrackId)
        {
            return tracks.SingleOrDefault(track => track.TrackId == outputTrackId)
                ?? throw new VietsubOcrException(
                    VietsubOcrErrorCodes.JobNotResumable,
                    "Track đầu ra của OCR job không còn tồn tại.");
        }

        var now = DateTime.UtcNow;
        var track = new VietsubSubtitleTrack
        {
            TrackId = Guid.NewGuid(),
            DisplayName = $"OCR {parameters.LanguageCode.ToUpperInvariant()} {now:yyyy-MM-dd HH:mm}",
            LanguageCode = parameters.LanguageCode,
            Source = TrackSource,
            Revision = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        await subtitleStore.SaveTrackAsync(job.ProjectId, track, cancellationToken);
        await jobStore.BindOutputTrackAsync(job.ProjectId, job.Id, track.TrackId, cancellationToken);
        return track;
    }

    private async Task CommitAsync(
        VietsubJobExecutionContext context,
        VietsubSubtitleTrack track,
        VietsubOcrCueAccumulator accumulator,
        VietsubOcrJobParameters parameters,
        long timestampMilliseconds,
        int recognizedFrames,
        int reusedFrames,
        double progressPercent,
        string message,
        CancellationToken cancellationToken)
    {
        var completed = accumulator.DrainCompleted();
        var trackChanged = completed.Count > 0;
        if (completed.Count > 0)
        {
            MergeCues(track, completed, parameters.Profile.SampleIntervalMilliseconds, parameters.DurationSeconds);
            track.Revision++;
            track.UpdatedAtUtc = DateTime.UtcNow;
        }
        var checkpoint = CreateCheckpoint(
            parameters,
            accumulator,
            timestampMilliseconds,
            track,
            recognizedFrames,
            reusedFrames);
        var checkpointJson = JsonSerializer.Serialize(checkpoint, JsonOptions);
        if (trackChanged)
        {
            await subtitleStore.SaveTrackAndJobCheckpointAsync(
                context.Job.ProjectId,
                track,
                context.Job.Id,
                checkpointJson,
                cancellationToken);
        }
        await context.ReportProgressAsync(
            new VietsubJobProgressUpdate(
                "OCR_RECOGNIZE",
                Math.Clamp(progressPercent / 0.9, 0, 100),
                progressPercent,
                message,
                checkpointJson),
            cancellationToken);
    }

    private static VietsubOcrCheckpoint CreateCheckpoint(
        VietsubOcrJobParameters parameters,
        VietsubOcrCueAccumulator accumulator,
        long timestampMilliseconds,
        VietsubSubtitleTrack track,
        int recognizedFrames,
        int reusedFrames) =>
        new(
            parameters.StrategyVersion,
            timestampMilliseconds,
            timestampMilliseconds,
            track.Revision,
            recognizedFrames,
            reusedFrames,
            accumulator.Snapshot());

    private async Task SaveTrackRevisionAsync(
        Guid projectId,
        VietsubSubtitleTrack track,
        CancellationToken cancellationToken)
    {
        track.Revision++;
        track.UpdatedAtUtc = DateTime.UtcNow;
        await subtitleStore.SaveTrackAsync(projectId, track, cancellationToken);
    }

    private async Task WriteSrtArtifactAsync(
        Guid projectId,
        VietsubSubtitleTrack track,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.Combine("subtitles", $"ocr-{track.TrackId:N}.srt");
        var absolutePath = paths.GetProjectPath(projectId, relativePath);
        var bytes = new UTF8Encoding(false).GetBytes(
            VietsubSubtitleService.Serialize(track.Cues, preferTranslation: false));
        var partialPath = absolutePath + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        TryDelete(partialPath);
        try
        {
            await using (var stream = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(partialPath, absolutePath, overwrite: true);
        }
        finally
        {
            TryDelete(partialPath);
        }

        var now = DateTime.UtcNow;
        var artifact = track.Artifacts.SingleOrDefault(item =>
            item.ArtifactType == "SRT_ORIGINAL"
            && string.Equals(item.WorkspaceRelativePath, relativePath, StringComparison.Ordinal));
        if (artifact is null)
        {
            artifact = new VietsubSubtitleArtifact
            {
                ArtifactType = "SRT_ORIGINAL",
                WorkspaceRelativePath = relativePath,
                CreatedAtUtc = now
            };
            track.Artifacts.Add(artifact);
        }
        artifact.TrackRevision = track.Revision + 1;
        artifact.Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        artifact.Status = VietsubSubtitleArtifactStatuses.Ready;
        artifact.UpdatedAtUtc = now;
        await SaveTrackRevisionAsync(projectId, track, cancellationToken);
    }

    private static void MergeCues(
        VietsubSubtitleTrack track,
        IReadOnlyList<VietsubSubtitleCue> candidates,
        int sampleIntervalMilliseconds,
        decimal durationSeconds)
    {
        var maximumEnd = Math.Max(1L, (long)Math.Ceiling(durationSeconds * 1000));
        foreach (var candidate in candidates)
        {
            candidate.StartMilliseconds = Math.Clamp(candidate.StartMilliseconds, 0, maximumEnd - 1);
            candidate.EndMilliseconds = Math.Clamp(candidate.EndMilliseconds, candidate.StartMilliseconds + 1, maximumEnd);
            var duplicate = track.Cues.Any(existing =>
                Math.Abs(existing.StartMilliseconds - candidate.StartMilliseconds) <= sampleIntervalMilliseconds * 2L
                && VietsubOcrCueAccumulator.IsSimilar(
                    VietsubOcrCueAccumulator.NormalizeForComparison(existing.OriginalText),
                    VietsubOcrCueAccumulator.NormalizeForComparison(candidate.OriginalText)));
            if (!duplicate)
            {
                track.Cues.Add(candidate);
            }
        }
        track.Cues = track.Cues
            .OrderBy(cue => cue.StartMilliseconds)
            .ThenBy(cue => cue.EndMilliseconds)
            .ToList();
    }

    private static VietsubOcrJobParameters ParseAndValidateParameters(string json)
    {
        VietsubOcrJobParameters? parameters;
        try
        {
            parameters = JsonSerializer.Deserialize<VietsubOcrJobParameters>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.JobNotResumable,
                "Snapshot OCR job không hợp lệ.",
                exception);
        }
        if (parameters is null
            || parameters.StrategyVersion != VietsubOcrJobParameters.CurrentStrategyVersion
            || parameters.MediaId == Guid.Empty
            || parameters.SourceSha256 is null
            || parameters.SourceSha256.Length != 64
            || parameters.SourceSha256.Any(character => !Uri.IsHexDigit(character))
            || parameters.Profile is null
            || parameters.Region is null
            || parameters.DurationSeconds <= 0
            || parameters.SourceWidth <= 0
            || parameters.SourceHeight <= 0)
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.JobNotResumable,
                "Snapshot OCR job thiếu dữ liệu hoặc dùng strategy không được hỗ trợ.");
        }
        var language = VietsubOcrLanguageCodes.Normalize(parameters.LanguageCode);
        var profile = VietsubOcrProfile.Resolve(parameters.Profile.Name);
        var region = parameters.Region.Validate();
        return parameters with
        {
            LanguageCode = language,
            Profile = profile,
            Region = region,
            RotationDegrees = VietsubVideoRotation.Normalize(parameters.RotationDegrees)
        };
    }

    private static VietsubOcrCheckpoint? ParseCheckpoint(
        string? json,
        VietsubSubtitleTrack track)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            var checkpoint = JsonSerializer.Deserialize<VietsubOcrCheckpoint>(json, JsonOptions);
            return checkpoint is not null
                && checkpoint.StrategyVersion == VietsubOcrJobParameters.CurrentStrategyVersion
                && checkpoint.TrackRevision <= track.Revision
                    ? checkpoint
                    : throw new JsonException();
        }
        catch (JsonException exception)
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.JobNotResumable,
                "Checkpoint OCR không tương thích với track đầu ra.",
                exception);
        }
    }

    internal static string BuildProgressMessage(
        long processedFrames,
        long totalEstimatedFrames,
        int recognizedFrames,
        int reusedFrames,
        TimeSpan elapsed)
    {
        var rate = processedFrames / Math.Max(0.001, elapsed.TotalSeconds);
        var remainingSeconds = rate <= 0
            ? 0
            : Math.Max(0, totalEstimatedFrames - processedFrames) / rate;
        var eta = remainingSeconds >= 60
            ? $"còn ~{Math.Ceiling(remainingSeconds / 60):0} phút"
            : $"còn ~{Math.Ceiling(remainingSeconds):0} giây";
        return $"Đã quét {processedFrames}/{totalEstimatedFrames} frame; " +
               $"nhận dạng {recognizedFrames}, tái dùng {reusedFrames}; {eta}.";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

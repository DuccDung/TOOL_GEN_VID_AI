using System.Globalization;
using System.Security.Cryptography;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_LOCAL.Vietsub.Voice;

internal sealed record VietsubVoicePhraseAudio(
    VietsubVoicePhrase Phrase,
    VietsubVoiceArtifact Artifact,
    string AbsolutePath,
    VietsubWavMetadata Metadata,
    long TailFadeMilliseconds = 0);

internal sealed record VietsubVoiceTimelineRenderResult(
    string AbsolutePath,
    VietsubWavMetadata Metadata,
    IReadOnlyList<VietsubVoiceTimingDiagnostic> Diagnostics);

internal sealed partial class VietsubVoiceTimelineRenderer(
    VietsubAppPaths paths,
    IMediaToolPreflightService preflight,
    string ffmpegPath,
    IExternalProcessRunner processRunner)
{
    private const int MaximumInputsPerPartition = 80;

    public async Task<VietsubVoiceTimelineRenderResult> RenderAsync(
        Guid projectId,
        Guid jobId,
        Guid trackId,
        int trackRevision,
        IReadOnlyList<VietsubVoicePhraseAudio> phraseAudio,
        VietsubVoiceSettingsSnapshot settings,
        long requestedTimelineDurationMilliseconds,
        CancellationToken cancellationToken,
        Func<int, int, CancellationToken, Task>? segmentProgress = null)
    {
        if (phraseAudio.Count == 0)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.TranslationRequired, "Không có phrase tiếng Việt để dựng giọng đọc.");
        }

        var prepared = phraseAudio.ToArray();
        var diagnostics = new VietsubVoiceTimingDiagnostic[prepared.Length];
        for (var index = 0; index < prepared.Length; index++)
        {
            var item = prepared[index];
            var nextStart = index + 1 < phraseAudio.Count
                ? phraseAudio[index + 1].Phrase.StartMilliseconds
                : requestedTimelineDurationMilliseconds;
            if (item.Phrase.HardEndMilliseconds is { } hardEnd) nextStart = Math.Min(nextStart, hardEnd);
            var diagnostic = VietsubVoiceTimelineFitPolicy.Evaluate(item.Phrase, item.Metadata, nextStart, settings);
            if (item.Phrase.HardEndMilliseconds is { } boundary)
            {
                var available = Math.Max(0, boundary - item.Phrase.StartMilliseconds);
                var overflow = RenderedDuration(item.Metadata, diagnostic.Tempo) - available;
                if (overflow > 0 && settings.TrimSilence && item.Metadata.SignalEndMilliseconds is { } signalEnd)
                {
                    // Source WAV milliseconds, before atempo. Output overflow is not source trim.
                    var newEnd = item.Metadata.TrimStartMilliseconds + (long)Math.Floor(available * diagnostic.Tempo);
                    var reduction = item.Metadata.TrimEndMilliseconds - newEnd;
                    const int signalGuard = 5;
                    if (available > 0 && reduction is > 0 and <= 120 && newEnd >= signalEnd + signalGuard)
                    {
                        item = item with
                        {
                            Metadata = item.Metadata with { TrimEndMilliseconds = newEnd },
                            TailFadeMilliseconds = Math.Min(5, newEnd - signalEnd - signalGuard)
                        };
                        diagnostic = VietsubVoiceTimelineFitPolicy.Evaluate(item.Phrase, item.Metadata, nextStart, settings);
                    }
                }
                overflow = RenderedDuration(item.Metadata, diagnostic.Tempo) - available;
                if (overflow > 0)
                {
                    // A skipped cue excludes its own text from synthesis. Its start is
                    // a fitting target, not a reason to fail or cut the preceding speech.
                    diagnostic = diagnostic with { Status = VietsubVoiceTimingStatuses.ReviewRequired };
                }
            }
            prepared[index] = item;
            diagnostics[index] = diagnostic;
        }
        phraseAudio = prepared;
        var byId = diagnostics.ToDictionary(value => value.PhraseId, StringComparer.Ordinal);

        await preflight.RequireReadyAsync(cancellationToken);
        var renderedContentEnd = phraseAudio.Max(item =>
        {
            var diagnostic = byId[item.Phrase.PhraseId];
            var renderedDuration = (long)Math.Ceiling(item.Metadata.AudibleDurationMilliseconds / Math.Max(1d, diagnostic.Tempo));
            return checked(item.Phrase.StartMilliseconds + renderedDuration);
        });
        var timelineDuration = Math.Max(
            requestedTimelineDurationMilliseconds,
            checked(renderedContentEnd + 250));
        if (timelineDuration > VietsubWavInspector.MaximumTimelineMilliseconds)
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.TimelineFailed, "Timeline giọng vượt giới hạn thời lượng được hỗ trợ.");
        if (timelineDuration > SegmentMilliseconds || phraseAudio.Count > MaximumInputsPerPartition)
            return await RenderSegmentsAsync(projectId, jobId, trackId, trackRevision, phraseAudio, diagnostics,
                timelineDuration, cancellationToken, segmentProgress);
        var partitions = phraseAudio.Chunk(MaximumInputsPerPartition).Select(chunk => chunk.ToArray()).ToArray();
        EnsureDiskSpace(projectId, timelineDuration, partitions.Length + 2);
        var tempDirectory = paths.GetProjectPath(projectId, "temp", $"voice-{jobId:N}");
        var outputDirectory = paths.GetProjectPath(projectId, "voice", trackId.ToString("N"), $"revision-{trackRevision}");
        Directory.CreateDirectory(tempDirectory);
        Directory.CreateDirectory(outputDirectory);
        var stems = new List<string>();
        try
        {
            for (var index = 0; index < partitions.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stem = Path.Combine(tempDirectory, $"stem-{index:D4}.wav");
                await RenderPartitionAsync(partitions[index], byId, timelineDuration, stem, cancellationToken);
                _ = VietsubWavInspector.InspectTimeline(stem, timelineDuration, cancellationToken);
                stems.Add(stem);
            }

            var partial = Path.Combine(tempDirectory, "timeline.partial.wav");
            if (stems.Count == 1)
            {
                File.Copy(stems[0], partial, overwrite: true);
            }
            else
            {
                await MixStemsAsync(stems, timelineDuration, partial, cancellationToken);
            }
            var metadata = VietsubWavInspector.InspectTimeline(partial, timelineDuration, cancellationToken);
            var final = Path.Combine(outputDirectory, $"voice-timeline-{Guid.NewGuid():N}.wav");
            File.Move(partial, final);
            return new(final, metadata, diagnostics);
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private async Task RenderPartitionAsync(
        IReadOnlyList<VietsubVoicePhraseAudio> items,
        IReadOnlyDictionary<string, VietsubVoiceTimingDiagnostic> allDiagnostics,
        long timelineDurationMilliseconds,
        string outputPath,
        CancellationToken cancellationToken,
        long originMilliseconds = 0)
    {
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error" };
        foreach (var item in items)
        {
            arguments.Add("-i");
            arguments.Add(item.AbsolutePath);
        }
        var filters = new List<string>();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var diagnostic = allDiagnostics[item.Phrase.PhraseId];
            var trimStart = Seconds(item.Metadata.TrimStartMilliseconds);
            var trimEnd = Seconds(item.Metadata.TrimEndMilliseconds);
            var filter = $"[{index}:a:0]atrim=start={trimStart}:end={trimEnd},asetpts=PTS-STARTPTS,aresample=48000,aformat=sample_fmts=s16:channel_layouts=stereo";
            if (item.TailFadeMilliseconds > 0)
                filter += $",afade=t=out:st={Seconds(item.Metadata.AudibleDurationMilliseconds - item.TailFadeMilliseconds)}:d={Seconds(item.TailFadeMilliseconds)}";
            if (diagnostic.Tempo > 1.0001)
            {
                filter += $",atempo={diagnostic.Tempo.ToString("R", CultureInfo.InvariantCulture)}";
            }
            // adelay can emit leading frames without PTS after atempo. Rebuild the clock
            // from all samples, including silence, before atrim/amix can drop those frames.
            var offset = item.Phrase.StartMilliseconds - originMilliseconds;
            if (offset < 0) filter += $",atrim=start_sample={checked(-offset * 48)},asetpts=N/SR/TB";
            var delay = Math.Max(0, offset);
            filter += $",adelay={delay}|{delay},asetpts=N/SR/TB[a{index}]";
            filters.Add(filter);
        }
        var inputLabels = string.Concat(Enumerable.Range(0, items.Count).Select(index => $"[a{index}]"));
        filters.Add(items.Count == 1
            ? $"{inputLabels}{TimelineOutputFilter(timelineDurationMilliseconds)}[out]"
            : $"{inputLabels}amix=inputs={items.Count}:duration=longest:normalize=0,{TimelineOutputFilter(timelineDurationMilliseconds)}[out]");
        arguments.AddRange([
            "-filter_complex", string.Join(';', filters),
            "-map", "[out]",
            "-c:a", "pcm_s16le",
            "-ar", "48000",
            "-ac", "2",
            "-y", outputPath
        ]);
        await RunFfmpegAsync(arguments, outputPath, cancellationToken);
    }

    private async Task MixStemsAsync(
        IReadOnlyList<string> stems,
        long timelineDurationMilliseconds,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error" };
        foreach (var stem in stems)
        {
            arguments.Add("-i");
            arguments.Add(stem);
        }
        var labels = string.Concat(Enumerable.Range(0, stems.Count).Select(index => $"[{index}:a:0]"));
        var filter = $"{labels}amix=inputs={stems.Count}:duration=longest:normalize=0,{TimelineOutputFilter(timelineDurationMilliseconds)}[out]";
        arguments.AddRange([
            "-filter_complex", filter,
            "-map", "[out]",
            "-c:a", "pcm_s16le",
            "-ar", "48000",
            "-ac", "2",
            "-y", outputPath
        ]);
        await RunFfmpegAsync(arguments, outputPath, cancellationToken);
    }

    private async Task RunFfmpegAsync(
        IReadOnlyList<string> arguments,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var limitedArguments = new List<string> { "-filter_complex_threads", "2" };
        limitedArguments.AddRange(arguments);
        var result = await processRunner.RunAsync(ffmpegPath, limitedArguments, TimeSpan.FromMinutes(20), cancellationToken);
        if (result.ExitCode != 0 || !File.Exists(outputPath) || new FileInfo(outputPath).Length <= 44)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.TimelineFailed, "FFmpeg không thể dựng timeline giọng Việt.", true);
        }
    }

    private void EnsureDiskSpace(Guid projectId, long durationMilliseconds, int fileMultiplier)
    {
        var estimated = checked((long)Math.Ceiling(durationMilliseconds / 1000d * 48_000 * 2 * 2 * fileMultiplier));
        var root = Path.GetPathRoot(paths.GetProjectDirectory(projectId));
        if (root is null) return;
        var drive = new DriveInfo(root);
        const long safetyMargin = 512L * 1024 * 1024;
        if (drive.IsReady && drive.AvailableFreeSpace < estimated + safetyMargin)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.TimelineFailed, "Ổ đĩa không đủ chỗ để dựng timeline giọng Việt.");
        }
    }

    private static long RenderedDuration(VietsubWavMetadata metadata, double tempo) =>
        (long)Math.Ceiling(metadata.AudibleDurationMilliseconds / tempo - 1e-7);

    private static string TimelineOutputFilter(long durationMilliseconds)
    {
        // Bound padding by samples as well as rebuild PTS. Some FFmpeg filter combinations
        // propagate missing timestamps, so an unbounded apad + duration trim can keep writing.
        var samples = checked(durationMilliseconds * 48); // All stems are 48 kHz.
        return $"asetpts=N/SR/TB,apad=whole_len={samples},atrim=end_sample={samples},alimiter=limit=0.95:latency=1,asetpts=N/SR/TB";
    }

    private static string Seconds(long milliseconds) =>
        (milliseconds / 1000d).ToString("0.###", CultureInfo.InvariantCulture);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

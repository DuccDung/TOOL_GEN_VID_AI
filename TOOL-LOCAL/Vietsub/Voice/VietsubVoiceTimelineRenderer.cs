using System.Globalization;
using System.Security.Cryptography;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_LOCAL.Vietsub.Voice;

internal sealed record VietsubVoicePhraseAudio(
    VietsubVoicePhrase Phrase,
    VietsubVoiceArtifact Artifact,
    string AbsolutePath,
    VietsubWavMetadata Metadata);

internal sealed record VietsubVoiceTimelineRenderResult(
    string AbsolutePath,
    VietsubWavMetadata Metadata,
    IReadOnlyList<VietsubVoiceTimingDiagnostic> Diagnostics);

internal sealed class VietsubVoiceTimelineRenderer(
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
        CancellationToken cancellationToken)
    {
        if (phraseAudio.Count == 0)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.TranslationRequired, "Không có phrase tiếng Việt để dựng giọng đọc.");
        }

        var diagnostics = phraseAudio.Select((item, index) =>
        {
            var nextStart = index + 1 < phraseAudio.Count
                ? phraseAudio[index + 1].Phrase.StartMilliseconds
                : requestedTimelineDurationMilliseconds;
            return VietsubVoiceTimelineFitPolicy.Evaluate(item.Phrase, item.Metadata, nextStart, settings);
        }).ToArray();

        await preflight.RequireReadyAsync(cancellationToken);
        var renderedContentEnd = phraseAudio.Max(item =>
        {
            var diagnostic = diagnostics.Single(value => value.PhraseId == item.Phrase.PhraseId);
            var renderedDuration = (long)Math.Ceiling(item.Metadata.AudibleDurationMilliseconds / Math.Max(1d, diagnostic.Tempo));
            return checked(item.Phrase.StartMilliseconds + renderedDuration);
        });
        var timelineDuration = Math.Max(
            requestedTimelineDurationMilliseconds,
            checked(renderedContentEnd + 250));
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
                await RenderPartitionAsync(partitions[index], diagnostics, timelineDuration, stem, cancellationToken);
                _ = VietsubWavInspector.Inspect(stem, analyzeSilence: false);
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
            var metadata = VietsubWavInspector.Inspect(partial, analyzeSilence: false);
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
        IReadOnlyList<VietsubVoiceTimingDiagnostic> allDiagnostics,
        long timelineDurationMilliseconds,
        string outputPath,
        CancellationToken cancellationToken)
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
            var diagnostic = allDiagnostics.Single(value => value.PhraseId == item.Phrase.PhraseId);
            var trimStart = Seconds(item.Metadata.TrimStartMilliseconds);
            var trimEnd = Seconds(item.Metadata.TrimEndMilliseconds);
            var filter = $"[{index}:a:0]atrim=start={trimStart}:end={trimEnd},asetpts=PTS-STARTPTS,aresample=48000,aformat=sample_fmts=s16:channel_layouts=stereo";
            if (diagnostic.Tempo > 1.0001)
            {
                filter += $",atempo={diagnostic.Tempo.ToString("0.######", CultureInfo.InvariantCulture)}";
            }
            filter += $",adelay={item.Phrase.StartMilliseconds}|{item.Phrase.StartMilliseconds}[a{index}]";
            filters.Add(filter);
        }
        var inputLabels = string.Concat(Enumerable.Range(0, items.Count).Select(index => $"[a{index}]"));
        filters.Add(items.Count == 1
            ? $"{inputLabels}apad,atrim=duration={Seconds(timelineDurationMilliseconds)},alimiter=limit=0.95[out]"
            : $"{inputLabels}amix=inputs={items.Count}:duration=longest:normalize=0,alimiter=limit=0.95,apad,atrim=duration={Seconds(timelineDurationMilliseconds)}[out]");
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
        var filter = $"{labels}amix=inputs={stems.Count}:duration=longest:normalize=0,alimiter=limit=0.95,apad,atrim=duration={Seconds(timelineDurationMilliseconds)}[out]";
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
        var result = await processRunner.RunAsync(ffmpegPath, arguments, TimeSpan.FromMinutes(20), cancellationToken);
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

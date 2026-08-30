using System.Globalization;
using System.Text;

namespace TOOL_LOCAL.Media;

public sealed record FinalRenderManifest(
    IReadOnlyList<string> ScenePaths,
    string OutputPath,
    string WorkingDirectory,
    int Width,
    int Height,
    decimal FramesPerSecond,
    string? VoicePath = null,
    string? MusicPath = null,
    string? SubtitlePath = null,
    decimal MusicVolume = 0.15m);

public interface IFinalMediaRenderer
{
    Task RenderAsync(FinalRenderManifest manifest, CancellationToken cancellationToken = default);
}

public sealed class FfmpegRenderService(string ffmpegPath, IExternalProcessRunner processRunner) : IFinalMediaRenderer
{
    public async Task RenderAsync(FinalRenderManifest manifest, CancellationToken cancellationToken = default)
    {
        Validate(manifest);
        var workingDirectory = Path.GetFullPath(manifest.WorkingDirectory);
        Directory.CreateDirectory(workingDirectory);
        var normalizedFiles = new List<string>(manifest.ScenePaths.Count);

        for (var index = 0; index < manifest.ScenePaths.Count; index++)
        {
            var normalized = Path.Combine(workingDirectory, $"normalized_{index + 1:000}.mp4");
            await NormalizeSceneAsync(manifest.ScenePaths[index], normalized, manifest, cancellationToken);
            normalizedFiles.Add(normalized);
        }

        var concatList = Path.Combine(workingDirectory, "concat.txt");
        var concatContent = string.Join(
            Environment.NewLine,
            normalizedFiles.Select(path => $"file '{EscapeConcatPath(path)}'"));
        await File.WriteAllTextAsync(concatList, concatContent, new UTF8Encoding(false), cancellationToken);

        var concatenated = Path.Combine(workingDirectory, "concatenated.mp4");
        await RunFfmpegAsync(
            ["-y", "-f", "concat", "-safe", "0", "-i", concatList, "-c", "copy", concatenated],
            TimeSpan.FromMinutes(30),
            cancellationToken);

        var temporaryOutput = Path.Combine(workingDirectory, $"final_{Guid.NewGuid():N}.mp4");
        await CompositeAudioAndSubtitleAsync(concatenated, temporaryOutput, manifest, cancellationToken);

        var output = Path.GetFullPath(manifest.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.Move(temporaryOutput, output, true);
    }

    private Task NormalizeSceneAsync(
        string input,
        string output,
        FinalRenderManifest manifest,
        CancellationToken cancellationToken)
    {
        var filter = $"scale={manifest.Width}:{manifest.Height}:force_original_aspect_ratio=decrease," +
                     $"pad={manifest.Width}:{manifest.Height}:(ow-iw)/2:(oh-ih)/2:black," +
                     $"fps={Invariant(manifest.FramesPerSecond)},format=yuv420p";
        return RunFfmpegAsync(
            [
                "-y", "-i", Path.GetFullPath(input),
                "-map", "0:v:0", "-map", "0:a:0?", "-vf", filter,
                "-c:v", "libx264", "-preset", "medium", "-crf", "18",
                "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2",
                "-movflags", "+faststart", output
            ],
            TimeSpan.FromMinutes(20),
            cancellationToken);
    }

    private Task CompositeAudioAndSubtitleAsync(
        string video,
        string output,
        FinalRenderManifest manifest,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "-y", "-i", video };
        var voiceInput = AddOptionalInput(arguments, manifest.VoicePath, loop: false);
        var musicInput = AddOptionalInput(arguments, manifest.MusicPath, loop: true);
        var videoMap = "0:v:0";
        string? audioMap = null;

        if (voiceInput.HasValue && musicInput.HasValue)
        {
            var filter = $"[{voiceInput}:a]volume=1.0[voice];" +
                         $"[{musicInput}:a]volume={Invariant(manifest.MusicVolume)}[music];" +
                         "[voice][music]amix=inputs=2:duration=first:dropout_transition=2[aout]";
            arguments.AddRange(["-filter_complex", filter]);
            audioMap = "[aout]";
        }
        else if (voiceInput.HasValue)
        {
            audioMap = $"{voiceInput}:a:0";
        }
        else if (musicInput.HasValue)
        {
            var filter = "[0:a:0]aformat=sample_rates=48000:channel_layouts=stereo[scene];" +
                         $"[{musicInput}:a]aformat=sample_rates=48000:channel_layouts=stereo,volume={Invariant(manifest.MusicVolume)}[music];" +
                         "[scene][music]amix=inputs=2:duration=first:dropout_transition=2,alimiter=limit=0.95[aout]";
            arguments.AddRange(["-filter_complex", filter]);
            audioMap = "[aout]";
        }
        else
        {
            // SceneVideo already contains approved Kling Native Audio. Keep the
            // concatenated scene audio when no optional legacy voice/music input exists.
            audioMap = "0:a:0?";
        }

        if (!string.IsNullOrWhiteSpace(manifest.SubtitlePath))
        {
            arguments.AddRange(["-vf", $"subtitles=filename='{EscapeSubtitlePath(manifest.SubtitlePath)}'"]);
        }

        arguments.AddRange(["-map", videoMap]);
        if (audioMap is not null)
        {
            arguments.AddRange(["-map", audioMap, "-c:a", "aac", "-b:a", "192k", "-shortest"]);
        }

        arguments.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart", output]);
        return RunFfmpegAsync(arguments, TimeSpan.FromMinutes(60), cancellationToken);
    }

    private async Task RunFfmpegAsync(
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(ffmpegPath, arguments, timeout, cancellationToken);
        if (result.ExitCode != 0)
        {
            var error = result.StandardError.Length <= 4000 ? result.StandardError : result.StandardError[..4000];
            throw new InvalidDataException($"FFmpeg thất bại với exit code {result.ExitCode}: {error.Trim()}");
        }
    }

    private static int? AddOptionalInput(List<string> arguments, string? path, bool loop)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var inputIndex = arguments.Count(x => x == "-i");
        if (loop)
        {
            arguments.AddRange(["-stream_loop", "-1"]);
        }

        arguments.AddRange(["-i", Path.GetFullPath(path)]);
        return inputIndex;
    }

    private static void Validate(FinalRenderManifest manifest)
    {
        if (manifest.ScenePaths.Count == 0)
        {
            throw new ArgumentException("Render manifest phải có ít nhất một scene.", nameof(manifest));
        }

        foreach (var path in manifest.ScenePaths.Append(manifest.VoicePath).Append(manifest.MusicPath).Append(manifest.SubtitlePath))
        {
            if (!string.IsNullOrWhiteSpace(path) && !File.Exists(Path.GetFullPath(path)))
            {
                throw new FileNotFoundException("Không tìm thấy render asset.", path);
            }
        }

        if (manifest.Width <= 0 || manifest.Height <= 0 || manifest.FramesPerSecond is <= 0 or > 120)
        {
            throw new ArgumentException("Thông số output video không hợp lệ.", nameof(manifest));
        }

        if (manifest.MusicVolume is < 0 or > 1)
        {
            throw new ArgumentException("Music volume phải nằm trong khoảng 0–1.", nameof(manifest));
        }
    }

    private static string EscapeConcatPath(string path) =>
        Path.GetFullPath(path).Replace("'", "'\\''");

    private static string EscapeSubtitlePath(string path) =>
        Path.GetFullPath(path)
            .Replace('\\', '/')
            .Replace(":", "\\:")
            .Replace("'", "\\'");

    private static string Invariant(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

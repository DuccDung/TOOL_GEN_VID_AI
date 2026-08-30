using System.Globalization;

namespace TOOL_LOCAL.Media;

public sealed class SceneVideoTrimmer(
    string ffmpegPath,
    IExternalProcessRunner processRunner)
{
    public async Task TrimAsync(
        string inputPath,
        string outputPath,
        int durationSeconds,
        CancellationToken cancellationToken = default,
        bool includeAudio = true)
    {
        if (durationSeconds is < 1 or > 15)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationSeconds),
                "Thời lượng clip sau khi cắt phải nằm trong khoảng 1–15 giây.");
        }

        var (input, output) = ResolvePaths(inputPath, outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var duration = durationSeconds.ToString(CultureInfo.InvariantCulture);
        var arguments = new List<string>
        {
            "-y", "-i", input,
            "-t", duration,
            "-map", "0:v:0",
            "-c:v", "libx264", "-preset", "medium", "-crf", "18"
        };
        if (includeAudio)
        {
            arguments.AddRange([
                "-map", "0:a:0?",
                "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2"
            ]);
        }
        else
        {
            arguments.Add("-an");
        }
        arguments.AddRange([
            "-avoid_negative_ts", "make_zero", "-movflags", "+faststart",
            "-f", "mp4", output
        ]);
        var result = await processRunner.RunAsync(
            ffmpegPath,
            arguments,
            TimeSpan.FromMinutes(20),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            var error = result.StandardError.Length <= 4000
                ? result.StandardError
                : result.StandardError[..4000];
            throw new InvalidDataException($"FFmpeg không cắt được clip theo thời lượng đã chọn: {error.Trim()}");
        }
    }

    public async Task StripAudioAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var (input, output) = ResolvePaths(inputPath, outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var result = await processRunner.RunAsync(
            ffmpegPath,
            [
                "-y", "-i", input,
                "-map", "0:v:0", "-c:v", "copy", "-an",
                "-movflags", "+faststart", "-f", "mp4", output
            ],
            TimeSpan.FromMinutes(20),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            var error = result.StandardError.Length <= 4000
                ? result.StandardError
                : result.StandardError[..4000];
            throw new InvalidDataException($"FFmpeg không loại bỏ được âm thanh khỏi clip: {error.Trim()}");
        }
    }

    private static (string Input, string Output) ResolvePaths(string inputPath, string outputPath)
    {
        var input = Path.GetFullPath(inputPath);
        var output = Path.GetFullPath(outputPath);
        if (!File.Exists(input))
        {
            throw new FileNotFoundException("Không tìm thấy clip provider cần xử lý.", input);
        }
        if (input.Equals(output, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Đường dẫn đầu vào và đầu ra của xử lý media phải khác nhau.", nameof(outputPath));
        }
        return (input, output);
    }
}

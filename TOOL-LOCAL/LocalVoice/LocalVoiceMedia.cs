using System.Globalization;
using TOOL_LOCAL.Media;

namespace TOOL_LOCAL.LocalVoice;

internal interface ILocalVoiceMedia
{
    Task PrepareAsync(string video, string wav, long durationMs, CancellationToken token);
    Task<MediaProbeResult> RemuxAsync(string video, string converted, string output, long durationMs, CancellationToken token);
}

internal sealed class LocalVoiceMedia(string ffmpegPath, IExternalProcessRunner runner,
    FfprobeService probe, AudioQualityValidator audio) : ILocalVoiceMedia
{
    public async Task PrepareAsync(string video, string wav, long durationMs, CancellationToken token)
    {
        var input = await probe.ProbeAsync(video, token);
        var seconds = durationMs / 1000m;
        if (!input.HasVideo || !input.HasAudio || seconds is < 1 or > 8.5m || input.DurationSeconds < seconds - 0.15m)
            throw new InvalidDataException("Clip Veo không có đủ hình, tiếng hoặc thời lượng cần thiết.");
        await audio.RequireAudibleAsync(video, "Clip Veo không có âm thanh nghe được", token);
        await RunAsync(["-y", "-i", video, "-vn", "-t", seconds.ToString(CultureInfo.InvariantCulture),
            "-ar", "48000", "-ac", "2", "-c:a", "pcm_s16le", "-f", "wav", wav + ".part"], token);
        await audio.RequireAudibleAsync(wav + ".part", "Audio chuẩn bị không nghe được", token);
        File.Move(wav + ".part", wav, true);
    }

    public async Task<MediaProbeResult> RemuxAsync(string video, string converted, string output, long durationMs, CancellationToken token)
    {
        var seconds = durationMs / 1000m;
        var source = await probe.ProbeAsync(video, token);
        var speech = await probe.ProbeAsync(converted, token);
        if (!speech.HasAudio || Math.Abs(speech.DurationSeconds - seconds) > 0.10m)
            throw new InvalidDataException("Thời lượng lời sau chuyển giọng lệch khỏi clip nguồn.");
        await RunAsync(["-y", "-i", video, "-i", converted, "-map", "0:v:0", "-map", "1:a:0",
            "-c:v", "copy", "-af", "loudnorm=I=-16:LRA=11:TP=-1.5", "-c:a", "aac", "-b:a", "192k",
            "-ar", "48000", "-ac", "2", "-t", seconds.ToString(CultureInfo.InvariantCulture),
            "-movflags", "+faststart", "-f", "mp4", output + ".part"], token);
        var result = await probe.ProbeAsync(output + ".part", token);
        if (!result.HasVideo || !result.HasAudio || result.Width != source.Width || result.Height != source.Height ||
            result.VideoCodec != source.VideoCodec || Math.Abs(result.DurationSeconds - seconds) > 0.15m)
            throw new InvalidDataException("Clip đổi giọng không giữ đúng hình ảnh hoặc thời lượng.");
        await audio.RequireAudibleAsync(output + ".part", "Clip đổi giọng không nghe được", token);
        File.Move(output + ".part", output, true);
        return result;
    }

    private async Task RunAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var result = await runner.RunAsync(ffmpegPath, args, TimeSpan.FromMinutes(10), token);
        if (result.ExitCode != 0) throw new InvalidDataException("FFmpeg không hoàn tất xử lý giọng local. Kiểm tra media và thử lại.");
    }
}

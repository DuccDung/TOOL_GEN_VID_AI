using TOOL_LOCAL.Media;

namespace TOOL_TESTS.Media;

public sealed class SceneAudioMixerIntegrationTests
{
    [Fact]
    public async Task MixAsync_AddsAudibleVoiceWhenKlingClipHasNoAudio()
    {
        var tools = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
        var ffprobe = Path.Combine(tools, "ffprobe.exe");
        Assert.True(File.Exists(ffmpeg), "Test output must include the licensed FFmpeg bundle.");
        Assert.True(File.Exists(ffprobe), "Test output must include the licensed FFprobe bundle.");

        var root = Path.Combine(Path.GetTempPath(), $"videomaker-audio-mix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var rawVideo = Path.Combine(root, "raw.mp4");
            var voice = Path.Combine(root, "voice.wav");
            var output = Path.Combine(root, "narrated.mp4.part");
            var runner = new ExternalProcessRunner();
            await RequireFfmpegSuccessAsync(runner, ffmpeg,
                [
                    "-y", "-f", "lavfi", "-i", "color=c=blue:s=320x180:r=25:d=2",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", rawVideo
                ]);
            await RequireFfmpegSuccessAsync(runner, ffmpeg,
                [
                    "-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=24000:duration=1.5",
                    "-af", "volume=0.5", "-c:a", "pcm_s16le", voice
                ]);

            var probe = new FfprobeService(ffprobe, runner);
            var validator = new AudioQualityValidator(ffmpeg, runner, probe);
            var mixer = new SceneAudioMixer(ffmpeg, runner, probe, validator);

            var result = await mixer.MixAsync(
                rawVideo,
                voice,
                output,
                2m,
                mixStrategy: TOOL_SHARED.Contracts.Generation.SpeechMixStrategies.MixWithVerifiedAmbience,
                nativeAmbienceVerifiedNoSpeech: true);

            Assert.False(result.PreservedNativeAudio);
            Assert.True(result.OutputProbe.HasVideo);
            Assert.True(result.OutputProbe.HasAudio);
            Assert.Equal("h264", result.OutputProbe.VideoCodec);
            Assert.Equal("aac", result.OutputProbe.AudioCodec);
            Assert.True(result.OutputAudioQuality.IsAudible);
            Assert.True(File.Exists(output));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MixAsync_PreservesAndDucksAudibleNativeAudio()
    {
        var tools = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
        var ffprobe = Path.Combine(tools, "ffprobe.exe");
        var root = Path.Combine(Path.GetTempPath(), $"videomaker-audio-duck-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var rawVideo = Path.Combine(root, "raw-native.mp4");
            var voice = Path.Combine(root, "voice.wav");
            var output = Path.Combine(root, "narrated-native.mp4.part");
            var runner = new ExternalProcessRunner();
            await RequireFfmpegSuccessAsync(runner, ffmpeg,
                [
                    "-y", "-f", "lavfi", "-i", "color=c=green:s=320x180:r=25:d=2",
                    "-f", "lavfi", "-i", "sine=frequency=220:sample_rate=48000:duration=2",
                    "-map", "0:v:0", "-map", "1:a:0", "-c:v", "libx264", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-shortest", rawVideo
                ]);
            await RequireFfmpegSuccessAsync(runner, ffmpeg,
                [
                    "-y", "-f", "lavfi", "-i", "sine=frequency=660:sample_rate=24000:duration=1.5",
                    "-af", "volume=0.5", "-c:a", "pcm_s16le", voice
                ]);
            var probe = new FfprobeService(ffprobe, runner);
            var validator = new AudioQualityValidator(ffmpeg, runner, probe);
            var mixer = new SceneAudioMixer(ffmpeg, runner, probe, validator);

            var result = await mixer.MixAsync(
                rawVideo,
                voice,
                output,
                2m,
                mixStrategy: TOOL_SHARED.Contracts.Generation.SpeechMixStrategies.MixWithVerifiedAmbience,
                nativeAmbienceVerifiedNoSpeech: true);

            Assert.True(result.PreservedNativeAudio);
            Assert.True(result.OutputAudioQuality.IsAudible);
            Assert.Equal("aac", result.OutputProbe.AudioCodec);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MixAsync_ReplaceAllNativeAudio_DoesNotPreserveAudibleNativeTrack()
    {
        var tools = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
        var ffprobe = Path.Combine(tools, "ffprobe.exe");
        var root = Path.Combine(Path.GetTempPath(), $"videomaker-audio-replace-native-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var rawVideo = Path.Combine(root, "raw-native-speech.mp4");
            var voice = Path.Combine(root, "canonical-voice.wav");
            var output = Path.Combine(root, "narrated.mp4.part");
            var runner = new ExternalProcessRunner();
            await RequireFfmpegSuccessAsync(runner, ffmpeg,
                [
                    "-y", "-f", "lavfi", "-i", "color=c=yellow:s=320x180:r=25:d=2",
                    "-f", "lavfi", "-i", "sine=frequency=220:sample_rate=48000:duration=2",
                    "-map", "0:v:0", "-map", "1:a:0", "-c:v", "libx264", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-shortest", rawVideo
                ]);
            await RequireFfmpegSuccessAsync(runner, ffmpeg,
                [
                    "-y", "-f", "lavfi", "-i", "sine=frequency=660:sample_rate=24000:duration=1.5",
                    "-af", "volume=0.5", "-c:a", "pcm_s16le", voice
                ]);
            var probe = new FfprobeService(ffprobe, runner);
            var validator = new AudioQualityValidator(ffmpeg, runner, probe);
            var mixer = new SceneAudioMixer(ffmpeg, runner, probe, validator);

            var result = await mixer.MixAsync(
                rawVideo,
                voice,
                output,
                2m,
                mixStrategy: TOOL_SHARED.Contracts.Generation.SpeechMixStrategies.ReplaceAllNativeAudio);

            Assert.False(result.PreservedNativeAudio);
            Assert.Equal(TOOL_SHARED.Contracts.Generation.SpeechMixStrategies.ReplaceAllNativeAudio, result.MixStrategy);
            Assert.Equal(48_000, result.OutputProbe.AudioSampleRate);
            Assert.True(result.OutputAudioQuality.IsAudible);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MixAsync_ReplacesEffectivelySilentNativeAudioWithVoice()
    {
        var tools = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
        var ffprobe = Path.Combine(tools, "ffprobe.exe");
        var root = Path.Combine(Path.GetTempPath(), $"videomaker-audio-silent-native-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var rawVideo = Path.Combine(root, "raw-silent.mp4");
            var voice = Path.Combine(root, "voice.wav");
            var output = Path.Combine(root, "narrated-silent-native.mp4.part");
            var runner = new ExternalProcessRunner();
            await RequireFfmpegSuccessAsync(runner, ffmpeg,
                [
                    "-y", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=25:d=2",
                    "-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000",
                    "-map", "0:v:0", "-map", "1:a:0", "-c:v", "libx264", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-t", "2", rawVideo
                ]);
            await RequireFfmpegSuccessAsync(runner, ffmpeg,
                [
                    "-y", "-f", "lavfi", "-i", "sine=frequency=550:sample_rate=24000:duration=1.5",
                    "-af", "volume=0.5", "-c:a", "pcm_s16le", voice
                ]);

            var probe = new FfprobeService(ffprobe, runner);
            var validator = new AudioQualityValidator(ffmpeg, runner, probe);
            var mixer = new SceneAudioMixer(ffmpeg, runner, probe, validator);

            var result = await mixer.MixAsync(rawVideo, voice, output, 2m);

            Assert.False(result.PreservedNativeAudio);
            Assert.True(result.OutputAudioQuality.IsAudible);
            Assert.True(result.OutputProbe.HasAudio);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MixAsync_UsesAsrSpeechWindowAndPlacesSpeechAtConfiguredLeadIn()
    {
        var tools = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
        var ffprobe = Path.Combine(tools, "ffprobe.exe");
        var root = Path.Combine(Path.GetTempPath(), $"videomaker-audio-timeline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var rawVideo = Path.Combine(root, "raw.mp4");
            var voice = Path.Combine(root, "voice.wav");
            var output = Path.Combine(root, "narrated.mp4.part");
            var runner = new ExternalProcessRunner();
            await RequireFfmpegSuccessAsync(runner, ffmpeg,
                [
                    "-y", "-f", "lavfi", "-i", "color=c=navy:s=320x180:r=25:d=2",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", rawVideo
                ]);
            await RequireFfmpegSuccessAsync(runner, ffmpeg,
                [
                    "-y", "-f", "lavfi", "-i", "sine=frequency=520:sample_rate=24000:duration=2",
                    "-af", "volume=0.5", "-c:a", "pcm_s16le", voice
                ]);
            var probe = new FfprobeService(ffprobe, runner);
            var validator = new AudioQualityValidator(ffmpeg, runner, probe);
            var mixer = new SceneAudioMixer(
                ffmpeg,
                runner,
                probe,
                validator,
                new SceneAudioMixerOptions
                {
                    TargetSpeechLeadInMs = 150,
                    SpeechBoundaryPaddingMs = 80
                });

            var result = await mixer.MixAsync(
                rawVideo,
                voice,
                output,
                2m,
                sourceSpeechStartMs: 80,
                sourceSpeechEndMs: 1_920);

            Assert.Equal(80, result.SourceSpeechStartMs);
            Assert.Equal(1_920, result.SourceSpeechEndMs);
            Assert.InRange(result.TargetSpeechStartMs, 149, 151);
            Assert.Equal(2m, result.EffectiveVoiceDurationSeconds);
            Assert.InRange(result.AppliedTempo, 1.03m, 1.05m);
            Assert.InRange(result.TrailingPaddingSeconds, 0m, 0.001m);
            Assert.True(result.OutputProbe.HasAudio);
            Assert.True(result.OutputAudioQuality.IsAudible);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MixAsync_RejectsVoiceThatWouldRequireTempoAboveQualityLimit()
    {
        var tools = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
        var ffprobe = Path.Combine(tools, "ffprobe.exe");
        var root = Path.Combine(Path.GetTempPath(), $"videomaker-audio-tempo-limit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var rawVideo = Path.Combine(root, "raw.mp4");
            var voice = Path.Combine(root, "voice-too-long.wav");
            var output = Path.Combine(root, "narrated.mp4.part");
            var runner = new ExternalProcessRunner();
            await RequireFfmpegSuccessAsync(runner, ffmpeg,
                [
                    "-y", "-f", "lavfi", "-i", "color=c=red:s=320x180:r=25:d=2",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", rawVideo
                ]);
            await RequireFfmpegSuccessAsync(runner, ffmpeg,
                [
                    "-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=24000:duration=4.2",
                    "-af", "volume=0.5", "-c:a", "pcm_s16le", voice
                ]);

            var probe = new FfprobeService(ffprobe, runner);
            var validator = new AudioQualityValidator(ffmpeg, runner, probe);
            var mixer = new SceneAudioMixer(ffmpeg, runner, probe, validator);

            var exception = await Assert.ThrowsAsync<SpeechDurationOutOfRangeException>(
                () => mixer.MixAsync(rawVideo, voice, output, 2m));

            Assert.Contains("vượt ngưỡng điều chỉnh tempo", exception.Message);
            Assert.False(File.Exists(output));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ValidateVoiceDuration_RejectsAbnormallyShortVoiceUsingConfiguredRatio()
    {
        var mixer = new SceneAudioMixer(
            "ffmpeg-not-used",
            null!,
            null!,
            null!,
            new SceneAudioMixerOptions
            {
                MaximumTempoAdjustmentRatio = 0.05m,
                MinimumVoiceDurationRatio = 0.25m,
                TargetLoudnessLufs = -16m
            });

        var exception = Assert.Throws<SpeechDurationOutOfRangeException>(() =>
            mixer.ValidateVoiceDuration(1m, 10m));

        Assert.Equal(0.25m, exception.MinimumVoiceDurationRatio);
        Assert.Contains("tỷ lệ tối thiểu 25", exception.Message);
    }

    private static async Task RequireFfmpegSuccessAsync(
        IExternalProcessRunner runner,
        string ffmpeg,
        IReadOnlyList<string> arguments)
    {
        var result = await runner.RunAsync(ffmpeg, arguments, TimeSpan.FromMinutes(2));
        Assert.True(result.ExitCode == 0, result.StandardError);
    }
}

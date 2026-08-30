using TOOL_LOCAL.Media;

namespace TOOL_TESTS.Media;

public sealed class AudioQualityValidatorTests
{
    [Fact]
    public void ParseDiagnostics_AcceptsAudibleTrack()
    {
        const string diagnostics = """
            [silencedetect] silence_duration: 0.20
            [Parsed_volumedetect] mean_volume: -21.4 dB
            [Parsed_volumedetect] max_volume: -2.1 dB
            """;

        var result = AudioQualityValidator.ParseDiagnostics(diagnostics, 10m);

        Assert.True(result.HasAudioStream);
        Assert.True(result.IsAudible);
        Assert.Equal(-21.4m, result.MeanVolumeDb);
        Assert.Equal(-2.1m, result.MaxVolumeDb);
        Assert.Equal(0.02m, result.SilentRatio);
        Assert.Null(result.FailureCode);
    }

    [Fact]
    public void ParseDiagnostics_RejectsAudioStreamThatIsEffectivelySilent()
    {
        const string diagnostics = """
            [silencedetect] silence_duration: 9.98
            [Parsed_volumedetect] mean_volume: -64.0 dB
            [Parsed_volumedetect] max_volume: -49.2 dB
            """;

        var result = AudioQualityValidator.ParseDiagnostics(diagnostics, 10m);

        Assert.True(result.HasAudioStream);
        Assert.False(result.IsAudible);
        Assert.Equal("audio_effectively_silent", result.FailureCode);
        Assert.Equal(0.998m, result.SilentRatio);
    }

    [Fact]
    public void ParseDiagnostics_RejectsMissingVolumeStatistics()
    {
        var result = AudioQualityValidator.ParseDiagnostics("ffmpeg completed", 10m);

        Assert.True(result.HasAudioStream);
        Assert.False(result.IsAudible);
        Assert.Equal("audio_levels_unavailable", result.FailureCode);
    }
}

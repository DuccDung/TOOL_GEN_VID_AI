using TOOL_LOCAL.Media;

namespace TOOL_TESTS.Media;

public sealed class FfprobeServiceTests
{
    [Fact]
    public void ParseOutput_ReadsNativeAudioStreamAndRealMediaProperties()
    {
        const string json = """
            {
              "streams": [
                { "codec_type": "video", "codec_name": "h264", "width": 1280, "height": 720, "avg_frame_rate": "30000/1001" },
                { "codec_type": "audio", "codec_name": "aac", "sample_rate": "48000" }
              ],
              "format": { "duration": "5.125" }
            }
            """;

        var result = FfprobeService.ParseOutput(json);

        Assert.True(result.HasVideo);
        Assert.True(result.HasAudio);
        Assert.Equal("h264", result.VideoCodec);
        Assert.Equal("aac", result.AudioCodec);
        Assert.Equal(48_000, result.AudioSampleRate);
        Assert.Equal(1280, result.Width);
        Assert.Equal(720, result.Height);
        Assert.Equal(5.125m, result.DurationSeconds);
        Assert.InRange(result.FramesPerSecond!.Value, 29.96m, 29.98m);
    }

    [Fact]
    public void ParseOutput_MarksMissingAudioWithoutRejectingVideo()
    {
        const string json = """
            {
              "streams": [
                { "codec_type": "video", "codec_name": "h264", "width": 720, "height": 1280, "avg_frame_rate": "30/1" }
              ],
              "format": { "duration": "5" }
            }
            """;

        var result = FfprobeService.ParseOutput(json);

        Assert.True(result.HasVideo);
        Assert.False(result.HasAudio);
        Assert.Null(result.AudioCodec);
        Assert.Null(result.AudioSampleRate);
    }

    [Theory]
    [InlineData("-90", 270)]
    [InlineData("90", 90)]
    [InlineData("270", 270)]
    public void ParseOutput_ReadsAndNormalizesDisplayRotation(string rotation, int expected)
    {
        var json = $$"""
            {
              "streams": [
                {
                  "codec_type": "video",
                  "width": 1920,
                  "height": 1080,
                  "side_data_list": [{ "rotation": "{{rotation}}" }]
                }
              ],
              "format": { "duration": "5" }
            }
            """;

        var result = FfprobeService.ParseOutput(json);

        Assert.Equal(expected, result.RotationDegrees);
    }
}

using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class VideoDurationAllocatorTests
{
    [Fact]
    public void Parse_PreservesExactProviderDurations()
    {
        var capabilities = VideoModelCapabilities.Parse(
            """{"durations":[4,6,8],"resolutions":["720p"],"aspectRatios":["16:9","9:16"],"nativeAudio":true,"referenceImage":true}""",
            "fal");

        Assert.Equal([4, 6, 8], capabilities.AllowedDurationsSeconds.Order());
        Assert.False(capabilities.AllowedDurationsSeconds.Contains(5));
        Assert.False(capabilities.AllowedDurationsSeconds.Contains(7));
    }

    [Fact]
    public void Allocate_SeventyFiveSeconds_KeepsContentTargetAndUsesOnlyVeoDurations()
    {
        var capabilities = VideoModelCapabilities.Parse(
            """{"durations":[4,6,8],"resolutions":["720p"],"aspectRatios":["16:9","9:16"],"nativeAudio":true,"referenceImage":true}""",
            "fal");

        var result = VideoDurationAllocator.Allocate(75, capabilities);

        Assert.Equal(75, result.Sum(x => x.ContentDurationSeconds));
        Assert.Equal(76, result.Sum(x => x.GenerationDurationSeconds));
        Assert.Equal(1, result.Sum(x => x.TailTrimSeconds));
        Assert.All(result, scene => Assert.Contains(scene.GenerationDurationSeconds, new[] { 4, 6, 8 }));
        Assert.All(result, scene => Assert.InRange(scene.ContentDurationSeconds, 1, scene.GenerationDurationSeconds));
    }

    [Fact]
    public void Allocate_ContinuousKlingRange_RemainsBackwardCompatible()
    {
        var result = VideoDurationAllocator.Allocate(75, VideoModelCapabilities.KlingDefault);

        Assert.Equal(75, result.Sum(x => x.ContentDurationSeconds));
        Assert.Equal(75, result.Sum(x => x.GenerationDurationSeconds));
        Assert.All(result, scene => Assert.Equal(0, scene.TailTrimSeconds));
    }
}

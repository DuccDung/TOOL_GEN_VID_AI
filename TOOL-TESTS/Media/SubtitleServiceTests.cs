using TOOL_LOCAL.Media;

namespace TOOL_TESTS.Media;

public sealed class SubtitleServiceTests
{
    [Fact]
    public void CreateSrt_FormatsVietnameseCues()
    {
        var cues = new[]
        {
            new SubtitleCue(1, TimeSpan.Zero, TimeSpan.FromSeconds(3), "Bạn có biết cơ thể đang cảnh báo điều này?"),
            new SubtitleCue(2, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(6250), "Đừng bỏ qua dấu hiệu đầu tiên.")
        };

        var result = new SubtitleService().CreateSrt(cues);

        Assert.Contains("00:00:00,000 --> 00:00:03,000", result);
        Assert.Contains("00:00:03,000 --> 00:00:06,250", result);
        Assert.Contains("Bạn có biết", result);
    }

    [Fact]
    public void CreateSrt_RejectsOverlappingCues()
    {
        var cues = new[]
        {
            new SubtitleCue(1, TimeSpan.Zero, TimeSpan.FromSeconds(3), "Một"),
            new SubtitleCue(2, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "Hai")
        };

        Assert.Throws<ArgumentException>(() => new SubtitleService().CreateSrt(cues));
    }
}

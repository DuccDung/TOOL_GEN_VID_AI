using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class SpeechTranscriptComparerTests
{
    [Fact]
    public void Compare_NormalizesVietnameseCaseWhitespaceAndPunctuation()
    {
        var result = SpeechTranscriptComparer.Compare(
            "Xin chào, Việt Nam!",
            "  XIN CHÀO Việt Nam  ");

        Assert.Equal(0m, result.WordErrorRate);
        Assert.Equal(0m, result.CharacterErrorRate);
        Assert.Equal(1m, result.RequiredTermRecall);
    }

    [Fact]
    public void Compare_ReportsMissingRequiredWord()
    {
        var result = SpeechTranscriptComparer.Compare(
            "VideoMaker đồng bộ lời nói chính xác",
            "VideoMaker đồng bộ lời nói");

        Assert.True(result.WordErrorRate > 0m);
        Assert.True(result.CharacterErrorRate > 0m);
        Assert.True(result.RequiredTermRecall < 1m);
    }
}

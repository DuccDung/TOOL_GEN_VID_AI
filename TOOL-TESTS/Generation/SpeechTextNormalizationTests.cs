using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Generation;

public sealed class SpeechTextNormalizationTests
{
    [Fact]
    public void Normalize_CanonicalizesVietnameseUnicodeAndWhitespace()
    {
        const string composed = "Tiếng Việt rất đẹp";
        const string decomposed = "Tiếng\tViệt\r\n  rất   đẹp";

        Assert.Equal(composed, SpeechTextNormalization.Normalize(decomposed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    public void Normalize_EmptyInput_ReturnsEmpty(string? value)
    {
        Assert.Equal(string.Empty, SpeechTextNormalization.Normalize(value));
    }
}

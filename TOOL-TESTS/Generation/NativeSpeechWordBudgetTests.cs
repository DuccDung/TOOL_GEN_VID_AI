using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Generation;

public sealed class NativeSpeechWordBudgetTests
{
    [Theory]
    [InlineData(3, 8)]
    [InlineData(5, 8)]
    [InlineData(6, 18)]
    [InlineData(10, 18)]
    [InlineData(11, 28)]
    [InlineData(15, 28)]
    [InlineData(20, 38)]
    public void MaximumWordsForDurationSeconds_UsesTheSharedNativeAudioRule(
        int durationSeconds,
        int expectedMaximumWords)
    {
        Assert.Equal(expectedMaximumWords, NativeSpeechWordBudget.MaximumWordsForDurationSeconds(durationSeconds));
    }

    [Fact]
    public void CountWords_UsesWhitespaceSeparatedWords()
    {
        Assert.Equal(3, NativeSpeechWordBudget.CountWords("  Mot\t hai\r\nba "));
        Assert.Equal(0, NativeSpeechWordBudget.CountWords(" \r\n "));
    }
}

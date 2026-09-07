using TOOL_SERVER.Generation;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Generation;

public sealed class OpenAiBuiltInVoiceCatalogTests
{
    private static readonly string[] ExpectedVoiceCodes =
    [
        "alloy", "ash", "ballad", "coral", "echo", "fable", "onyx",
        "nova", "sage", "shimmer", "verse", "marin", "cedar"
    ];

    [Fact]
    public void Catalog_ContainsEveryCurrentBuiltInVoiceExactlyOnce()
    {
        var voices = OpenAiBuiltInVoiceCatalog.Voices;

        Assert.Equal(ExpectedVoiceCodes, voices.Select(voice => voice.VoiceCode));
        Assert.Equal(13, voices.Select(voice => voice.VoiceCode).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(voices, voice => Assert.False(string.IsNullOrWhiteSpace(voice.DisplayName)));
    }

    [Fact]
    public void DefaultServerOptions_ResolveEveryCatalogVoiceWithoutChangingItsProviderCode()
    {
        var options = new OpenAiSpeechOptions();

        options.Validate();
        Assert.All(
            OpenAiBuiltInVoiceCatalog.Voices,
            voice => Assert.Equal(voice.VoiceCode, options.ResolveProviderVoice(voice.VoiceCode)));
    }

    [Theory]
    [InlineData("female-sweet", "shimmer")]
    [InlineData("male-warm", "onyx")]
    public void LegacyAliases_RemainSupported(string legacyVoiceCode, string providerVoiceCode)
    {
        var options = new OpenAiSpeechOptions();

        Assert.True(OpenAiBuiltInVoiceCatalog.IsSupported(legacyVoiceCode));
        Assert.Equal(providerVoiceCode, options.ResolveProviderVoice(legacyVoiceCode));
    }

    [Fact]
    public void UnknownVoice_IsRejectedByCatalogAndResolver()
    {
        var options = new OpenAiSpeechOptions();

        Assert.False(OpenAiBuiltInVoiceCatalog.IsSupported("unknown-voice"));
        Assert.Throws<ArgumentException>(() => options.ResolveProviderVoice("unknown-voice"));
    }
}

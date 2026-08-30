using TOOL_SERVER.Generation;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Generation;

public sealed class KlingNativeAudioPromptComposerTests
{
    [Fact]
    public void Compose_OnCameraDialogue_PreservesExactLineAndAudioIntent()
    {
        var prompt = KlingNativeAudioPromptComposer.Compose(
            ["IDENTITY LOCK: Use the exact same approved character Minh."],
            "Minh looks into the camera in a bright classroom.",
            "blurry, subtitles",
            new KlingNativeSpeechPrompt(
                KlingSpeechModes.OnCameraDialogue,
                "Bạn bắt đầu từ một thói quen nhỏ hôm nay.",
                "vi-VN",
                "Minh",
                "warm, confident and friendly",
                "quiet classroom room tone",
                "soft synchronized hand movement sounds"),
            10,
            "16:9");

        Assert.Contains("says exactly once, without translating, paraphrasing or repeating", prompt);
        Assert.Contains("\"Bạn bắt đầu từ một thói quen nhỏ hôm nay.\"", prompt);
        Assert.Contains("Vietnamese (experimental and best effort)", prompt);
        Assert.Contains("Synchronize lip movements", prompt);
        Assert.Contains("Minh is the only speaker", prompt);
        Assert.Contains("quiet classroom room tone", prompt);
        Assert.Contains("NEGATIVE CONSTRAINTS", prompt);
        Assert.True(prompt.Length <= KlingNativeAudioPromptComposer.MaximumPromptLength);
    }

    [Fact]
    public void Compose_NativeVoiceOver_ProhibitsOnScreenDialogue()
    {
        var prompt = KlingNativeAudioPromptComposer.Compose(
            [],
            "A calm sunrise over a rice field.",
            null,
            new KlingNativeSpeechPrompt(
                KlingSpeechModes.NativeVoiceOver,
                "A new day begins with one small choice.",
                "en-US",
                null,
                null,
                null,
                null),
            10,
            "9:16");

        Assert.Contains("NATIVE VOICE-OVER", prompt);
        Assert.Contains("One off-screen narrator", prompt);
        Assert.Contains("No on-screen character speaks", prompt);
        Assert.Contains("\"A new day begins with one small choice.\"", prompt);
    }

    [Fact]
    public void Compose_RejectsSpeechThatExceedsDurationBudget()
    {
        var exception = Assert.Throws<KlingPromptValidationException>(() =>
            KlingNativeAudioPromptComposer.Compose(
                [],
                "A presenter speaks.",
                null,
                new KlingNativeSpeechPrompt(
                    KlingSpeechModes.OnCameraDialogue,
                    "one two three four five six seven eight nine",
                    "en-US",
                    "Presenter",
                    null,
                    null,
                    null),
                5,
                "16:9"));

        Assert.Equal("kling_spoken_text_too_long", exception.Code);
    }

    [Fact]
    public void Compose_TruncatesOnlyOptionalSectionsAndKeepsRequiredSpeechIntact()
    {
        const string spokenText = "Keep this exact short sentence.";
        var prompt = KlingNativeAudioPromptComposer.Compose(
            [],
            string.Join(' ', Enumerable.Repeat("cinematic-environment-detail", 400)),
            string.Join(' ', Enumerable.Repeat("negative-detail", 400)),
            new KlingNativeSpeechPrompt(
                KlingSpeechModes.NativeVoiceOver,
                spokenText,
                "en-US",
                null,
                null,
                null,
                null),
            10,
            "16:9");

        Assert.Contains($"\"{spokenText}\"", prompt);
        Assert.True(prompt.Length <= KlingNativeAudioPromptComposer.MaximumPromptLength);
        Assert.False(char.IsHighSurrogate(prompt[^1]));
    }

    [Fact]
    public void Compose_None_RequestsAmbienceWithoutInventingSpeech()
    {
        var prompt = KlingNativeAudioPromptComposer.Compose(
            [],
            "Close-up of rain on a window.",
            null,
            new KlingNativeSpeechPrompt(
                KlingSpeechModes.None,
                string.Empty,
                "vi-VN",
                null,
                null,
                "gentle rain",
                "soft droplets"),
            5,
            "1:1");

        Assert.Contains("No spoken dialogue and no narrator", prompt);
        Assert.Contains("gentle rain", prompt);
    }
}

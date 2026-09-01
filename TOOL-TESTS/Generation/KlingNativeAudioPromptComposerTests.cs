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

        Assert.Contains("start speaking within the first 0.5 seconds", prompt);
        Assert.Contains("say exactly once, finishing within the shot, without translating, paraphrasing or repeating", prompt);
        Assert.Contains("\"Bạn bắt đầu từ một thói quen nhỏ hôm nay.\"", prompt);
        Assert.Contains("Vietnamese (experimental and best effort)", prompt);
        Assert.Contains("synchronize lip movements", prompt);
        Assert.Contains("supplied first-frame image, labeled \"Minh\", is the only speaker", prompt);
        Assert.Contains("face, lips, mouth and jaw clearly visible", prompt);
        Assert.Contains("only smiles without speaking", prompt);
        Assert.Contains("quiet classroom room tone", prompt);
        Assert.Contains("NEGATIVE CONSTRAINTS", prompt);
        Assert.True(prompt.Length <= KlingNativeAudioPromptComposer.MaximumPromptLength);
        Assert.True(prompt.IndexOf("ON-CAMERA NATIVE SPEECH", StringComparison.Ordinal) <
                    prompt.IndexOf("IDENTITY LOCK", StringComparison.Ordinal));
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
    public void Compose_VietnameseLongFormTemplate_PreservesVietnameseSpeechAndWrappers()
    {
        var prompt = KlingNativeAudioPromptComposer.Compose(
            ["KHÓA NHẬN DIỆN NHÂN VẬT: dùng đúng nhân vật Maya đã duyệt."],
            "Maya nói trực tiếp với máy quay, khuôn mặt và miệng hiện rõ.",
            "không phụ đề, không logo, không watermark",
            new KlingNativeSpeechPrompt(
                KlingSpeechModes.OnCameraDialogue,
                "Hãy bắt đầu bằng một hành động nhỏ.",
                "vi-VN",
                "Maya",
                "ấm áp và rõ ràng",
                "âm nền căn phòng yên tĩnh",
                "tiếng cử động tay nhẹ"),
            5,
            "16:9",
            useVietnameseTemplate: true);

        Assert.Equal(
            KlingNativeAudioPromptComposer.VietnameseTemplateVersion,
            KlingNativeAudioPromptComposer.ResolveTemplateVersion(true));
        Assert.Contains("LỜI NHÂN VẬT TRỰC TIẾP", prompt, StringComparison.Ordinal);
        Assert.Contains("Ngôn ngữ: tiếng Việt", prompt, StringComparison.Ordinal);
        Assert.Contains("\"Hãy bắt đầu bằng một hành động nhỏ.\"", prompt, StringComparison.Ordinal);
        Assert.Contains("CẢNH, HÀNH ĐỘNG VÀ MÁY QUAY", prompt, StringComparison.Ordinal);
        Assert.Contains("RÀNG BUỘC LOẠI TRỪ", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("ON-CAMERA NATIVE SPEECH", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_AcceptsSpeechBeyondLegacyDurationWordBudget()
    {
        const string spokenText = "one two three four five six seven eight nine";
        var prompt = KlingNativeAudioPromptComposer.Compose(
            [],
            "A presenter speaks.",
            null,
            new KlingNativeSpeechPrompt(
                KlingSpeechModes.OnCameraDialogue,
                spokenText,
                "en-US",
                "Presenter",
                null,
                null,
                null),
            5,
            "16:9");

        Assert.Contains($"\"{spokenText}\"", prompt);
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
    public void Analyze_SeparatesRequiredContentFromAutoTruncatedOptionalPrompt()
    {
        var scenePrompt = string.Join(' ', Enumerable.Repeat("cinematic-environment-detail", 400));
        var speech = new KlingNativeSpeechPrompt(
            KlingSpeechModes.None,
            string.Empty,
            "en-US",
            null,
            null,
            null,
            null);

        var analysis = KlingNativeAudioPromptComposer.Analyze(
            [],
            scenePrompt,
            null,
            speech,
            10,
            "16:9");

        Assert.True(analysis.FitsRequiredContent);
        Assert.True(analysis.RequiredCharacters < 1000);
        Assert.True(analysis.FinalCharacters > 3000);
        Assert.Equal(KlingNativeAudioPromptComposer.MaximumPromptLength, analysis.MaximumCharacters);
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

    [Fact]
    public void Compose_SpeechRecoveryProfile_UsesCloseFramingAndMinimalAudio()
    {
        var prompt = KlingNativeAudioPromptComposer.Compose(
            ["IDENTITY LOCK: Use the approved presenter."],
            "The presenter speaks to camera.",
            null,
            new KlingNativeSpeechPrompt(
                KlingSpeechModes.OnCameraDialogue,
                "Start with one clear sentence.",
                "en-US",
                "Presenter",
                null,
                "busy city traffic",
                "loud footsteps"),
            5,
            "16:9",
            KlingNativeAudioPromptComposer.SpeechRecoveryProfile);

        Assert.Contains("SPEECH RECOVERY", prompt);
        Assert.Contains("medium close-up or medium shot", prompt);
        Assert.Contains("no silent intro", prompt);
        Assert.Contains("minimal natural room tone only", prompt);
        Assert.DoesNotContain("busy city traffic", prompt);
        Assert.DoesNotContain("loud footsteps", prompt);
    }
}

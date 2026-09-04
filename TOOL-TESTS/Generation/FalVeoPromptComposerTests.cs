using TOOL_SERVER.Generation;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Generation;

public sealed class FalVeoPromptComposerTests
{
    [Fact]
    public void Compose_PutsExactVietnameseSpeechBeforeVisualAndIdentityContext()
    {
        const string spokenText = "Xin chào, hôm nay chúng ta cùng bắt đầu.";
        var prompt = FalVeoPromptComposer.Compose(
            ["KHÓA NHẬN DIỆN: giữ nguyên nhân vật An."],
            "An đang trình bày trong trung cảnh, nhìn rõ khuôn mặt và miệng.",
            "không đổi trang phục",
            new KlingNativeSpeechPrompt(
                KlingSpeechModes.OnCameraDialogue,
                spokenText,
                "vi-VN",
                "An",
                "ấm áp",
                "âm phòng nhẹ",
                null),
            8,
            "16:9");

        Assert.Contains(spokenText, prompt, StringComparison.Ordinal);
        Assert.True(prompt.IndexOf("LỜI NHÂN VẬT TRỰC TIẾP", StringComparison.Ordinal) <
                    prompt.IndexOf("KHÓA NHẬN DIỆN", StringComparison.Ordinal));
        Assert.True(prompt.IndexOf("LỜI NHÂN VẬT TRỰC TIẾP", StringComparison.Ordinal) <
                    prompt.IndexOf("CẢNH, HÀNH ĐỘNG", StringComparison.Ordinal));
        Assert.Contains("không chỉ đứng yên hoặc mỉm cười im lặng", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_RecoveryProfileStrengthensVisibleSpeechWithoutChangingUtterance()
    {
        const string spokenText = "Đây là nguyên văn cần giữ.";
        var prompt = FalVeoPromptComposer.Compose(
            [],
            "Nhân vật nói trong trung cảnh.",
            null,
            new KlingNativeSpeechPrompt(
                KlingSpeechModes.OnCameraDialogue,
                spokenText,
                "vi-VN",
                "Mai",
                null,
                null,
                null),
            6,
            "9:16",
            FalVeoPolicy.SpeechRecoveryProfile);

        Assert.Contains(spokenText, prompt, StringComparison.Ordinal);
        Assert.Contains("PHỤC HỒI LỜI NÓI", prompt, StringComparison.Ordinal);
        Assert.Contains("giảm âm nền xuống tối thiểu", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(5, "16:9")]
    [InlineData(8, "1:1")]
    public void Compose_RejectsUnsupportedProviderVariant(int durationSeconds, string aspectRatio)
    {
        var exception = Assert.Throws<KlingPromptValidationException>(() =>
            FalVeoPromptComposer.Compose(
                [],
                "Cảnh hợp lệ.",
                null,
                new KlingNativeSpeechPrompt(KlingSpeechModes.None, string.Empty, "vi-VN", null, null, null, null),
                durationSeconds,
                aspectRatio));

        Assert.StartsWith("fal_", exception.Code, StringComparison.Ordinal);
    }
}

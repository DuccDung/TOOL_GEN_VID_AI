using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class SceneFirstFramePromptComposerTests
{
    [Fact]
    public void OnCameraPrompt_KeepsOneIdentityAndSafeCompositionRules()
    {
        var prompt = SceneFirstFramePromptComposer.Compose(new SceneFirstFramePromptInput(
            "16:9",
            "Minh đứng trong căn bếp và nhìn vào máy quay.",
            "medium close-up",
            "ánh sáng cửa sổ dịu",
            "chuẩn bị nâng chiếc cốc",
            "bình tĩnh",
            new SceneFirstFrameCharacterPrompt(
                "Minh",
                "người dẫn chuyện",
                "khuôn mặt trái xoan, tóc đen ngắn",
                "{\"age\":30}",
                "{\"clothing\":\"áo sơ mi xanh\"}",
                "[\"không đổi khuôn mặt\"]"),
            [new SceneFirstFrameAssetPrompt("Background", "Bếp nhà Minh", "tủ gỗ sáng và cửa sổ bên trái") ],
            "Minh giới thiệu công thức.",
            "Chúng ta bắt đầu nhé."));

        Assert.Contains("đúng một nhân vật", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Minh", prompt, StringComparison.Ordinal);
        Assert.Contains("vùng bố cục an toàn", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("môi và hàm", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("subtitle", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bếp nhà Minh", prompt, StringComparison.Ordinal);
        Assert.Contains("Minh giới thiệu công thức", prompt, StringComparison.Ordinal);
        Assert.Contains("Chúng ta bắt đầu", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BrollPrompt_DoesNotInventCharacter()
    {
        var prompt = SceneFirstFramePromptComposer.Compose(new SceneFirstFramePromptInput(
            "9:16",
            "Cận cảnh chiếc cốc trên bàn gỗ.",
            null,
            null,
            null,
            null,
            null,
            []));

        Assert.Contains("B-roll", prompt, StringComparison.Ordinal);
        Assert.Contains("Không thêm người", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nguồn nhận diện duy nhất", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LongPrompt_PreservesMandatorySafetySuffixWithoutSplittingUnicode()
    {
        var prompt = SceneFirstFramePromptComposer.Compose(new SceneFirstFramePromptInput(
            "16:9",
            string.Concat(Enumerable.Repeat("khung cảnh 🎬 ", 1000)),
            null,
            null,
            null,
            null,
            null,
            []));

        Assert.True(prompt.Length <= 8_000);
        Assert.False(char.IsHighSurrogate(prompt[^1]));
        Assert.Contains("This is B-roll", prompt, StringComparison.Ordinal);
        Assert.EndsWith("collages.", prompt, StringComparison.Ordinal);
    }
}

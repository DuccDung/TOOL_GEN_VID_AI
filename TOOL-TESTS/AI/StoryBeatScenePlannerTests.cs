using TOOL_LOCAL.AI.Contracts;
using TOOL_LOCAL.AI.ScenePlanning;
using TOOL_LOCAL.Providers;

namespace TOOL_TESTS.AI;

public sealed class StoryBeatScenePlannerTests
{
    private static readonly VideoProviderCapabilities Capabilities =
        new([5, 8, 10], 1920, 1080, true, true, false, false, true);

    [Fact]
    public void Plan_PreservesStoryBeatOrderAndTargetDuration()
    {
        var script = new ScriptContract(
            "Thiếu ngủ",
            "Cơ thể đang cảnh báo bạn.",
            [
                Beat("HOOK", "Mở đầu", "Bạn có biết cơ thể đang cảnh báo điều này mỗi đêm?", "Cận cảnh nhân vật thức giấc", "worried"),
                Beat("PROBLEM", "Nêu vấn đề", "Dấu hiệu đầu tiên là bạn luôn cảm thấy mệt dù đã ngủ đủ giờ.", "Nhân vật ngồi mệt mỏi", "tired"),
                Beat("SOLUTION", "Giải pháp", "Hãy duy trì giờ ngủ ổn định và giảm ánh sáng xanh trước khi ngủ.", "Nhân vật tắt điện thoại", "hopeful"),
                Beat("CTA", "Kêu gọi", "Hãy lưu video để kiểm tra lại tối nay.", "Nhân vật nhìn vào camera", "encouraging")
            ],
            "Lưu video",
            30);

        var result = new StoryBeatScenePlanner().Plan(script, 30, Capabilities);
        var scenes = result.Scenes.ToArray();

        Assert.Equal(30m, result.TotalContentDurationSeconds);
        Assert.Equal(30m, scenes.Sum(x => x.ContentDurationSeconds));
        Assert.Equal("HOOK", scenes[0].BeatType);
        Assert.Equal("CTA", scenes[^1].BeatType);
        Assert.All(scenes, scene => Assert.Contains(scene.GenerationDurationSeconds, Capabilities.SupportedDurationsSeconds));
        Assert.All(scenes, scene => Assert.True(scene.ContentDurationSeconds <= 10));
    }

    [Fact]
    public void Plan_SplitsLongBeatWithoutLosingSceneLinks()
    {
        var longNarration = string.Join(' ', Enumerable.Repeat("nội dung quan trọng", 80));
        var script = new ScriptContract(
            "Long",
            "Hook",
            [Beat("INFORMATION", "Giải thích", longNarration, "Chuỗi hành động liên tục", "focused", 5)],
            "CTA",
            30);

        var result = new StoryBeatScenePlanner().Plan(script, 30, Capabilities);
        var scenes = result.Scenes.ToArray();

        Assert.True(scenes.Length >= 3);
        for (var index = 1; index < scenes.Length; index++)
        {
            Assert.Equal(scenes[index - 1].SceneKey, scenes[index].PreviousSceneKey);
            Assert.Equal(scenes[index].SceneKey, scenes[index - 1].NextSceneKey);
        }

        var report = new ContinuityValidator().Validate(result);
        Assert.True(report.Approved);
        Assert.Equal(100m, report.Score);
    }

    private static StoryBeatContract Beat(
        string type,
        string purpose,
        string narration,
        string visual,
        string emotion,
        int complexity = 1) =>
        new(type, purpose, narration, null, visual, emotion, complexity);
}

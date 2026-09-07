using TOOL_SERVER.Generation;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Generation;

public sealed class ContentSpeechPacingValidatorTests
{
    [Fact]
    public void FindPlanViolations_ReportsSceneAndSafeTimingMetrics()
    {
        var plan = CreatePlan("Hít sâu.", KlingSpeechModes.NativeVoiceOver);

        var violation = Assert.Single(ContentSpeechPacingValidator.FindPlanViolations(plan, 1m));

        Assert.Equal("scenes[0].spoken_text", violation.Field);
        Assert.Equal(ContentPlanViolationReasons.SpeechTooShort, violation.Reason);
        Assert.NotNull(violation.EstimatedDurationSeconds);
        Assert.Equal(6.8m, violation.TargetMinimumSeconds);
        Assert.Equal(7.6m, violation.TargetMaximumSeconds);
    }

    [Fact]
    public void FindPlanViolations_IgnoresSilentSceneAndAcceptsSafeNarration()
    {
        var silent = CreatePlan(string.Empty, KlingSpeechModes.None);
        var suitable = CreatePlan(
            "Đầu tiên, đứng thẳng, hít sâu và nâng hai tay lên cao để đánh thức toàn thân thật nhẹ nhàng.",
            KlingSpeechModes.NativeVoiceOver);

        Assert.Empty(ContentSpeechPacingValidator.FindPlanViolations(silent, 1m));
        Assert.Empty(ContentSpeechPacingValidator.FindPlanViolations(suitable, 1m));
    }

    private static GeneratedContentPlan CreatePlan(string narration, string speechMode) =>
        new(
            "Bài tập buổi sáng",
            "Bắt đầu ngày mới",
            "Hướng dẫn thực tế",
            "Người trưởng thành",
            "Hãy tập ngay",
            narration,
            "Ánh sáng tự nhiên",
            "Không chữ trên màn hình",
            [],
            [
                new GeneratedContentScene(
                    1,
                    "Hướng dẫn động tác đầu tiên",
                    narration,
                    "Phòng khách sáng với thảm tập màu xanh.",
                    8,
                    [],
                    speechMode,
                    null,
                    "Chậm rãi và rõ ràng",
                    "Âm thanh phòng yên tĩnh",
                    "Không có",
                    ["living-room"])
            ],
            [
                new GeneratedProjectAsset(
                    "living-room",
                    "Background",
                    "Phòng khách",
                    "Phòng khách sáng với thảm tập xanh.",
                    [1])
            ]);
}

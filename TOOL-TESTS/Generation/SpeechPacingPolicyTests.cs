using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Generation;

public sealed class SpeechPacingPolicyTests
{
    [Fact]
    public void CreateGuidance_ForEightSecondSceneTargetsMostOfTheScene()
    {
        var guidance = SpeechPacingPolicy.CreateGuidance(8m);

        Assert.Equal(6.8m, guidance.TargetMinimumSeconds);
        Assert.Equal(7.6m, guidance.TargetMaximumSeconds);
        Assert.Equal(19, guidance.SuggestedMinimumSpeechUnits);
        Assert.Equal(22, guidance.SuggestedMaximumSpeechUnits);
    }

    [Fact]
    public void CountSpeechUnits_IsDeterministicForVietnameseNumbersAcronymsAndWhitespace()
    {
        var units = SpeechPacingPolicy.CountSpeechUnits("AI giúp bạn tập 75\tgiây mỗi sáng.");

        Assert.Equal(10, units);
    }

    [Fact]
    public void Assess_DistinguishesTooShortTargetAndTooLongNarration()
    {
        var tooShort = SpeechPacingPolicy.Assess("Hít sâu.", 8m);
        var onTarget = SpeechPacingPolicy.Assess(
            "Đầu tiên, đứng thẳng, hít sâu và nâng hai tay lên cao để đánh thức toàn thân thật nhẹ nhàng.",
            8m);
        var tooLong = SpeechPacingPolicy.Assess(
            "Đầu tiên hãy đứng thẳng, hít thật sâu rồi từ từ nâng hai tay lên cao, kéo giãn toàn bộ cơ thể, giữ vai thả lỏng, tiếp tục hít vào chậm rãi và cảm nhận nguồn năng lượng mới đang lan tỏa khắp người.",
            8m);

        Assert.Equal(SpeechPacingStatuses.TooShort, tooShort.Status);
        Assert.False(tooShort.PassesGenerationGuard);
        Assert.Equal(SpeechPacingStatuses.OnTarget, onTarget.Status);
        Assert.True(onTarget.PassesGenerationGuard);
        Assert.Equal(SpeechPacingStatuses.TooLong, tooLong.Status);
        Assert.False(tooLong.PassesGenerationGuard);
    }

    [Fact]
    public void Assess_UsesConfiguredSpeakingRate()
    {
        const string narration = "Đứng thẳng, hít sâu rồi nâng hai tay lên cao chậm rãi.";

        var normal = SpeechPacingPolicy.Assess(narration, 5m, 1m);
        var faster = SpeechPacingPolicy.Assess(narration, 5m, 1.25m);

        Assert.True(faster.EstimatedDurationSeconds < normal.EstimatedDurationSeconds);
    }
}

using TOOL_SHARED.Contracts.Generation;

namespace TOOL_SERVER.Generation;

internal static class ContentSpeechPacingValidator
{
    public static IReadOnlyList<ContentLanguageViolation> FindPlanViolations(
        GeneratedContentPlan plan,
        decimal speakingRate)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var violations = new List<ContentLanguageViolation>();
        for (var index = 0; index < plan.Scenes.Count; index++)
        {
            var scene = plan.Scenes[index];
            if (scene.DurationSeconds <= 0 ||
                string.Equals(scene.SpeechMode, KlingSpeechModes.None, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(scene.Narration))
            {
                continue;
            }

            var assessment = SpeechPacingPolicy.Assess(
                scene.Narration,
                scene.DurationSeconds,
                speakingRate);
            var reason = assessment.Status switch
            {
                SpeechPacingStatuses.TooShort => ContentPlanViolationReasons.SpeechTooShort,
                SpeechPacingStatuses.TooLong => ContentPlanViolationReasons.SpeechTooLong,
                _ => null
            };
            if (reason is null)
            {
                continue;
            }

            violations.Add(new ContentLanguageViolation(
                $"scenes[{index}].spoken_text",
                reason,
                assessment.EstimatedDurationSeconds,
                assessment.TargetMinimumSeconds,
                assessment.TargetMaximumSeconds));
        }

        return violations;
    }
}

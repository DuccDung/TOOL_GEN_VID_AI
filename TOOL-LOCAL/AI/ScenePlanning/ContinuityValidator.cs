using TOOL_LOCAL.AI.Contracts;

namespace TOOL_LOCAL.AI.ScenePlanning;

public sealed class ContinuityValidator
{
    public QualityReportContract Validate(ScenePlanContract plan)
    {
        var issues = new List<QualityIssueContract>();
        var scenes = plan.Scenes.OrderBy(x => x.SequenceNumber).ToArray();

        for (var index = 0; index < scenes.Length; index++)
        {
            var scene = scenes[index];
            if (scene.TimeEndSeconds <= scene.TimeStartSeconds)
            {
                issues.Add(Error("SCENE_DURATION_INVALID", scene.SceneKey, "Scene có thời lượng không hợp lệ."));
            }

            if (index == 0)
            {
                continue;
            }

            var previous = scenes[index - 1];
            Compare(previous, scene, "Clothing", previous.EndState.Clothing, scene.StartState.Clothing, issues);
            Compare(previous, scene, "Location", previous.EndState.Location, scene.StartState.Location, issues);
            Compare(previous, scene, "TimeOfDay", previous.EndState.TimeOfDay, scene.StartState.TimeOfDay, issues);
            Compare(previous, scene, "Lighting", previous.EndState.Lighting, scene.StartState.Lighting, issues);

            if (!previous.EndState.HeldProps.Order().SequenceEqual(scene.StartState.HeldProps.Order(), StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(Error(
                    "PROP_CONTINUITY_MISMATCH",
                    scene.SceneKey,
                    $"Đạo cụ đầu {scene.SceneKey} không khớp trạng thái cuối {previous.SceneKey}."));
            }

            if (scene.PreviousSceneKey != previous.SceneKey || previous.NextSceneKey != scene.SceneKey)
            {
                issues.Add(Error("SCENE_LINK_MISMATCH", scene.SceneKey, "Liên kết previous/next scene không nhất quán."));
            }
        }

        var plannedDuration = scenes.Sum(x => x.ContentDurationSeconds);
        if (Math.Abs(plannedDuration - plan.TotalContentDurationSeconds) > 0.2m)
        {
            issues.Add(Error("TOTAL_DURATION_MISMATCH", "scene_plan", "Tổng thời lượng scene không khớp target."));
        }

        var score = Math.Max(0, 100 - issues.Count * 15);
        return new QualityReportContract(score, score >= 70 && issues.All(x => x.Severity != "Error"), issues);
    }

    private static void Compare(
        PlannedSceneContract previous,
        PlannedSceneContract current,
        string field,
        string previousValue,
        string currentValue,
        ICollection<QualityIssueContract> issues)
    {
        if (!string.Equals(previousValue, currentValue, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Error(
                $"{field.ToUpperInvariant()}_CONTINUITY_MISMATCH",
                current.SceneKey,
                $"{field} đầu {current.SceneKey} không khớp trạng thái cuối {previous.SceneKey}."));
        }
    }

    private static QualityIssueContract Error(string code, string target, string message) =>
        new(code, "Error", message, $"Đồng bộ lại continuity state tại {target}.");
}

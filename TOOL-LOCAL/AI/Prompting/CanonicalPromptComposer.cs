using TOOL_LOCAL.AI.Contracts;

namespace TOOL_LOCAL.AI.Prompting;

public sealed class CanonicalPromptComposer
{
    private static readonly string[] BaselineNegativeTerms =
    [
        "different face",
        "changing identity",
        "different hairstyle",
        "different clothing",
        "duplicate person",
        "deformed hands",
        "extra fingers",
        "warped body",
        "distorted face",
        "unnatural movement",
        "flickering",
        "low quality",
        "blurry"
    ];

    public CanonicalVideoPromptContract Compose(
        CharacterProfileContract character,
        StyleProfileContract style,
        PlannedSceneContract scene,
        PlannedSceneContract? previousScene,
        PlannedSceneContract? nextScene,
        IReadOnlyCollection<string> characterReferencePaths,
        int outputWidth,
        int outputHeight,
        decimal framesPerSecond)
    {
        var identity = string.Join(", ", new[]
        {
            character.Name,
            character.Gender,
            character.Age is null ? null : $"{character.Age}-year-old",
            character.Face,
            character.Hair,
            character.Eyes,
            character.Skin,
            character.Body,
            character.Clothing,
            character.Accessories,
            character.VisualIdentity
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var previousContext = previousScene is null
            ? "This is the opening scene."
            : $"Continue exactly from {previousScene.SceneKey}: {Describe(previousScene.EndState)}.";
        var nextContext = nextScene is null
            ? "End with a stable final composition."
            : $"Finish ready for {nextScene.SceneKey}: {Describe(nextScene.StartState)}.";

        var positive = string.Join(" ",
            $"Feature the exact same character identity: {identity}.",
            $"Immutable identity traits: {string.Join(", ", character.ImmutableTraits)}.",
            $"Scene purpose: {scene.StoryPurpose}.",
            $"Visual action: {scene.VisualDescription}.",
            $"Camera: {scene.Camera}; motion: {scene.Motion}.",
            $"Emotion: {scene.Emotion}.",
            $"Environment: {scene.StartState.Location}; time: {scene.StartState.TimeOfDay}.",
            $"Style: {style.VisualStyle}; color: {style.ColorStyle}; camera language: {style.CameraStyle}; lighting: {style.LightingStyle}; quality: {style.RenderQuality}.",
            previousContext,
            nextContext,
            "Preserve face geometry, hairstyle, clothing, body proportions, props, environment and lighting across the entire clip.");

        var negative = string.Join(", ", BaselineNegativeTerms
            .Concat(style.GlobalNegativeTerms)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        var continuity = $"START {Describe(scene.StartState)}. END {Describe(scene.EndState)}.";

        return new CanonicalVideoPromptContract(
            positive,
            negative,
            continuity,
            characterReferencePaths,
            scene.GenerationDurationSeconds,
            outputWidth,
            outputHeight,
            framesPerSecond);
    }

    private static string Describe(ContinuityStateContract state) =>
        $"pose={state.CharacterPose}, position={state.CharacterPosition}, look={state.LookDirection}, " +
        $"clothing={state.Clothing}, props=[{string.Join(", ", state.HeldProps)}], location={state.Location}, " +
        $"time={state.TimeOfDay}, lighting={state.Lighting}, emotion={state.Emotion}";
}

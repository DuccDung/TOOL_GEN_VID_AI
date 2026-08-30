using TOOL_LOCAL.AI.Contracts;
using TOOL_LOCAL.AI.Prompting;

namespace TOOL_TESTS.AI;

public sealed class PromptComposerTests
{
    [Fact]
    public void Compose_InjectsCharacterStyleReferencesAndIdentityNegatives()
    {
        var state = new ContinuityStateContract(
            "standing",
            "center",
            "window",
            "white hoodie",
            ["phone"],
            "modern apartment",
            "night",
            "warm practical lighting",
            "worried");
        var scene = new PlannedSceneContract(
            1, "scene_001", "HOOK", "Hook", 0, 5, 5, 5,
            "Narration", null, "Anna turns toward the rainy window", "close-up", "slow push-in", "worried",
            "cut from black", state, state, null, null);
        var character = new CharacterProfileContract(
            "anna", "Anna", "main", "female", 25, "oval face", "long black hair", "brown eyes",
            "warm skin", "slim", "white hoodie", "silver necklace", "thoughtful", "identity-anchor-anna",
            ["oval face", "long black hair", "white hoodie"]);
        var style = new StyleProfileContract(
            "cinematic realistic", "warm high contrast", "35mm shallow depth of field", "dramatic practical light",
            "modern urban", "high detail", ["text", "watermark"]);

        var prompt = new CanonicalPromptComposer().Compose(
            character, style, scene, null, null, ["characters/anna-reference.png"], 1080, 1920, 30);

        Assert.Contains("identity-anchor-anna", prompt.PositivePrompt);
        Assert.Contains("white hoodie", prompt.PositivePrompt);
        Assert.Contains("changing identity", prompt.NegativePrompt);
        Assert.Contains("watermark", prompt.NegativePrompt);
        Assert.Single(prompt.ReferenceImagePaths);
        Assert.Equal(5, prompt.DurationSeconds);
    }
}

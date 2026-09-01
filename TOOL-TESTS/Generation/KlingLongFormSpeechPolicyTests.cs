using TOOL_SERVER.Generation;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Generation;

public sealed class KlingLongFormSpeechPolicyTests
{
    [Fact]
    public void Validate_RejectsVoiceOverWithOnScreenCharacter()
    {
        var exception = Assert.Throws<KlingPromptValidationException>(() =>
            KlingLongFormSpeechPolicy.Validate(
                new KlingNativeSpeechPrompt(
                    KlingSpeechModes.NativeVoiceOver,
                    "Start with one small action.",
                    "en-US",
                    null,
                    null,
                    null,
                    null),
                KlingSpeechModes.NativeVoiceOver,
                1,
                "A presenter stands in a bright room."));

        Assert.Equal("kling_voice_over_character_not_allowed", exception.Code);
    }

    [Theory]
    [InlineData("The presenter remains silent and smiles at the camera.")]
    [InlineData("The presenter keeps her mouth closed.")]
    [InlineData("An off-screen narrator speaks while the presenter listens.")]
    public void Validate_RejectsVisibleActionThatConflictsWithOnCameraSpeech(string visualPrompt)
    {
        var exception = Assert.Throws<KlingPromptValidationException>(() =>
            KlingLongFormSpeechPolicy.Validate(
                new KlingNativeSpeechPrompt(
                    KlingSpeechModes.OnCameraDialogue,
                    "Start with one small action.",
                    "en-US",
                    "Presenter",
                    null,
                    null,
                    null),
                KlingSpeechModes.OnCameraDialogue,
                1,
                visualPrompt));

        Assert.Equal("kling_on_camera_action_invalid", exception.Code);
    }

    [Fact]
    public void VisibleSpeakingPerformance_RequiresSpeechActionAndVisibleFaceOrMouth()
    {
        Assert.True(KlingLongFormSpeechPolicy.HasVisibleSpeakingPerformance(
            "The presenter speaks to camera in a medium close-up with the mouth clearly visible."));
        Assert.False(KlingLongFormSpeechPolicy.HasVisibleSpeakingPerformance(
            "The presenter stands and smiles at the camera."));
        Assert.False(KlingLongFormSpeechPolicy.HasOnCameraConflict(
            "The presenter speaks clearly with no off-screen narrator speaking."));
    }
}

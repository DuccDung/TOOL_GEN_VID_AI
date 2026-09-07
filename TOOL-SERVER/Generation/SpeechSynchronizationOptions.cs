namespace TOOL_SERVER.Generation;

internal sealed class SpeechSynchronizationOptions
{
    public const string SectionName = "Generation:SpeechSynchronization";

    public bool CanonicalVoiceEnabled { get; init; }

    public bool SpeechVerificationEnabled { get; init; }

    public void Validate()
    {
        // Canonical Voice only requires TTS plus local technical/audio review.
        // Speech verification remains an independent option for provider-native audio.
    }
}

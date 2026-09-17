using TOOL_SERVER.Generation;

namespace TOOL_SERVER_TESTS;

public sealed class TtsFoundationServerTests
{
    [Fact]
    public void CanonicalVoiceConfiguration_DoesNotRequireSpeechVerification()
    {
        var options = new SpeechSynchronizationOptions
        {
            CanonicalVoiceEnabled = true,
            SpeechVerificationEnabled = false
        };

        options.Validate();
    }
}

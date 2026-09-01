using TOOL_SHARED.Contracts.Generation;

namespace TOOL_SERVER.Generation;

internal static class SeedanceNativeAudioPromptComposer
{
    public const string TemplateVersion = "seedance-native-audio-v2-project-assets";

    public static string Compose(
        IReadOnlyCollection<string> identityParts,
        string scenePrompt,
        string? negativePrompt,
        KlingNativeSpeechPrompt speech,
        int durationSeconds,
        string aspectRatio)
    {
        var sections = new List<string>
        {
            $"Create one continuous {durationSeconds}-second cinematic shot in {aspectRatio}.",
            "Generate synchronized native audio together with the video. Do not add subtitles, captions, logos or watermarks."
        };
        sections.AddRange(identityParts.Where(x => !string.IsNullOrWhiteSpace(x)));
        sections.Add($"VISUAL ACTION: {scenePrompt.Trim()}");
        if (!string.IsNullOrWhiteSpace(negativePrompt))
        {
            sections.Add($"AVOID: {negativePrompt.Trim()}");
        }
        if (speech.Mode == KlingSpeechModes.OnCameraDialogue)
        {
            sections.Add(
                $"ON-CAMERA DIALOGUE ({speech.LanguageCode}): " +
                $"{speech.SpeakerName ?? "The approved character"} says exactly: “{speech.SpokenText}”. " +
                "Keep natural lip synchronization and do not translate, paraphrase or add words.");
        }
        else if (speech.Mode == KlingSpeechModes.NativeVoiceOver)
        {
            sections.Add(
                $"NATIVE VOICE-OVER ({speech.LanguageCode}): Speak exactly: “{speech.SpokenText}”. " +
                "Do not show an extra speaker and do not translate, paraphrase or add words.");
        }
        if (!string.IsNullOrWhiteSpace(speech.VoiceStyle))
        {
            sections.Add($"VOICE STYLE: {speech.VoiceStyle.Trim()}.");
        }
        if (!string.IsNullOrWhiteSpace(speech.AmbientAudio))
        {
            sections.Add($"AMBIENCE: {speech.AmbientAudio.Trim()}.");
        }
        if (!string.IsNullOrWhiteSpace(speech.SoundEffects))
        {
            sections.Add($"SOUND EFFECTS: {speech.SoundEffects.Trim()}.");
        }
        return string.Join("\n", sections);
    }
}

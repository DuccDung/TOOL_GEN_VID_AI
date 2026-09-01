using TOOL_SHARED.Contracts.Generation;

namespace TOOL_LOCAL.Projects;

internal static class KlingLongFormSpeechIntentValidator
{
    public static bool Applies(string? providerCode, string? structureType) =>
        KlingLongFormVietnameseValidator.RequiresVietnamese(providerCode, structureType);

    public static string? FindViolation(
        string speechMode,
        string? spokenText,
        string? speakerCharacterKey,
        int characterCount)
    {
        var hasSpeech = !string.IsNullOrWhiteSpace(spokenText);
        var hasSpeaker = !string.IsNullOrWhiteSpace(speakerCharacterKey);
        return speechMode switch
        {
            KlingSpeechModes.None when hasSpeech || hasSpeaker =>
                "Cảnh không lời không được chứa nội dung hoặc người nói.",
            KlingSpeechModes.None => null,
            KlingSpeechModes.OnCameraDialogue when !hasSpeech || !hasSpeaker || characterCount != 1 =>
                "Cảnh nhân vật nói trực tiếp phải có lời, đúng một nhân vật và đúng người nói.",
            KlingSpeechModes.OnCameraDialogue => null,
            KlingSpeechModes.NativeVoiceOver when !hasSpeech || hasSpeaker =>
                "Cảnh lời dẫn ngoài khung hình phải có lời và không được gắn người nói trực tiếp.",
            KlingSpeechModes.NativeVoiceOver when characterCount != 0 =>
                "Lời dẫn ngoài khung hình chỉ dùng cho cảnh B-roll không gắn nhân vật.",
            KlingSpeechModes.NativeVoiceOver => null,
            _ => "Kiểu lời Native Audio không hợp lệ."
        };
    }
}

using System.Text.RegularExpressions;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_SERVER.Generation;

internal static partial class KlingLongFormSpeechPolicy
{
    public const string PolicyVersion = "kling-long-form-speech-v1";

    public static bool Applies(string? providerCode, string? structureType) =>
        string.Equals(providerCode, ProviderCodes.Kling, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(structureType, GenerationWorkflowTypes.OpenAiStructuredPlan, StringComparison.Ordinal);

    public static void Validate(
        KlingNativeSpeechPrompt speech,
        string? declaredMode,
        int characterCount,
        string scenePrompt)
    {
        if (!string.Equals(declaredMode, speech.Mode, StringComparison.Ordinal))
        {
            throw Invalid(
                "kling_speech_intent_invalid",
                "Kiểu lời đã lưu không khớp nội dung thoại của cảnh. Hãy sửa hoặc sinh lại cảnh trước khi tạo video.");
        }

        switch (speech.Mode)
        {
            case KlingSpeechModes.OnCameraDialogue when characterCount != 1 || string.IsNullOrWhiteSpace(speech.SpeakerName):
                throw Invalid(
                    "kling_on_camera_speaker_required",
                    "Cảnh nhân vật nói trực tiếp phải có đúng một nhân vật đã khóa và một ảnh tham chiếu chính.");
            case KlingSpeechModes.NativeVoiceOver when characterCount != 0:
                throw Invalid(
                    "kling_voice_over_character_not_allowed",
                    "Lời dẫn ngoài khung hình chỉ dùng cho cảnh B-roll không gắn nhân vật. Hãy chuyển sang nhân vật nói trực tiếp hoặc bỏ nhân vật khỏi cảnh.");
            case KlingSpeechModes.OnCameraDialogue when HasOnCameraConflict(scenePrompt):
                throw Invalid(
                    "kling_on_camera_action_invalid",
                    "Prompt hình ảnh đang yêu cầu nhân vật im lặng, khép miệng hoặc dùng lời dẫn ngoài khung hình. Hãy sửa prompt cảnh trước khi tạo video.");
        }
    }

    public static bool HasVisibleSpeakingPerformance(string? visualPrompt) =>
        !string.IsNullOrWhiteSpace(visualPrompt) &&
        VisibleSpeakingActionRegex().IsMatch(visualPrompt) &&
        VisibleFaceOrMouthRegex().IsMatch(visualPrompt) &&
        !HasOnCameraConflict(visualPrompt);

    public static bool HasOnCameraConflict(string? visualPrompt) =>
        !string.IsNullOrWhiteSpace(visualPrompt) && OnCameraConflictRegex().IsMatch(visualPrompt);

    [GeneratedRegex(
        @"\b(?:speaks?|speaking|talks?|talking|addresses?|addressing|presents?|presenting|delivers?|delivering|explains?|explaining|says?|saying|nói|đang nói|trình bày|đang trình bày|giải thích|đang giải thích|phát biểu|đang phát biểu|trò chuyện)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VisibleSpeakingActionRegex();

    [GeneratedRegex(
        @"\b(?:face|facial|mouth|lips?|jaw|close[- ]?up|medium shot|medium close[- ]?up|khuôn mặt|gương mặt|miệng|môi|hàm|cận cảnh|trung cảnh|trung cận)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VisibleFaceOrMouthRegex();

    [GeneratedRegex(
        @"\b(?:does not speak|doesn't speak|remains silent|stays silent|is silent|mouth remains closed|keeps (?:his|her|their|the) mouth closed|listens? without speaking|listening without speaking|silent smile|smiles? silently|(?<!no )(?<!no an )(?<!without )(?<!without an )(?:(?:an?) )?off[- ]screen narrator (?:speaks?|says?|narrates?)|(?<!no )(?<!without )voice[- ]?over (?:narration|narrator|speaks?|says?|plays?)|không nói|không phát biểu|vẫn im lặng|giữ im lặng|khép miệng|ngậm miệng|chỉ mỉm cười|cười im lặng|nghe mà không nói|lời dẫn ngoài khung hình|người dẫn chuyện ngoài khung hình|giọng thuyết minh ngoài khung hình)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OnCameraConflictRegex();

    private static KlingPromptValidationException Invalid(string code, string message) => new(code, message);
}

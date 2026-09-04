using System.Text.Encodings.Web;
using System.Text.Json;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_SERVER.Generation;

internal static class FalVeoPromptComposer
{
    public const int MaximumPromptCharacters = 12_000;

    private static readonly JsonSerializerOptions TextJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Compose(
        IReadOnlyCollection<string> identityParts,
        string scenePrompt,
        string? negativePrompt,
        KlingNativeSpeechPrompt speech,
        int durationSeconds,
        string aspectRatio,
        string? recoveryProfile = null)
    {
        if (durationSeconds is not (4 or 6 or 8))
        {
            throw Invalid("fal_duration_invalid", "Veo chỉ hỗ trợ clip dài đúng 4, 6 hoặc 8 giây trong workflow này.");
        }
        if (aspectRatio is not ("16:9" or "9:16"))
        {
            throw Invalid("fal_aspect_ratio_invalid", "Veo chỉ hỗ trợ tỷ lệ 16:9 hoặc 9:16 trong workflow này.");
        }
        if (string.IsNullOrWhiteSpace(scenePrompt))
        {
            throw Invalid("scene_prompt_not_ready", "Cảnh chưa có prompt hình ảnh hợp lệ.");
        }
        if (recoveryProfile is not null &&
            (recoveryProfile != FalVeoPolicy.SpeechRecoveryProfile || speech.Mode != KlingSpeechModes.OnCameraDialogue))
        {
            throw Invalid("fal_speech_recovery_profile_invalid", "Profile phục hồi lời nói Veo không hợp lệ cho cảnh hiện tại.");
        }

        var sections = new List<string>
        {
            $"Tạo một cảnh quay điện ảnh liên tục duy nhất dài {durationSeconds} giây theo tỷ lệ {aspectRatio}, có video và âm thanh native đồng bộ. Không tạo phụ đề, chữ phủ, logo hoặc watermark.",
            ComposeSpeech(speech, recoveryProfile)
        };
        sections.AddRange(identityParts
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()));
        sections.Add($"CẢNH, HÀNH ĐỘNG VÀ MÁY QUAY: {scenePrompt.Trim()}");
        sections.Add(
            "RÀNG BUỘC LOẠI TRỪ BẮT BUỘC: không sai người nói; không nhiều người cùng nói; không miệng khép khi có lời thoại; " +
            "không chỉ đứng yên hoặc mỉm cười im lặng; không lời dẫn ngoài khung hình trong cảnh nhân vật nói trực tiếp; không phụ đề hay chữ trên màn hình.");
        if (!string.IsNullOrWhiteSpace(negativePrompt))
        {
            sections.Add($"RÀNG BUỘC LOẠI TRỪ CỦA DỰ ÁN: {negativePrompt.Trim()}");
        }

        var result = string.Join("\n", sections);
        if (result.Length > MaximumPromptCharacters)
        {
            throw Invalid(
                "fal_prompt_too_long",
                "Lời thoại, nhân vật và tài sản đã khóa làm prompt Veo vượt giới hạn an toàn; hệ thống không tự cắt nội dung đã duyệt.");
        }
        return result;
    }

    private static string ComposeSpeech(KlingNativeSpeechPrompt speech, string? recoveryProfile)
    {
        var spokenText = speech.SpokenText?.Trim() ?? string.Empty;
        var language = string.IsNullOrWhiteSpace(speech.LanguageCode)
            ? FalVeoPolicy.VietnameseLanguageCode
            : speech.LanguageCode.Trim();
        var voiceStyle = string.IsNullOrWhiteSpace(speech.VoiceStyle)
            ? "tự nhiên, rõ ràng, ấm áp, có nhịp thở và khoảng nghỉ phù hợp"
            : speech.VoiceStyle.Trim();
        var ambience = string.IsNullOrWhiteSpace(speech.AmbientAudio)
            ? "âm nền tự nhiên nhẹ, không lấn át lời nói"
            : speech.AmbientAudio.Trim();
        var effects = string.IsNullOrWhiteSpace(speech.SoundEffects)
            ? "chỉ dùng hiệu ứng hành động nhẹ và đồng bộ khi cần"
            : speech.SoundEffects.Trim();

        if (speech.Mode == KlingSpeechModes.None)
        {
            if (spokenText.Length > 0 || !string.IsNullOrWhiteSpace(speech.SpeakerName))
            {
                throw Invalid("fal_speech_mode_invalid", "Cảnh không lời không được chứa người nói hoặc nội dung lời nói.");
            }
            return $"ÂM THANH NATIVE: Không có lời thoại và không có người dẫn chuyện. Tạo {ambience}; {effects}.";
        }

        var exactText = JsonSerializer.Serialize(spokenText, TextJsonOptions);
        if (spokenText.Length == 0)
        {
            throw Invalid("fal_speech_mode_invalid", "Cảnh có lời phải chứa nguyên văn nội dung cần nói.");
        }
        if (speech.Mode == KlingSpeechModes.OnCameraDialogue)
        {
            if (string.IsNullOrWhiteSpace(speech.SpeakerName))
            {
                throw Invalid("fal_on_camera_speaker_required", "Cảnh nói trực tiếp phải xác định nhân vật nói.");
            }
            var speaker = JsonSerializer.Serialize(speech.SpeakerName.Trim(), TextJsonOptions);
            var recovery = recoveryProfile == FalVeoPolicy.SpeechRecoveryProfile
                ? " PHỤC HỒI LỜI NÓI: dùng trung cận hoặc trung cảnh tĩnh; giữ mặt, môi, miệng và hàm không bị che; bắt đầu nói ngay; chỉ dùng cử chỉ đơn giản; giảm âm nền xuống tối thiểu."
                : string.Empty;
            return
                $"LỜI NHÂN VẬT TRỰC TIẾP — ƯU TIÊN CAO NHẤT: Nhân vật duy nhất trong first-frame, tên {speaker}, là người nói duy nhất. " +
                $"Nhân vật bắt đầu nói trong 0,5 giây đầu và nói đúng một lần nguyên văn chuỗi JSON sau, không dịch, không diễn giải, không thêm hoặc bớt từ: {exactText}. " +
                $"Ngôn ngữ: {language}. Giọng và cách thể hiện: {voiceStyle}. Hoàn tất câu trước ranh giới nội dung của clip. " +
                "Giữ mặt, môi, miệng và hàm nhìn rõ trong toàn bộ câu; đồng bộ môi, biểu cảm và cử chỉ tự nhiên với từng từ. " +
                $"Không tạo narrator, giọng phụ hay màn trình diễn im lặng. Âm nền: {ambience}. Hiệu ứng: {effects}." + recovery;
        }
        if (speech.Mode == KlingSpeechModes.NativeVoiceOver)
        {
            if (!string.IsNullOrWhiteSpace(speech.SpeakerName))
            {
                throw Invalid("fal_speech_mode_invalid", "Voice-over không được gắn với một nhân vật trên màn hình.");
            }
            return
                $"LỜI DẪN NATIVE NGOÀI KHUNG HÌNH — ƯU TIÊN CAO NHẤT: Một narrator nói đúng một lần nguyên văn chuỗi JSON sau, không dịch, không diễn giải, không thêm hoặc bớt từ: {exactText}. " +
                $"Ngôn ngữ: {language}. Giọng và cách thể hiện: {voiceStyle}. Không nhân vật nào trên màn hình nói. Âm nền: {ambience}. Hiệu ứng: {effects}.";
        }

        throw Invalid("fal_speech_mode_invalid", "Kiểu lời của cảnh không được Veo hỗ trợ.");
    }

    private static KlingPromptValidationException Invalid(string code, string message) => new(code, message);
}

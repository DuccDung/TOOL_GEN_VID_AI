using System.Text.Json;

namespace TOOL_SERVER.Generation;

internal static class FalVeoPolicy
{
    public const string QueueEndpointId = "fal-ai/veo3.1";
    public const string StandardEndpointId = "fal-ai/veo3.1/image-to-video";
    public const string FastEndpointId = "fal-ai/veo3.1/fast/image-to-video";
    public const string Resolution = "720p";
    public const string SafetyTolerance = "4";
    public const long MaximumReferenceImageBytes = 8L * 1024 * 1024;
    public const string VietnameseLanguageCode = "vi-VN";
    public const string LanguagePolicyVersion = "fal-veo-long-form-vietnamese-v1";
    public const string SpeechPolicyVersion = "fal-veo-long-form-speech-v1";
    public const string PromptTemplateVersion = "veo-native-audio-v1-vietnamese-speech-first";
    public const string SpeechRecoveryProfile = "veo-speech-recovery-v1";
    public const int ObjectLifecycleSeconds = 345600;

    private static readonly HashSet<string> ApprovedEndpointIds =
        new([StandardEndpointId, FastEndpointId], StringComparer.Ordinal);

    public static bool IsApprovedEndpoint(string endpointId) => ApprovedEndpointIds.Contains(endpointId);

    public static bool AppliesToLongForm(string? providerCode, string? structureType) =>
        string.Equals(providerCode, ProviderCodes.Fal, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(structureType, GenerationWorkflowTypes.OpenAiStructuredPlan, StringComparison.Ordinal);

    public static void ValidateSpeech(
        KlingNativeSpeechPrompt speech,
        string? declaredMode,
        int characterCount,
        string scenePrompt)
    {
        if (!string.Equals(declaredMode, speech.Mode, StringComparison.Ordinal))
        {
            throw Invalid(
                "fal_speech_intent_invalid",
                "Kiểu lời đã lưu không khớp nội dung thoại của cảnh. Hãy sửa hoặc sinh lại cảnh trước khi tạo video Veo.");
        }

        switch (speech.Mode)
        {
            case TOOL_SHARED.Contracts.Generation.KlingSpeechModes.OnCameraDialogue
                when characterCount != 1 || string.IsNullOrWhiteSpace(speech.SpeakerName):
                throw Invalid(
                    "fal_on_camera_speaker_required",
                    "Cảnh nhân vật nói trực tiếp bằng Veo phải có đúng một nhân vật đã khóa và một ảnh chính đã duyệt.");
            case TOOL_SHARED.Contracts.Generation.KlingSpeechModes.NativeVoiceOver when characterCount != 0:
                throw Invalid(
                    "fal_voice_over_character_not_allowed",
                    "Lời dẫn ngoài khung hình không được gắn với nhân vật nói trực tiếp.");
            case TOOL_SHARED.Contracts.Generation.KlingSpeechModes.OnCameraDialogue
                when KlingLongFormSpeechPolicy.HasOnCameraConflict(scenePrompt):
                throw Invalid(
                    "fal_on_camera_action_invalid",
                    "Prompt đang yêu cầu nhân vật im lặng, khép miệng hoặc dùng lời dẫn ngoài khung hình.");
        }
    }

    public static void ValidateFirstFrame(
        VideoProviderReferenceImage referenceImage,
        int? storedWidth,
        int? storedHeight,
        string aspectRatio)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(referenceImage.Base64Data);
        }
        catch (FormatException)
        {
            throw Invalid("fal_first_frame_invalid", "First-frame gửi cho Veo không có dữ liệu Base64 hợp lệ.");
        }
        if (bytes.LongLength is <= 0 or > MaximumReferenceImageBytes)
        {
            throw Invalid("fal_first_frame_size_invalid", "First-frame gửi cho Veo phải có dung lượng không quá 8 MB.");
        }

        (string MimeType, int Width, int Height) imageInfo;
        try
        {
            imageInfo = GeneratedImageValidator.ReadImageInfo(bytes);
        }
        catch (ProviderHttpException)
        {
            throw Invalid("fal_first_frame_invalid", "First-frame gửi cho Veo không phải ảnh PNG/JPEG hợp lệ.");
        }
        if (!string.Equals(imageInfo.MimeType, referenceImage.MimeType, StringComparison.Ordinal) ||
            (storedWidth is > 0 && storedWidth != imageInfo.Width) ||
            (storedHeight is > 0 && storedHeight != imageInfo.Height))
        {
            throw Invalid("fal_first_frame_metadata_invalid", "Kích thước hoặc định dạng first-frame không khớp asset đã duyệt.");
        }

        var validDimensions = aspectRatio switch
        {
            "16:9" => imageInfo.Width >= 1280 && imageInfo.Height >= 720 && HasAspect(imageInfo.Width, imageInfo.Height, 16, 9),
            "9:16" => imageInfo.Width >= 720 && imageInfo.Height >= 1280 && HasAspect(imageInfo.Width, imageInfo.Height, 9, 16),
            _ => false
        };
        if (!validDimensions)
        {
            throw Invalid(
                "fal_first_frame_aspect_invalid",
                $"First-frame Veo phải đạt tối thiểu 720p và đúng tỷ lệ {aspectRatio}; hệ thống không tự crop asset đã khóa.");
        }
    }

    public static bool MatchesRateMetadata(string? metadataJson, string endpointId)
    {
        if (!IsApprovedEndpoint(endpointId) || string.IsNullOrWhiteSpace(metadataJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            var root = document.RootElement;
            return TryReadString(root, "resolution", out var resolution) &&
                   resolution.Equals(Resolution, StringComparison.OrdinalIgnoreCase) &&
                   TryReadBoolean(root, "nativeAudio", out var nativeAudio) &&
                   nativeAudio &&
                   TryReadString(root, "endpointId", out var configuredEndpoint) &&
                   configuredEndpoint.Equals(endpointId, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadString(JsonElement root, string name, out string value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString() ?? string.Empty;
                return true;
            }
        }
        value = string.Empty;
        return false;
    }

    private static bool TryReadBoolean(JsonElement root, string name, out bool value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = property.Value.GetBoolean();
                return true;
            }
        }
        value = false;
        return false;
    }

    private static bool HasAspect(int width, int height, int ratioWidth, int ratioHeight)
    {
        var difference = Math.Abs((long)width * ratioHeight - (long)height * ratioWidth);
        var scale = Math.Max((long)width * ratioHeight, (long)height * ratioWidth);
        return difference * 100 <= scale;
    }

    private static KlingPromptValidationException Invalid(string code, string message) =>
        new(code, message);
}

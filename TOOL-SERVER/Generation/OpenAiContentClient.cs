using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TOOL_SERVER.Authentication;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Projects;

namespace TOOL_SERVER.Generation;

internal sealed record OpenAiContentResult(
    GeneratedContentPlan Plan,
    long InputTokens,
    long OutputTokens,
    string ResponseId);

internal interface IOpenAiContentClient
{
    Task<OpenAiContentResult> GenerateAsync(
        ProviderRuntimeConfiguration provider,
        string topic,
        string languageCode,
        string platform,
        string aspectRatio,
        int targetDurationSeconds,
        string safetyIdentifier,
        CancellationToken cancellationToken);

    Task<OpenAiContentResult> GenerateWithVideoConstraintsAsync(
        ProviderRuntimeConfiguration provider,
        string topic,
        string languageCode,
        string platform,
        string aspectRatio,
        int targetDurationSeconds,
        string safetyIdentifier,
        VideoModelCapabilities videoCapabilities,
        bool enforceKlingLongFormSpeechPolicy,
        decimal speakingRate,
        CancellationToken cancellationToken) =>
        GenerateAsync(
            provider,
            topic,
            languageCode,
            platform,
            aspectRatio,
            targetDurationSeconds,
            safetyIdentifier,
            cancellationToken);

    Task<OpenAiContentResult> RepairWithVideoConstraintsAsync(
        ProviderRuntimeConfiguration provider,
        GeneratedContentPlan rejectedPlan,
        IReadOnlyList<ContentLanguageViolation> violations,
        string languageCode,
        string platform,
        string aspectRatio,
        int targetDurationSeconds,
        string safetyIdentifier,
        VideoModelCapabilities videoCapabilities,
        bool enforceKlingLongFormSpeechPolicy,
        decimal speakingRate,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Content repair is not supported by this OpenAI client.");
}

internal sealed class OpenAiContentClient(IHttpClientFactory httpClientFactory) : IOpenAiContentClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<OpenAiContentResult> GenerateAsync(
        ProviderRuntimeConfiguration provider,
        string topic,
        string languageCode,
        string platform,
        string aspectRatio,
        int targetDurationSeconds,
        string safetyIdentifier,
        CancellationToken cancellationToken) =>
        await GenerateWithVideoConstraintsAsync(
            provider,
            topic,
            languageCode,
            platform,
            aspectRatio,
            targetDurationSeconds,
            safetyIdentifier,
            VideoModelCapabilities.KlingDefault,
            true,
            1m,
            cancellationToken);

    public async Task<OpenAiContentResult> GenerateWithVideoConstraintsAsync(
        ProviderRuntimeConfiguration provider,
        string topic,
        string languageCode,
        string platform,
        string aspectRatio,
        int targetDurationSeconds,
        string safetyIdentifier,
        VideoModelCapabilities videoCapabilities,
        bool enforceKlingLongFormSpeechPolicy,
        decimal speakingRate,
        CancellationToken cancellationToken)
    {
        if (targetDurationSeconds > 360)
        {
            throw new ArgumentException("Luồng tạo nội dung tự động hiện hỗ trợ video tối đa 360 giây.");
        }

        var durationAllocations = VideoDurationAllocator.Allocate(targetDurationSeconds, videoCapabilities);
        var sceneCount = durationAllocations.Count;
        var durations = durationAllocations.Select(x => x.ContentDurationSeconds).ToArray();
        var generationDurations = durationAllocations.Select(x => x.GenerationDurationSeconds).ToArray();
        var instructions = CreateGenerationInstructions(languageCode, enforceKlingLongFormSpeechPolicy);
        var input = CreateGenerationInput(
            topic,
            languageCode,
            platform,
            aspectRatio,
            targetDurationSeconds,
            durations,
            generationDurations,
            enforceKlingLongFormSpeechPolicy,
            speakingRate);
        return await SendPlanRequestAsync(
            provider,
            languageCode,
            instructions,
            input,
            sceneCount,
            durations,
            generationDurations,
            safetyIdentifier,
            enforceKlingLongFormSpeechPolicy,
            cancellationToken);
    }

    public async Task<OpenAiContentResult> RepairWithVideoConstraintsAsync(
        ProviderRuntimeConfiguration provider,
        GeneratedContentPlan rejectedPlan,
        IReadOnlyList<ContentLanguageViolation> violations,
        string languageCode,
        string platform,
        string aspectRatio,
        int targetDurationSeconds,
        string safetyIdentifier,
        VideoModelCapabilities videoCapabilities,
        bool enforceKlingLongFormSpeechPolicy,
        decimal speakingRate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rejectedPlan);
        if (violations.Count == 0)
        {
            throw new ArgumentException("Danh sách trường cần sửa không được rỗng.", nameof(violations));
        }

        var durationAllocations = VideoDurationAllocator.Allocate(targetDurationSeconds, videoCapabilities);
        var sceneCount = durationAllocations.Count;
        var durations = durationAllocations.Select(x => x.ContentDurationSeconds).ToArray();
        var generationDurations = durationAllocations.Select(x => x.GenerationDurationSeconds).ToArray();
        var isVietnamese = IsVietnamese(languageCode);
        var violationLines = string.Join(
            "\n",
            violations.Select(x => FormatRepairViolation(x, isVietnamese)));
        var speechContracts = CreateSpeechContracts(
            durations,
            enforceKlingLongFormSpeechPolicy,
            languageCode,
            speakingRate);
        var instructions = isVietnamese
            ? "Bạn là biên tập viên sửa content plan video có cấu trúc. Trả về duy nhất JSON đúng schema. " +
              "Chỉ sửa các trường được liệt kê là rỗng, chưa đạt tiếng Việt hoặc chưa khớp nhịp lời; giữ nguyên ý nghĩa và mọi trường đã hợp lệ. " +
              "Tuyệt đối giữ nguyên character_key, asset_key, asset_type, speech_mode, speaker_character_key, thứ tự cảnh, số cảnh, thời lượng, character_keys và asset_keys. " +
              "Mọi giá trị người đọc được phải là tiếng Việt tự nhiên có dấu; tên riêng, thương hiệu và model có thể giữ nguyên. " +
              "Khi sửa nhịp lời, bổ sung hoặc rút gọn nội dung có ích; không lặp ý, chèn từ đệm hoặc kéo dài bằng dấu câu. " +
              "Trước khi trả kết quả, tự kiểm tra lại toàn bộ chuỗi và không để sót câu mô tả tiếng Anh."
            : "You repair a structured video content plan. Return only JSON matching the schema. " +
              "Change only the listed invalid fields, including speech pacing issues, and preserve every valid field, machine key, enum, scene order, duration and mapping.";
        var planJson = JsonSerializer.Serialize(rejectedPlan, JsonOptions);
        var input = isVietnamese
            ? $"Hãy sửa content plan sau cho ngôn ngữ {languageCode}, nền tảng {platform}, tỷ lệ {aspectRatio}.\n" +
              $"Các trường cần sửa:\n{violationLines}\nRàng buộc nhịp lời:\n{speechContracts}\nContent plan hiện tại:\n{planJson}"
            : $"Repair this content plan for language {languageCode}, platform {platform}, aspect ratio {aspectRatio}.\n" +
              $"Invalid fields:\n{violationLines}\nSpeech contracts:\n{speechContracts}\nCurrent content plan:\n{planJson}";

        return await SendPlanRequestAsync(
            provider,
            languageCode,
            instructions,
            input,
            sceneCount,
            durations,
            generationDurations,
            safetyIdentifier,
            enforceKlingLongFormSpeechPolicy,
            cancellationToken);
    }

    private async Task<OpenAiContentResult> SendPlanRequestAsync(
        ProviderRuntimeConfiguration provider,
        string languageCode,
        string instructions,
        string input,
        int sceneCount,
        IReadOnlyList<int> durations,
        IReadOnlyList<int> generationDurations,
        string safetyIdentifier,
        bool enforceKlingLongFormSpeechPolicy,
        CancellationToken cancellationToken)
    {
        var schema = CreateSchema(sceneCount, languageCode);
        var requestBody = new
        {
            model = provider.ModelCode,
            safety_identifier = safetyIdentifier,
            instructions,
            input,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "video_content_plan",
                    strict = true,
                    schema
                }
            },
            max_output_tokens = 8000,
            store = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.BaseUri, "responses"))
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions)
        };
        ApplyAuthentication(request, provider);

        using var response = await httpClientFactory.CreateClient("OpenAiRuntime")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ProviderHttpException.FromResponse(ProviderCodes.OpenAi, response.StatusCode, responseJson);
        }

        using var document = ParseJson(responseJson, ProviderCodes.OpenAi);
        var root = document.RootElement;
        var outputText = ExtractOutputText(root);
        OpenAiPlanDto planDto;
        try
        {
            planDto = JsonSerializer.Deserialize<OpenAiPlanDto>(outputText, JsonOptions)
                ?? throw new JsonException("OpenAI returned an empty content plan.");
        }
        catch (JsonException exception)
        {
            throw new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_invalid_structured_output",
                "OpenAI trả về content plan không đúng cấu trúc.",
                exception);
        }

        var preserveVietnameseHumanReadableGaps = IsVietnamese(languageCode);
        ValidatePlan(
            planDto,
            sceneCount,
            durations,
            enforceKlingLongFormSpeechPolicy,
            preserveVietnameseHumanReadableGaps);
        var characters = planDto.Characters
            .Select(character => new GeneratedCharacterProfile(
                RequiredCharacterKey(character.CharacterKey),
                HumanReadable(character.Name, "characters.name", preserveVietnameseHumanReadableGaps),
                HumanReadable(character.Role, "characters.role", preserveVietnameseHumanReadableGaps),
                HumanReadable(character.Gender, "characters.gender", preserveVietnameseHumanReadableGaps),
                character.Age,
                HumanReadable(character.Face, "characters.face", preserveVietnameseHumanReadableGaps),
                HumanReadable(character.Hair, "characters.hair", preserveVietnameseHumanReadableGaps),
                HumanReadable(character.Skin, "characters.skin", preserveVietnameseHumanReadableGaps),
                HumanReadable(character.Body, "characters.body", preserveVietnameseHumanReadableGaps),
                HumanReadable(character.Clothing, "characters.clothing", preserveVietnameseHumanReadableGaps),
                HumanReadable(character.Accessories, "characters.accessories", preserveVietnameseHumanReadableGaps),
                HumanReadable(character.VisualIdentity, "characters.visual_identity", preserveVietnameseHumanReadableGaps),
                HumanReadableList(character.ImmutableTraits, "characters.immutable_traits", preserveVietnameseHumanReadableGaps),
                HumanReadableList(character.ForbiddenChanges, "characters.forbidden_changes", preserveVietnameseHumanReadableGaps)))
            .ToArray();
        var scenes = planDto.Scenes
            .Select((scene, index) => new GeneratedContentScene(
                index + 1,
                HumanReadable(scene.StoryPurpose, "story_purpose", preserveVietnameseHumanReadableGaps),
                NormalizeSpokenText(scene.SpokenText, scene.SpeechMode, preserveVietnameseHumanReadableGaps),
                HumanReadable(scene.VisualPrompt, "visual_prompt", preserveVietnameseHumanReadableGaps),
                durations[index],
                scene.CharacterKeys
                    .Select(RequiredCharacterKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Required(scene.SpeechMode, "speech_mode"),
                NullIfWhiteSpace(scene.SpeakerCharacterKey),
                NormalizeVietnameseSentinel(
                    HumanReadable(scene.VoiceStyle, "voice_style", preserveVietnameseHumanReadableGaps),
                    languageCode,
                    "voice_style"),
                NormalizeVietnameseSentinel(
                    HumanReadable(scene.AmbientAudio, "ambient_audio", preserveVietnameseHumanReadableGaps),
                    languageCode,
                    "ambient_audio"),
                NormalizeVietnameseSentinel(
                    HumanReadable(scene.SoundEffects, "sound_effects", preserveVietnameseHumanReadableGaps),
                    languageCode,
                    "sound_effects"),
                scene.AssetKeys.Select(RequiredAssetKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                generationDurations[index]))
            .ToArray();
        var assets = planDto.Assets
            .Select(asset =>
            {
                var key = RequiredAssetKey(asset.AssetKey);
                return new GeneratedProjectAsset(
                    key,
                    RequiredAssetType(asset.AssetType),
                    HumanReadable(asset.Name, "assets.name", preserveVietnameseHumanReadableGaps),
                    HumanReadable(
                        asset.CanonicalDescription,
                        "assets.canonical_description",
                        preserveVietnameseHumanReadableGaps),
                    scenes
                        .Where(scene => (scene.AssetKeys ?? []).Contains(key, StringComparer.OrdinalIgnoreCase))
                        .Select(scene => scene.SequenceNumber)
                        .ToArray());
            })
            .ToArray();
        var usage = root.TryGetProperty("usage", out var usageElement) ? usageElement : default;

        return new OpenAiContentResult(
            new GeneratedContentPlan(
                HumanReadable(planDto.Title, "title", preserveVietnameseHumanReadableGaps),
                HumanReadable(planDto.Hook, "hook", preserveVietnameseHumanReadableGaps),
                HumanReadable(planDto.Angle, "angle", preserveVietnameseHumanReadableGaps),
                HumanReadable(planDto.Audience, "audience", preserveVietnameseHumanReadableGaps),
                HumanReadable(planDto.CallToAction, "call_to_action", preserveVietnameseHumanReadableGaps),
                HumanReadable(planDto.ScriptFullText, "script_full_text", preserveVietnameseHumanReadableGaps),
                HumanReadable(planDto.VisualStyle, "visual_style", preserveVietnameseHumanReadableGaps),
                HumanReadable(planDto.NegativePrompt, "negative_prompt", preserveVietnameseHumanReadableGaps),
                characters,
                scenes,
                assets),
            GetInt64(usage, "input_tokens"),
            GetInt64(usage, "output_tokens"),
            root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty);
    }

    private static string CreateGenerationInstructions(
        string languageCode,
        bool enforceKlingLongFormSpeechPolicy)
    {
        if (IsVietnamese(languageCode))
        {
            var speechPolicy = enforceKlingLongFormSpeechPolicy
                ? "Với video dài Native Audio: một nhân vật hiện rõ cùng spoken_text phải dùng OnCameraDialogue; lời đọc không có nhân vật phải dùng NativeVoiceOver và character_keys rỗng. Trong cảnh OnCameraDialogue, visual_prompt phải mô tả khuôn mặt và miệng người nói nhìn rõ, bắt đầu nói sớm và có cử chỉ tự nhiên; hành động chính không được chỉ đứng, tạo dáng hoặc mỉm cười. "
                : string.Empty;
            return "Bạn là chiến lược gia video và biên kịch giàu kinh nghiệm. Chỉ trả về dữ liệu đúng JSON Schema đã cung cấp. " +
                   "Chủ đề đầu vào có thể ở bất kỳ ngôn ngữ nào, nhưng mọi giá trị người đọc được phải viết bằng tiếng Việt tự nhiên, đúng dấu. " +
                   "Quy tắc này áp dụng cho title, hook, angle, audience, call_to_action, script_full_text, visual_style, negative_prompt; " +
                   "mọi giá trị role, gender, age, face, hair, skin, body, clothing, accessories, visual_identity, immutable_traits, forbidden_changes; " +
                   "mọi asset name, canonical_description; và mọi story_purpose, spoken_text, visual_prompt, voice_style, ambient_audio, sound_effects của cảnh. " +
                   "Chỉ giữ nguyên tên riêng, thương hiệu hoặc model không nên dịch. Giữ nguyên tên thuộc tính JSON, character_key, asset_key và các enum vì đó là mã máy. " +
                   "spoken_text là câu được provider đọc đúng một lần, không phải tóm tắt cảnh và không sao chép toàn bộ script_full_text. visual_prompt chỉ mô tả nội dung nhìn thấy. " +
                   "Mỗi cảnh có tối đa một người nói. Chọn OnCameraDialogue khi một người dẫn hiện trên hình, NativeVoiceOver khi người đọc ngoài hình và None khi không có lời. " +
                   speechPolicy +
                   "Tách riêng phong cách giọng, âm thanh môi trường và hiệu ứng âm thanh. Chỉ tạo tối đa một người dẫn lặp lại; tái sử dụng đúng character_key và nhận diện bất biến qua các cảnh. " +
                   "Tạo thư viện tài sản nhất quán: mỗi cảnh có đúng một Background, có thể có Prop hoặc Item quan trọng, và tái sử dụng đúng asset_key cho cùng địa điểm/vật thể. " +
                   "Ví dụ cách viết: negative_prompt = “Không chữ trên màn hình, không logo, không méo khuôn mặt”; voice_style = “Giọng nữ trẻ, ấm áp và rõ ràng”; ambient_audio = “Âm thanh căn phòng yên tĩnh”; sound_effects = “Tiếng đặt cốc nhẹ lên bàn”; canonical_description = “Chiếc bàn gỗ sồi sáng màu, mặt bàn nhẵn và chân vuông”. " +
                   "Trước khi trả JSON, tự kiểm tra mọi trường bắt buộc, mọi chuỗi còn tiếng Anh và sự phù hợp giữa spoken_text với speech_mode; tự sửa hết lỗi phát hiện được.";
        }

        var languageInstructions = string.Equals(languageCode, "en-US", StringComparison.OrdinalIgnoreCase)
            ? "Write every human-readable text field in English. This includes title, hook, angle, audience, call_to_action, script_full_text, visual_style, negative_prompt; " +
              "every character role, gender, face, hair, skin, body, clothing, accessories, visual_identity, immutable_traits and forbidden_changes value; " +
              "every asset name and canonical_description; and every scene story_purpose, spoken_text, visual_prompt, voice_style, ambient_audio and sound_effects value. " +
              "Character names may remain proper nouns. Keep machine identifiers, enum values and JSON property names unchanged. All spoken_text must be natural English. "
            : "Write every audience-facing value in the requested project language. Keep machine identifiers, enum values and JSON property names unchanged. ";
        var speechPolicyInstructions = enforceKlingLongFormSpeechPolicy
            ? "For long-form native audio, one visible character plus spoken_text requires OnCameraDialogue; off-screen speech requires NativeVoiceOver with an empty character_keys array. "
            : string.Empty;
        return "You are a senior video strategist and screenwriter. Return only data matching the supplied JSON schema. " +
               languageInstructions +
               "spoken_text is the exact literal utterance and visual_prompt only describes visible content. Every scene has at most one speaker. " +
               speechPolicyInstructions +
               "Create at most one recurring presenter and reuse stable character_key and asset_key values across scenes.";
    }

    private static string CreateGenerationInput(
        string topic,
        string languageCode,
        string platform,
        string aspectRatio,
        int targetDurationSeconds,
        IReadOnlyList<int> durations,
        IReadOnlyList<int> generationDurations,
        bool enforceKlingLongFormSpeechPolicy,
        decimal speakingRate)
    {
        var speechContracts = CreateSpeechContracts(
            durations,
            enforceKlingLongFormSpeechPolicy,
            languageCode,
            speakingRate);
        if (IsVietnamese(languageCode))
        {
            return $"Hãy tạo content plan video hoàn chỉnh. Chủ đề: {topic}\nNgôn ngữ: {languageCode}\nNền tảng: {platform}\nTỷ lệ khung hình: {aspectRatio}\n" +
                   $"Tổng thời lượng chính xác: {targetDurationSeconds} giây. Trả đúng {durations.Count} cảnh theo thứ tự thời gian. " +
                   $"Thời lượng nội dung bắt buộc: {string.Join(", ", durations.Select((seconds, index) => $"cảnh {index + 1} = {seconds} giây"))}. " +
                   $"Thời lượng provider cố định: {string.Join(", ", generationDurations.Select((seconds, index) => $"cảnh {index + 1} = {seconds} giây"))}. " +
                   "Hoàn thành spoken_text trước mốc thời lượng nội dung; phần đuôi chỉ dành cho provider sẽ bị cắt. " +
                   "Với cảnh có lời, ưu tiên lời đọc chiếm 85–95% thời lượng nội dung, đủ ý và tự nhiên; không kéo dài bằng từ đệm, lặp ý hoặc dấu câu giả tạo. " +
                   $"Ràng buộc spoken_text bắt buộc:\n{speechContracts}\n" +
                   "Mỗi visual_prompt phải mô tả chủ thể, môi trường, ánh sáng, hành động, cỡ cảnh và chuyển động máy mà không lặp spoken_text. " +
                   "Nếu có người dẫn lặp lại, giữ nguyên khuôn mặt, tóc, tỷ lệ cơ thể, trang phục và phụ kiện. Khai báo tài sản dùng chung một lần trong assets rồi tham chiếu bằng asset_keys.";
        }

        return $"Create a complete video content plan. Topic: {topic}\nLanguage: {languageCode}\nPlatform: {platform}\nAspect ratio: {aspectRatio}\n" +
               $"Exact total duration: {targetDurationSeconds} seconds. Return exactly {durations.Count} scenes. " +
               $"Content durations: {string.Join(", ", durations.Select((seconds, index) => $"scene {index + 1} = {seconds}s"))}. " +
               $"Provider durations: {string.Join(", ", generationDurations.Select((seconds, index) => $"scene {index + 1} = {seconds}s"))}. " +
               $"Mandatory spoken_text contracts:\n{speechContracts}";
    }

    private static bool IsVietnamese(string languageCode) =>
        string.Equals(
            languageCode,
            KlingLongFormLanguagePolicy.VietnameseLanguageCode,
            StringComparison.OrdinalIgnoreCase);

    private static JsonObject CreateSchema(int sceneCount, string languageCode) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("title", "hook", "angle", "audience", "call_to_action", "script_full_text", "visual_style", "negative_prompt", "characters", "assets", "scenes"),
        ["properties"] = new JsonObject
        {
            ["title"] = HumanReadableStringSchema(languageCode, "The audience-facing video title."),
            ["hook"] = HumanReadableStringSchema(languageCode, "The opening hook."),
            ["angle"] = HumanReadableStringSchema(languageCode, "The editorial angle."),
            ["audience"] = HumanReadableStringSchema(languageCode, "The intended audience description."),
            ["call_to_action"] = HumanReadableStringSchema(languageCode, "The audience-facing call to action."),
            ["script_full_text"] = HumanReadableStringSchema(languageCode, "The complete script."),
            ["visual_style"] = HumanReadableStringSchema(languageCode, "The visual style description."),
            ["negative_prompt"] = HumanReadableStringSchema(
                languageCode,
                "A descriptive list of visual elements to avoid; translate descriptive terms and retain only unavoidable proper names or model names."),
            ["characters"] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 0,
                ["maxItems"] = 1,
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray(
                        "character_key", "name", "role", "gender", "age", "face", "hair", "skin", "body",
                        "clothing", "accessories", "visual_identity", "immutable_traits", "forbidden_changes"),
                    ["properties"] = new JsonObject
                    {
                        ["character_key"] = StringSchema(MachineDescription(
                            languageCode,
                            "Stable lowercase machine identifier; do not translate it.",
                            "Mã máy ổn định viết thường; giữ nguyên, không dịch.")),
                        ["name"] = StringSchema(MachineDescription(
                            languageCode,
                            "The character's proper name; preserve an established name without translating it.",
                            "Tên riêng của nhân vật; giữ nguyên tên đã xác lập, không dịch.")),
                        ["role"] = HumanReadableStringSchema(languageCode, "The character's role."),
                        ["gender"] = HumanReadableStringSchema(languageCode, "The character's gender description."),
                        ["age"] = new JsonObject { ["type"] = new JsonArray("integer", "null"), ["minimum"] = 1, ["maximum"] = 120 },
                        ["face"] = HumanReadableStringSchema(languageCode, "The character's facial appearance."),
                        ["hair"] = HumanReadableStringSchema(languageCode, "The character's hair."),
                        ["skin"] = HumanReadableStringSchema(languageCode, "The character's skin appearance."),
                        ["body"] = HumanReadableStringSchema(languageCode, "The character's body proportions."),
                        ["clothing"] = HumanReadableStringSchema(languageCode, "The character's clothing."),
                        ["accessories"] = HumanReadableStringSchema(languageCode, "The character's accessories."),
                        ["visual_identity"] = HumanReadableStringSchema(languageCode, "The immutable visual identity summary."),
                        ["immutable_traits"] = StringArraySchema(
                            1,
                            12,
                            HumanReadableDescription(languageCode, "One immutable character trait.")),
                        ["forbidden_changes"] = StringArraySchema(
                            1,
                            12,
                            HumanReadableDescription(languageCode, "One forbidden character change."))
                    }
                }
            },
            ["assets"] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 1,
                ["maxItems"] = Math.Min(60, Math.Max(6, sceneCount * 6)),
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("asset_key", "asset_type", "name", "canonical_description"),
                    ["properties"] = new JsonObject
                    {
                        ["asset_key"] = StringSchema(MachineDescription(
                            languageCode,
                            "Stable lowercase machine identifier; do not translate it.",
                            "Mã máy ổn định viết thường; giữ nguyên, không dịch.")),
                        ["asset_type"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray(ProjectAssetTypes.Background, ProjectAssetTypes.Prop, ProjectAssetTypes.Item)
                        },
                        ["name"] = HumanReadableStringSchema(
                            languageCode,
                            "A short display name for the asset; retain unavoidable proper names, brands or model names."),
                        ["canonical_description"] = HumanReadableStringSchema(
                            languageCode,
                            "The immutable visual details needed to keep this asset consistent between clips.")
                    }
                }
            },
            ["scenes"] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = sceneCount,
                ["maxItems"] = sceneCount,
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray(
                        "sequence_number", "story_purpose", "visual_prompt", "character_keys",
                        "asset_keys", "speech_mode", "spoken_text", "speaker_character_key", "voice_style", "ambient_audio", "sound_effects"),
                    ["properties"] = new JsonObject
                    {
                        ["sequence_number"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 },
                        ["story_purpose"] = HumanReadableStringSchema(languageCode, "The narrative purpose of this scene."),
                        ["visual_prompt"] = HumanReadableStringSchema(
                            languageCode,
                            "Visible subject, environment, lighting, action, camera framing and motion; do not repeat spoken text."),
                        ["character_keys"] = StringArraySchema(
                            0,
                            1,
                            MachineDescription(
                                languageCode,
                                "Stable lowercase character_key; do not translate it.",
                                "character_key ổn định viết thường; giữ nguyên, không dịch.")),
                        ["asset_keys"] = StringArraySchema(
                            1,
                            20,
                            MachineDescription(
                                languageCode,
                                "Stable lowercase asset_key; do not translate it.",
                                "asset_key ổn định viết thường; giữ nguyên, không dịch.")),
                        ["speech_mode"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray(
                                KlingSpeechModes.None,
                                KlingSpeechModes.OnCameraDialogue,
                                KlingSpeechModes.NativeVoiceOver)
                        },
                        ["spoken_text"] = HumanReadableStringSchema(
                            languageCode,
                            "The exact literal utterance; use an empty string only when speech_mode is None."),
                        ["speaker_character_key"] = NullableStringSchema(MachineDescription(
                            languageCode,
                            "A character_key machine identifier or null; do not translate it.",
                            "Một mã máy character_key hoặc null; giữ nguyên, không dịch.")),
                        ["voice_style"] = HumanReadableStringSchema(languageCode, "The intended speaking style."),
                        ["ambient_audio"] = HumanReadableStringSchema(languageCode, "The audible ambience."),
                        ["sound_effects"] = HumanReadableStringSchema(
                            languageCode,
                            "The audible sound effects; express onomatopoeia inside a natural description.")
                    }
                }
            }
        }
    };

    private static JsonObject HumanReadableStringSchema(
        string languageCode,
        string purpose) =>
        StringSchema(HumanReadableDescription(languageCode, purpose));

    private static string HumanReadableDescription(string languageCode, string purpose)
    {
        if (string.Equals(
                languageCode,
                KlingLongFormLanguagePolicy.VietnameseLanguageCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return $"{VietnameseHumanReadablePurpose(purpose)} Viết trường này bằng tiếng Việt tự nhiên, đúng dấu. Không dùng câu mô tả tiếng Anh; chỉ giữ tên riêng, thương hiệu hoặc model không nên dịch.";
        }

        if (string.Equals(languageCode, "en-US", StringComparison.OrdinalIgnoreCase))
        {
            return $"{purpose} Write this field in natural English.";
        }

        return $"{purpose} Write this field in the requested project language.";
    }

    private static string VietnameseHumanReadablePurpose(string purpose) => purpose switch
    {
        "The audience-facing video title." => "Tiêu đề video dành cho người xem.",
        "The opening hook." => "Câu mở đầu thu hút người xem.",
        "The editorial angle." => "Góc triển khai nội dung.",
        "The intended audience description." => "Mô tả đối tượng người xem.",
        "The audience-facing call to action." => "Lời kêu gọi hành động dành cho người xem.",
        "The complete script." => "Toàn bộ kịch bản.",
        "The visual style description." => "Mô tả phong cách hình ảnh.",
        "A descriptive list of visual elements to avoid; translate descriptive terms and retain only unavoidable proper names or model names." =>
            "Danh sách mô tả các yếu tố hình ảnh cần tránh; chỉ giữ nguyên tên riêng hoặc tên model không thể dịch.",
        "The character's role." => "Vai trò của nhân vật.",
        "The character's gender description." => "Mô tả giới tính của nhân vật.",
        "The character's facial appearance." => "Mô tả khuôn mặt của nhân vật.",
        "The character's hair." => "Mô tả mái tóc của nhân vật.",
        "The character's skin appearance." => "Mô tả làn da của nhân vật.",
        "The character's body proportions." => "Mô tả tỷ lệ cơ thể của nhân vật.",
        "The character's clothing." => "Mô tả trang phục của nhân vật.",
        "The character's accessories." => "Mô tả phụ kiện của nhân vật.",
        "The immutable visual identity summary." => "Tóm tắt nhận diện hình ảnh bất biến.",
        "One immutable character trait." => "Một đặc điểm nhân vật bất biến.",
        "One forbidden character change." => "Một thay đổi nhân vật bị cấm.",
        "A short display name for the asset; retain unavoidable proper names, brands or model names." =>
            "Tên hiển thị ngắn của tài sản; giữ nguyên tên riêng, thương hiệu hoặc model không nên dịch.",
        "The immutable visual details needed to keep this asset consistent between clips." =>
            "Các chi tiết hình ảnh bất biến để giữ tài sản nhất quán giữa các clip.",
        "The narrative purpose of this scene." => "Mục đích kể chuyện của cảnh.",
        "Visible subject, environment, lighting, action, camera framing and motion; do not repeat spoken text." =>
            "Chủ thể, môi trường, ánh sáng, hành động, cỡ cảnh và chuyển động máy nhìn thấy được; không lặp lại lời nói.",
        "The exact literal utterance; use an empty string only when speech_mode is None." =>
            "Câu nói nguyên văn; chỉ để chuỗi rỗng khi speech_mode là None.",
        "The intended speaking style." => "Phong cách giọng nói mong muốn.",
        "The audible ambience." => "Âm thanh môi trường nghe được.",
        "The audible sound effects; express onomatopoeia inside a natural description." =>
            "Hiệu ứng âm thanh nghe được; đặt từ tượng thanh trong một mô tả tự nhiên.",
        _ => "Giá trị văn bản dành cho người đọc."
    };

    private static string MachineDescription(
        string languageCode,
        string englishDescription,
        string vietnameseDescription) =>
        IsVietnamese(languageCode) ? vietnameseDescription : englishDescription;

    private static JsonObject StringSchema(string? description = null)
    {
        var schema = new JsonObject { ["type"] = "string" };
        if (!string.IsNullOrWhiteSpace(description))
        {
            schema["description"] = description;
        }
        return schema;
    }

    private static JsonObject NullableStringSchema(string? description = null)
    {
        var schema = new JsonObject { ["type"] = new JsonArray("string", "null") };
        if (!string.IsNullOrWhiteSpace(description))
        {
            schema["description"] = description;
        }
        return schema;
    }

    private static JsonObject StringArraySchema(
        int minimum,
        int maximum,
        string? itemDescription = null) => new()
    {
        ["type"] = "array",
        ["minItems"] = minimum,
        ["maxItems"] = maximum,
        ["items"] = StringSchema(itemDescription)
    };

    private static void ValidatePlan(
        OpenAiPlanDto plan,
        int sceneCount,
        IReadOnlyList<int> durations,
        bool enforceKlingLongFormSpeechPolicy,
        bool preserveHumanReadableGaps)
    {
        if (plan.Scenes is null || plan.Scenes.Count != sceneCount)
        {
            throw new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_invalid_scene_count",
                $"OpenAI không trả về đúng {sceneCount} cảnh.");
        }

        if (plan.Characters.Count > 1)
        {
            throw new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_invalid_character_count",
                "Content plan hiện chỉ hỗ trợ một nhân vật xuyên suốt.");
        }

        var characterKeys = plan.Characters
            .Select(character => RequiredCharacterKey(character.CharacterKey))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (characterKeys.Count != plan.Characters.Count ||
            plan.Scenes.Any(scene =>
                scene.CharacterKeys.Count > 1 ||
                scene.CharacterKeys.Any(key => !characterKeys.Contains(RequiredCharacterKey(key)))))
        {
            throw new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_invalid_character_mapping",
                "OpenAI trả về liên kết nhân vật và cảnh không hợp lệ.");
        }

        if (plan.Assets.Count is < 1 or > 60)
        {
            throw new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_invalid_asset_count",
                "OpenAI trả về số lượng tài sản cảnh không hợp lệ.");
        }
        var assetKeys = plan.Assets
            .Select(asset => RequiredAssetKey(asset.AssetKey))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var comparableAssetNames = plan.Assets
            .Select(asset => new
            {
                Type = RequiredAssetType(asset.AssetType),
                Name = HumanReadable(asset.Name, "assets.name", preserveHumanReadableGaps)
            })
            .Where(asset => asset.Name.Length > 0)
            .Select(asset => $"{asset.Type}\n{asset.Name}")
            .ToArray();
        if (assetKeys.Count != plan.Assets.Count ||
            comparableAssetNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != comparableAssetNames.Length)
        {
            throw new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_invalid_asset_mapping",
                "OpenAI trả về asset_key hoặc tên tài sản bị trùng.");
        }
        var assetTypesByKey = plan.Assets.ToDictionary(
            asset => RequiredAssetKey(asset.AssetKey),
            asset => RequiredAssetType(asset.AssetType),
            StringComparer.OrdinalIgnoreCase);
        foreach (var asset in plan.Assets)
        {
            if (HumanReadable(asset.Name, "assets.name", preserveHumanReadableGaps).Length > 160 ||
                HumanReadable(
                    asset.CanonicalDescription,
                    "assets.canonical_description",
                    preserveHumanReadableGaps).Length > 2000)
            {
                throw new ProviderHttpException(
                    ProviderCodes.OpenAi,
                    "openai_invalid_asset_description",
                    "OpenAI trả về tên hoặc mô tả tài sản quá dài.");
            }
        }
        foreach (var scene in plan.Scenes)
        {
            var sceneAssetKeys = scene.AssetKeys
                .Select(RequiredAssetKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (sceneAssetKeys.Length != scene.AssetKeys.Count ||
                sceneAssetKeys.Any(key => !assetKeys.Contains(key)) ||
                sceneAssetKeys.Count(key => assetTypesByKey[key] == ProjectAssetTypes.Background) != 1)
            {
                throw new ProviderHttpException(
                    ProviderCodes.OpenAi,
                    "openai_invalid_asset_mapping",
                    "Mỗi cảnh phải liên kết tới đúng một bối cảnh và chỉ dùng asset_key đã khai báo.");
            }
        }

        for (var index = 0; index < plan.Scenes.Count; index++)
        {
            ValidateSpeechIntent(
                plan.Scenes[index],
                enforceKlingLongFormSpeechPolicy,
                preserveHumanReadableGaps);
        }
    }

    private static void ValidateSpeechIntent(
        OpenAiSceneDto scene,
        bool enforceKlingLongFormSpeechPolicy,
        bool preserveHumanReadableGaps)
    {
        var mode = Required(scene.SpeechMode, "speech_mode");
        var spokenText = scene.SpokenText?.Trim() ?? string.Empty;
        var speaker = NullIfWhiteSpace(scene.SpeakerCharacterKey);
        switch (mode)
        {
            case KlingSpeechModes.None when spokenText.Length == 0 && speaker is null:
                break;
            case KlingSpeechModes.OnCameraDialogue
                when (spokenText.Length > 0 || preserveHumanReadableGaps) &&
                     speaker is not null &&
                     scene.CharacterKeys.Count == 1 &&
                      string.Equals(speaker, scene.CharacterKeys[0], StringComparison.OrdinalIgnoreCase) &&
                      (!enforceKlingLongFormSpeechPolicy ||
                       (preserveHumanReadableGaps && string.IsNullOrWhiteSpace(scene.VisualPrompt)) ||
                       KlingLongFormSpeechPolicy.HasVisibleSpeakingPerformance(scene.VisualPrompt)):
                break;
            case KlingSpeechModes.NativeVoiceOver
                when (spokenText.Length > 0 || preserveHumanReadableGaps) &&
                     speaker is null &&
                     (!enforceKlingLongFormSpeechPolicy || scene.CharacterKeys.Count == 0):
                break;
            default:
                throw new ProviderHttpException(
                    ProviderCodes.OpenAi,
                    "openai_invalid_speech_intent",
                    "OpenAI trả về người nói, kiểu lời hoặc nội dung lời không hợp lệ.");
        }

    }

    private static string CreateSpeechContracts(
        IReadOnlyList<int> durations,
        bool enforceKlingLongFormSpeechPolicy,
        string languageCode,
        decimal speakingRate) =>
        string.Join(
            "\n",
            durations.Select((durationSeconds, index) =>
            {
                if (IsVietnamese(languageCode))
                {
                    var guidance = SpeechPacingPolicy.CreateGuidance(durationSeconds, speakingRate);
                    var targetMinimum = guidance.TargetMinimumSeconds.ToString("0.##", CultureInfo.InvariantCulture);
                    var targetMaximum = guidance.TargetMaximumSeconds.ToString("0.##", CultureInfo.InvariantCulture);
                    var formattedSpeakingRate = speakingRate.ToString("0.##", CultureInfo.InvariantCulture);
                    var vietnameseContract = enforceKlingLongFormSpeechPolicy
                        ? " Một nhân vật cùng spoken_text phải dùng OnCameraDialogue với nhân vật đó là người nói và hiện rõ hành động nói; không có nhân vật cùng spoken_text phải dùng NativeVoiceOver với character_keys=[]."
                        : string.Empty;
                    return $"- cảnh {index + 1}: đúng {durationSeconds} giây; speech_mode=None yêu cầu spoken_text rỗng; " +
                           $"các mode khác yêu cầu spoken_text tiếng Việt tự nhiên, ước tính đọc {targetMinimum}–{targetMaximum} giây " +
                           $"(khoảng {guidance.SuggestedMinimumSpeechUnits}–{guidance.SuggestedMaximumSpeechUnits} âm tiết/cụm đọc ở tốc độ {formattedSpeakingRate}x), " +
                           $"không rỗng và không vượt thời lượng cảnh.{vietnameseContract}";
                }
                var strictContract = enforceKlingLongFormSpeechPolicy
                    ? " One character plus spoken_text requires OnCameraDialogue with that character as speaker and visible speaking action; zero characters plus spoken_text requires NativeVoiceOver; NativeVoiceOver requires character_keys=[]."
                    : string.Empty;
                return $"- scene {index + 1}: exactly {durationSeconds}s; speech_mode=None requires empty spoken_text; otherwise spoken_text must be non-empty and natural for the scene duration.{strictContract}";
            }));

    private static string FormatRepairViolation(ContentLanguageViolation violation, bool isVietnamese)
    {
        if (violation.Reason is not (ContentPlanViolationReasons.SpeechTooShort or ContentPlanViolationReasons.SpeechTooLong))
        {
            return $"- {violation.Field}: {violation.Reason}";
        }

        var direction = violation.Reason == ContentPlanViolationReasons.SpeechTooShort
            ? isVietnamese ? "lời quá ngắn" : "speech is too short"
            : isVietnamese ? "lời quá dài" : "speech is too long";
        var estimated = violation.EstimatedDurationSeconds?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?";
        var minimum = violation.TargetMinimumSeconds?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?";
        var maximum = violation.TargetMaximumSeconds?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?";
        return isVietnamese
            ? $"- {violation.Field}: {direction}; ước tính {estimated} giây, mục tiêu {minimum}–{maximum} giây"
            : $"- {violation.Field}: {direction}; estimated {estimated}s, target {minimum}–{maximum}s";
    }

    private static string NormalizeVietnameseSentinel(string value, string languageCode, string field)
    {
        if (!IsVietnamese(languageCode))
        {
            return value;
        }

        var normalized = value.Trim();
        if (normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("n/a", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("not applicable", StringComparison.OrdinalIgnoreCase))
        {
            return field == "voice_style" ? "Không áp dụng" : "Không có";
        }
        if (field == "ambient_audio" &&
            normalized.Equals("no ambient audio", StringComparison.OrdinalIgnoreCase))
        {
            return "Không có âm thanh môi trường";
        }
        if (field == "sound_effects" &&
            normalized.Equals("no sound effects", StringComparison.OrdinalIgnoreCase))
        {
            return "Không có hiệu ứng âm thanh";
        }

        return normalized;
    }

    private static string NormalizeSpokenText(
        string? value,
        string? mode,
        bool preserveHumanReadableGaps) =>
        string.Equals(mode, KlingSpeechModes.None, StringComparison.Ordinal)
            ? string.Empty
            : HumanReadable(value, "spoken_text", preserveHumanReadableGaps);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse();
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text" &&
                    part.TryGetProperty("text", out var text) && !string.IsNullOrWhiteSpace(text.GetString()))
                {
                    return text.GetString()!;
                }
            }
        }

        throw InvalidResponse();
    }

    private static JsonDocument ParseJson(string json, string providerCode)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ProviderHttpException(
                providerCode,
                $"{providerCode}_invalid_response",
                $"{providerCode} trả về dữ liệu không hợp lệ.",
                exception);
        }
    }

    private static long GetInt64(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result
            : 0;

    private static string Required(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_invalid_structured_output",
                $"OpenAI không trả về trường {field} hợp lệ.");
        }

        return value.Trim();
    }

    private static string HumanReadable(string? value, string field, bool preserveGaps) =>
        preserveGaps
            ? value?.Trim() ?? string.Empty
            : Required(value, field);

    private static IReadOnlyList<string> HumanReadableList(
        IReadOnlyList<string>? values,
        string field,
        bool preserveGaps)
    {
        if (!preserveGaps)
        {
            return RequiredList(values, field);
        }
        if (values is null || values.Count == 0)
        {
            throw new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_invalid_structured_output",
                $"OpenAI không trả về trường {field} hợp lệ.");
        }

        return values.Select(value => value?.Trim() ?? string.Empty).ToArray();
    }

    private static IReadOnlyList<string> RequiredList(IReadOnlyList<string>? values, string field)
    {
        var result = values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (result.Length == 0)
        {
            throw new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_invalid_structured_output",
                $"OpenAI không trả về trường {field} hợp lệ.");
        }

        return result;
    }

    private static string RequiredCharacterKey(string? value)
    {
        var key = Required(value, "character_key").ToLowerInvariant();
        if (key.Length > 80 || key.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_invalid_character_key",
                "OpenAI trả về character_key không hợp lệ.");
        }

        return key;
    }

    private static string RequiredAssetKey(string? value)
    {
        var key = Required(value, "asset_key").ToLowerInvariant();
        if (key.Length > 80 || key.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_invalid_asset_key",
                "OpenAI trả về asset_key không hợp lệ.");
        }
        return key;
    }

    private static string RequiredAssetType(string? value)
    {
        var assetType = Required(value, "asset_type");
        if (string.Equals(assetType, ProjectAssetTypes.Background, StringComparison.OrdinalIgnoreCase)) return ProjectAssetTypes.Background;
        if (string.Equals(assetType, ProjectAssetTypes.Prop, StringComparison.OrdinalIgnoreCase)) return ProjectAssetTypes.Prop;
        if (string.Equals(assetType, ProjectAssetTypes.Item, StringComparison.OrdinalIgnoreCase)) return ProjectAssetTypes.Item;
        throw new ProviderHttpException(
            ProviderCodes.OpenAi,
            "openai_invalid_asset_type",
            "OpenAI trả về asset_type không hợp lệ.");
    }

    private static ProviderHttpException InvalidResponse() =>
        new(
            ProviderCodes.OpenAi,
            "openai_missing_output",
            "OpenAI không trả về nội dung có thể sử dụng.");

    internal static void ApplyAuthentication(HttpRequestMessage request, ProviderRuntimeConfiguration provider)
    {
        if (provider.AuthenticationType == "Bearer")
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        }
        else if (provider.AuthenticationType == "Key")
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Key", provider.ApiKey);
        }
        else
        {
            request.Headers.TryAddWithoutValidation(provider.HeaderName ?? "X-API-Key", provider.ApiKey);
        }
    }

    private sealed class OpenAiPlanDto
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("hook")]
        public string? Hook { get; init; }

        [JsonPropertyName("angle")]
        public string? Angle { get; init; }

        [JsonPropertyName("audience")]
        public string? Audience { get; init; }

        [JsonPropertyName("call_to_action")]
        public string? CallToAction { get; init; }

        [JsonPropertyName("script_full_text")]
        public string? ScriptFullText { get; init; }

        [JsonPropertyName("visual_style")]
        public string? VisualStyle { get; init; }

        [JsonPropertyName("negative_prompt")]
        public string? NegativePrompt { get; init; }

        [JsonPropertyName("characters")]
        public List<OpenAiCharacterDto> Characters { get; init; } = [];

        [JsonPropertyName("assets")]
        public List<OpenAiAssetDto> Assets { get; init; } = [];

        [JsonPropertyName("scenes")]
        public List<OpenAiSceneDto> Scenes { get; init; } = [];
    }

    private sealed class OpenAiAssetDto
    {
        [JsonPropertyName("asset_key")]
        public string? AssetKey { get; init; }

        [JsonPropertyName("asset_type")]
        public string? AssetType { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("canonical_description")]
        public string? CanonicalDescription { get; init; }
    }

    private sealed class OpenAiCharacterDto
    {
        [JsonPropertyName("character_key")]
        public string? CharacterKey { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("role")]
        public string? Role { get; init; }

        [JsonPropertyName("gender")]
        public string? Gender { get; init; }

        [JsonPropertyName("age")]
        public int? Age { get; init; }

        [JsonPropertyName("face")]
        public string? Face { get; init; }

        [JsonPropertyName("hair")]
        public string? Hair { get; init; }

        [JsonPropertyName("skin")]
        public string? Skin { get; init; }

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("clothing")]
        public string? Clothing { get; init; }

        [JsonPropertyName("accessories")]
        public string? Accessories { get; init; }

        [JsonPropertyName("visual_identity")]
        public string? VisualIdentity { get; init; }

        [JsonPropertyName("immutable_traits")]
        public List<string> ImmutableTraits { get; init; } = [];

        [JsonPropertyName("forbidden_changes")]
        public List<string> ForbiddenChanges { get; init; } = [];
    }

    private sealed class OpenAiSceneDto
    {
        [JsonPropertyName("story_purpose")]
        public string? StoryPurpose { get; init; }

        [JsonPropertyName("visual_prompt")]
        public string? VisualPrompt { get; init; }

        [JsonPropertyName("character_keys")]
        public List<string> CharacterKeys { get; init; } = [];

        [JsonPropertyName("asset_keys")]
        public List<string> AssetKeys { get; init; } = [];

        [JsonPropertyName("speech_mode")]
        public string? SpeechMode { get; init; }

        [JsonPropertyName("spoken_text")]
        public string? SpokenText { get; init; }

        [JsonPropertyName("speaker_character_key")]
        public string? SpeakerCharacterKey { get; init; }

        [JsonPropertyName("voice_style")]
        public string? VoiceStyle { get; init; }

        [JsonPropertyName("ambient_audio")]
        public string? AmbientAudio { get; init; }

        [JsonPropertyName("sound_effects")]
        public string? SoundEffects { get; init; }
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TOOL_SERVER.Authentication;
using TOOL_SHARED.Contracts.Generation;

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
        CancellationToken cancellationToken)
    {
        if (targetDurationSeconds > 360)
        {
            throw new ArgumentException("Luồng tạo nội dung tự động hiện hỗ trợ video tối đa 360 giây.");
        }

        var sceneCount = Math.Max(
            1,
            (int)Math.Ceiling(targetDurationSeconds / (decimal)videoCapabilities.MaximumDurationSeconds));
        var durations = AllocateDurations(
            targetDurationSeconds,
            sceneCount,
            videoCapabilities.MinimumDurationSeconds,
            videoCapabilities.MaximumDurationSeconds);
        var speechContracts = CreateSpeechContracts(durations);
        var schema = CreateSchema(sceneCount);
        var requestBody = new
        {
            model = provider.ModelCode,
            safety_identifier = safetyIdentifier,
            instructions =
                "You are a senior short-form video strategist and screenwriter. Return only data matching the supplied JSON schema. " +
                "spoken_text is the exact literal utterance that the video provider will say once; it is never a scene summary or a copy of script_full_text. " +
                "Write spoken_text in the requested language and keep visual_prompt strictly focused on visible content. " +
                "Choose OnCameraDialogue only when the scene has one visible presenter; choose NativeVoiceOver for an off-screen narrator; choose None only when no one speaks. " +
                "Every scene must have at most one speaker. The mandatory per-scene speech contracts in the input are authoritative. " +
                "Count spoken_text as whitespace-separated words; punctuation does not reduce the count. Before returning JSON, verify every scene against its own duration and maximum word count. " +
                "When an idea does not fit, distribute it across adjacent scenes, condense it, or communicate the remainder visually. Never exceed a scene's word limit and never place overflow text in spoken_text. " +
                "Describe voice style, ambient audio and sound effects separately so the selected video provider can generate synchronized native audio. " +
                "Do not mention copyrighted characters, real public figures, logos, watermarks, or camera metadata in spoken text. " +
                "Create at most one recurring on-screen presenter. Give that presenter one stable lowercase character_key and a detailed immutable visual identity. " +
                "Reuse the exact same character_key in every scene where the presenter appears; use an empty character_keys array for B-roll or scenes without that presenter.",
            input = $"Create a complete video content plan. Topic: {topic}\nLanguage: {languageCode}\nPlatform: {platform}\nAspect ratio: {aspectRatio}\n" +
                    $"Exact total duration: {targetDurationSeconds} seconds. Return exactly {sceneCount} scenes in chronological order. " +
                     $"Scene durations in order are exactly: {string.Join(", ", durations.Select((seconds, index) => $"scene {index + 1} = {seconds}s"))}. " +
                     $"Mandatory spoken_text contracts (follow each line exactly):\n{speechContracts}\n" +
                     "The visual_prompt for each scene must describe subject, environment, lighting, action, camera framing, and motion without duplicating spoken_text. " +
                     "When a recurring presenter is useful, keep the same face, hair, body proportions, clothing and accessories between scenes.",
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

        ValidatePlan(planDto, sceneCount, durations);
        var characters = planDto.Characters
            .Select(character => new GeneratedCharacterProfile(
                RequiredCharacterKey(character.CharacterKey),
                Required(character.Name, "characters.name"),
                Required(character.Role, "characters.role"),
                Required(character.Gender, "characters.gender"),
                character.Age,
                Required(character.Face, "characters.face"),
                Required(character.Hair, "characters.hair"),
                Required(character.Skin, "characters.skin"),
                Required(character.Body, "characters.body"),
                Required(character.Clothing, "characters.clothing"),
                Required(character.Accessories, "characters.accessories"),
                Required(character.VisualIdentity, "characters.visual_identity"),
                RequiredList(character.ImmutableTraits, "characters.immutable_traits"),
                RequiredList(character.ForbiddenChanges, "characters.forbidden_changes")))
            .ToArray();
        var scenes = planDto.Scenes
            .Select((scene, index) => new GeneratedContentScene(
                index + 1,
                Required(scene.StoryPurpose, "story_purpose"),
                NormalizeSpokenText(scene.SpokenText, scene.SpeechMode),
                Required(scene.VisualPrompt, "visual_prompt"),
                durations[index],
                scene.CharacterKeys
                    .Select(RequiredCharacterKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Required(scene.SpeechMode, "speech_mode"),
                NullIfWhiteSpace(scene.SpeakerCharacterKey),
                Required(scene.VoiceStyle, "voice_style"),
                Required(scene.AmbientAudio, "ambient_audio"),
                Required(scene.SoundEffects, "sound_effects")))
            .ToArray();
        var usage = root.TryGetProperty("usage", out var usageElement) ? usageElement : default;

        return new OpenAiContentResult(
            new GeneratedContentPlan(
                Required(planDto.Title, "title"),
                Required(planDto.Hook, "hook"),
                Required(planDto.Angle, "angle"),
                Required(planDto.Audience, "audience"),
                Required(planDto.CallToAction, "call_to_action"),
                Required(planDto.ScriptFullText, "script_full_text"),
                Required(planDto.VisualStyle, "visual_style"),
                Required(planDto.NegativePrompt, "negative_prompt"),
                characters,
                scenes),
            GetInt64(usage, "input_tokens"),
            GetInt64(usage, "output_tokens"),
            root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty);
    }

    private static JsonObject CreateSchema(int sceneCount) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("title", "hook", "angle", "audience", "call_to_action", "script_full_text", "visual_style", "negative_prompt", "characters", "scenes"),
        ["properties"] = new JsonObject
        {
            ["title"] = StringSchema(),
            ["hook"] = StringSchema(),
            ["angle"] = StringSchema(),
            ["audience"] = StringSchema(),
            ["call_to_action"] = StringSchema(),
            ["script_full_text"] = StringSchema(),
            ["visual_style"] = StringSchema(),
            ["negative_prompt"] = StringSchema(),
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
                        ["character_key"] = StringSchema(),
                        ["name"] = StringSchema(),
                        ["role"] = StringSchema(),
                        ["gender"] = StringSchema(),
                        ["age"] = new JsonObject { ["type"] = new JsonArray("integer", "null"), ["minimum"] = 1, ["maximum"] = 120 },
                        ["face"] = StringSchema(),
                        ["hair"] = StringSchema(),
                        ["skin"] = StringSchema(),
                        ["body"] = StringSchema(),
                        ["clothing"] = StringSchema(),
                        ["accessories"] = StringSchema(),
                        ["visual_identity"] = StringSchema(),
                        ["immutable_traits"] = StringArraySchema(1, 12),
                        ["forbidden_changes"] = StringArraySchema(1, 12)
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
                        "speech_mode", "spoken_text", "speaker_character_key", "voice_style", "ambient_audio", "sound_effects"),
                    ["properties"] = new JsonObject
                    {
                        ["sequence_number"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 },
                        ["story_purpose"] = StringSchema(),
                        ["visual_prompt"] = StringSchema(),
                        ["character_keys"] = StringArraySchema(0, 1),
                        ["speech_mode"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray(
                                KlingSpeechModes.None,
                                KlingSpeechModes.OnCameraDialogue,
                                KlingSpeechModes.NativeVoiceOver)
                        },
                        ["spoken_text"] = StringSchema(),
                        ["speaker_character_key"] = NullableStringSchema(),
                        ["voice_style"] = StringSchema(),
                        ["ambient_audio"] = StringSchema(),
                        ["sound_effects"] = StringSchema()
                    }
                }
            }
        }
    };

    private static JsonObject StringSchema() => new() { ["type"] = "string" };

    private static JsonObject NullableStringSchema() =>
        new() { ["type"] = new JsonArray("string", "null") };

    private static JsonObject StringArraySchema(int minimum, int maximum) => new()
    {
        ["type"] = "array",
        ["minItems"] = minimum,
        ["maxItems"] = maximum,
        ["items"] = StringSchema()
    };

    private static int[] AllocateDurations(
        int totalSeconds,
        int sceneCount,
        int minimumDurationSeconds,
        int maximumDurationSeconds)
    {
        var result = Enumerable.Repeat(totalSeconds / sceneCount, sceneCount).ToArray();
        for (var index = 0; index < totalSeconds % sceneCount; index++)
        {
            result[index]++;
        }

        if (result.Any(x => x < minimumDurationSeconds || x > maximumDurationSeconds))
        {
            throw new AccountApiException(
                StatusCodes.Status422UnprocessableEntity,
                "video_duration_not_supported",
                $"Không thể chia thời lượng dự án thành các clip từ {minimumDurationSeconds} đến {maximumDurationSeconds} giây cho model đã chọn.");
        }

        return result;
    }

    private static void ValidatePlan(OpenAiPlanDto plan, int sceneCount, IReadOnlyList<int> durations)
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

        for (var index = 0; index < plan.Scenes.Count; index++)
        {
            ValidateSpeechIntent(plan.Scenes[index], index + 1, durations[index]);
        }
    }

    private static void ValidateSpeechIntent(OpenAiSceneDto scene, int sceneNumber, int durationSeconds)
    {
        var mode = Required(scene.SpeechMode, "speech_mode");
        var spokenText = scene.SpokenText?.Trim() ?? string.Empty;
        var speaker = NullIfWhiteSpace(scene.SpeakerCharacterKey);
        switch (mode)
        {
            case KlingSpeechModes.None when spokenText.Length == 0 && speaker is null:
                break;
            case KlingSpeechModes.OnCameraDialogue
                when spokenText.Length > 0 &&
                     speaker is not null &&
                     scene.CharacterKeys.Count == 1 &&
                     string.Equals(speaker, scene.CharacterKeys[0], StringComparison.OrdinalIgnoreCase):
                break;
            case KlingSpeechModes.NativeVoiceOver when spokenText.Length > 0 && speaker is null:
                break;
            default:
                throw new ProviderHttpException(
                    ProviderCodes.OpenAi,
                    "openai_invalid_speech_intent",
                    "OpenAI trả về người nói, kiểu lời hoặc nội dung lời không hợp lệ.");
        }

        if (spokenText.Length > 0)
        {
            var wordCount = NativeSpeechWordBudget.CountWords(spokenText);
            var maximumWords = NativeSpeechWordBudget.MaximumWordsForDurationSeconds(durationSeconds);
            if (wordCount > maximumWords)
            {
                throw new ProviderHttpException(
                    ProviderCodes.OpenAi,
                    "openai_spoken_text_too_long",
                    $"Nội dung AI cho cảnh {sceneNumber} có {wordCount} từ, vượt mức {maximumWords} từ cho clip {durationSeconds} giây. Hãy sinh lại content để phân bổ lời ngắn hơn theo từng cảnh.",
                    errors: new Dictionary<string, string[]>
                    {
                        ["sceneNumber"] = [sceneNumber.ToString()],
                        ["durationSeconds"] = [durationSeconds.ToString()],
                        ["wordCount"] = [wordCount.ToString()],
                        ["maximumWords"] = [maximumWords.ToString()]
                    });
            }
        }
    }

    private static string CreateSpeechContracts(IReadOnlyList<int> durations) =>
        string.Join(
            "\n",
            durations.Select((durationSeconds, index) =>
            {
                var maximumWords = NativeSpeechWordBudget.MaximumWordsForDurationSeconds(durationSeconds);
                return $"- scene {index + 1}: exactly {durationSeconds}s; speech_mode=None requires empty spoken_text; otherwise spoken_text must contain 1 to {maximumWords} whitespace-separated words.";
            }));

    private static string NormalizeSpokenText(string? value, string? mode) =>
        string.Equals(mode, KlingSpeechModes.None, StringComparison.Ordinal)
            ? string.Empty
            : Required(value, "spoken_text");

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

        [JsonPropertyName("scenes")]
        public List<OpenAiSceneDto> Scenes { get; init; } = [];
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

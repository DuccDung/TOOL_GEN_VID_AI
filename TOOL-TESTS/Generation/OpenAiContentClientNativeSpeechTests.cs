using System.Net;
using System.Text;
using System.Text.Json;
using TOOL_SERVER.Generation;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Generation;

public sealed class OpenAiContentClientNativeSpeechTests
{
    [Fact]
    public async Task GenerateAsync_RequestsStructuredNativeSpeechAndMapsSpeakerIntent()
    {
        var handler = new CaptureHandler(CreateResponse("Minh bắt đầu thói quen nhỏ hôm nay."));
        var client = new OpenAiContentClient(new StubHttpClientFactory(handler));

        var result = await client.GenerateAsync(
            CreateProvider(),
            "thói quen lành mạnh",
            "vi-VN",
            "YouTube",
            "16:9",
            5,
            "safe-user",
            CancellationToken.None);

        var scene = Assert.Single(result.Plan.Scenes);
        Assert.Equal(KlingSpeechModes.OnCameraDialogue, scene.SpeechMode);
        Assert.Equal("minh", scene.SpeakerCharacterKey);
        Assert.Equal("Minh bắt đầu thói quen nhỏ hôm nay.", scene.Narration);
        using var request = JsonDocument.Parse(Assert.IsType<string>(handler.RequestBody));
        var root = request.RootElement;
        Assert.Contains("spoken_text", root.GetProperty("instructions").GetString(), StringComparison.Ordinal);
        Assert.Contains("cảnh 1 = 5 giây", root.GetProperty("input").GetString(), StringComparison.Ordinal);
        Assert.Contains("cảnh 1: đúng 5 giây", root.GetProperty("input").GetString(), StringComparison.Ordinal);
        Assert.Contains("spoken_text tiếng Việt tự nhiên", root.GetProperty("input").GetString(), StringComparison.Ordinal);
        Assert.Contains("85–95% thời lượng nội dung", root.GetProperty("input").GetString(), StringComparison.Ordinal);
        Assert.Contains("11–14 âm tiết/cụm đọc", root.GetProperty("input").GetString(), StringComparison.Ordinal);
        Assert.Contains("Một nhân vật cùng spoken_text phải dùng OnCameraDialogue", root.GetProperty("input").GetString(), StringComparison.Ordinal);
        Assert.Contains("khuôn mặt và miệng", root.GetProperty("instructions").GetString(), StringComparison.Ordinal);
        var schema = root.GetProperty("text").GetProperty("format").GetProperty("schema");
        var sceneProperties = schema.GetProperty("properties")
            .GetProperty("scenes")
            .GetProperty("items")
            .GetProperty("properties");
        Assert.True(sceneProperties.TryGetProperty("speech_mode", out _));
        Assert.True(sceneProperties.TryGetProperty("spoken_text", out _));
        Assert.True(sceneProperties.TryGetProperty("speaker_character_key", out _));
        Assert.True(sceneProperties.TryGetProperty("asset_keys", out _));
        Assert.Equal("bright-room", Assert.Single(result.Plan.Assets!).AssetKey);
        Assert.Equal([1], Assert.Single(result.Plan.Assets!).SceneSequenceNumbers);
    }

    [Fact]
    public async Task GenerateAsync_AcceptsSpeechBeyondLegacyDurationWordBudget()
    {
        var handler = new CaptureHandler(CreateResponse("one two three four five six seven eight nine"));
        var client = new OpenAiContentClient(new StubHttpClientFactory(handler));

        var result = await client.GenerateAsync(
            CreateProvider(),
            "healthy habits",
            "en-US",
            "YouTube",
            "16:9",
            5,
            "safe-user",
            CancellationToken.None);

        Assert.Equal("one two three four five six seven eight nine", Assert.Single(result.Plan.Scenes).Narration);
    }

    [Fact]
    public async Task GenerateAsync_EnglishPolicyListsEveryHumanReadableContentGroup()
    {
        var handler = new CaptureHandler(CreateResponse("Start with one small action."));
        var client = new OpenAiContentClient(new StubHttpClientFactory(handler));

        await client.GenerateAsync(
            CreateProvider(),
            "một thói quen lành mạnh",
            "en-US",
            "YouTube",
            "16:9",
            5,
            "safe-user",
            CancellationToken.None);

        using var request = JsonDocument.Parse(Assert.IsType<string>(handler.RequestBody));
        var instructions = request.RootElement.GetProperty("instructions").GetString();
        Assert.Contains("every human-readable text field in English", instructions, StringComparison.Ordinal);
        Assert.Contains("script_full_text", instructions, StringComparison.Ordinal);
        Assert.Contains("canonical_description", instructions, StringComparison.Ordinal);
        Assert.Contains("immutable_traits", instructions, StringComparison.Ordinal);
        Assert.Contains("ambient_audio", instructions, StringComparison.Ordinal);
        Assert.Contains("All spoken_text must be natural English", instructions, StringComparison.Ordinal);
        Assert.Contains("Topic: một thói quen lành mạnh", request.RootElement.GetProperty("input").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_VietnamesePolicyListsEveryHumanReadableContentGroup()
    {
        var handler = new CaptureHandler(CreateResponse("Hãy bắt đầu bằng một hành động nhỏ."));
        var client = new OpenAiContentClient(new StubHttpClientFactory(handler));

        await client.GenerateAsync(
            CreateProvider(),
            "một thói quen lành mạnh",
            "vi-VN",
            "YouTube",
            "16:9",
            5,
            "safe-user",
            CancellationToken.None);

        using var request = JsonDocument.Parse(Assert.IsType<string>(handler.RequestBody));
        var instructions = request.RootElement.GetProperty("instructions").GetString();
        Assert.Contains("mọi giá trị người đọc được phải viết bằng tiếng Việt tự nhiên", instructions, StringComparison.Ordinal);
        Assert.Contains("script_full_text", instructions, StringComparison.Ordinal);
        Assert.Contains("canonical_description", instructions, StringComparison.Ordinal);
        Assert.Contains("immutable_traits", instructions, StringComparison.Ordinal);
        Assert.Contains("ambient_audio", instructions, StringComparison.Ordinal);
        Assert.Contains("spoken_text là câu được provider đọc đúng một lần", instructions, StringComparison.Ordinal);
        Assert.Contains("tự kiểm tra mọi trường bắt buộc", instructions, StringComparison.Ordinal);
        var schema = request.RootElement.GetProperty("text").GetProperty("format").GetProperty("schema");
        var spokenTextDescription = schema.GetProperty("properties")
            .GetProperty("scenes")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("spoken_text")
            .GetProperty("description")
            .GetString();
        var assetNameDescription = schema.GetProperty("properties")
            .GetProperty("assets")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("name")
            .GetProperty("description")
            .GetString();
        Assert.Contains("tiếng Việt tự nhiên, đúng dấu", spokenTextDescription, StringComparison.Ordinal);
        Assert.Contains("tên riêng, thương hiệu hoặc model", assetNameDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_VietnamesePreservesEmptyHumanTextAndNormalizesAudioSentinels()
    {
        var handler = new CaptureHandler(CreateResponse(
            string.Empty,
            title: string.Empty,
            visualPrompt: string.Empty,
            voiceStyle: "None",
            ambientAudio: "N/A",
            soundEffects: "no sound effects"));
        var client = new OpenAiContentClient(new StubHttpClientFactory(handler));

        var result = await client.GenerateAsync(
            CreateProvider(),
            "thói quen lành mạnh",
            "vi-VN",
            "YouTube",
            "16:9",
            5,
            "safe-user",
            CancellationToken.None);

        Assert.Empty(result.Plan.Title);
        var scene = Assert.Single(result.Plan.Scenes);
        Assert.Empty(scene.Narration);
        Assert.Empty(scene.VisualPrompt);
        Assert.Equal("Không áp dụng", scene.VoiceStyle);
        Assert.Equal("Không có", scene.AmbientAudio);
        Assert.Equal("Không có hiệu ứng âm thanh", scene.SoundEffects);
        Assert.Equal(120, result.InputTokens);
        Assert.Equal(240, result.OutputTokens);
    }

    [Fact]
    public async Task RepairAsync_SendsRejectedPlanAndSafeViolationsWithImmutableInstructions()
    {
        var handler = new CaptureHandler(CreateResponse("Hãy bắt đầu bằng một hành động nhỏ."));
        var client = new OpenAiContentClient(new StubHttpClientFactory(handler));
        var rejectedPlan = new GeneratedContentPlan(
            "A Better Habit",
            "Hãy bắt đầu",
            "Góc nhìn thực tế",
            "Người trưởng thành",
            "Hãy thử ngay",
            "Hãy bắt đầu bằng một hành động nhỏ.",
            "Ánh sáng tự nhiên",
            "Không chữ trên màn hình",
            [],
            [],
            []);

        await client.RepairWithVideoConstraintsAsync(
            CreateProvider(),
            rejectedPlan,
            [
                new ContentLanguageViolation("title", "language_invalid"),
                new ContentLanguageViolation(
                    "scenes[0].spoken_text",
                    ContentPlanViolationReasons.SpeechTooShort,
                    1.2m,
                    4.25m,
                    4.75m)
            ],
            "vi-VN",
            "YouTube",
            "16:9",
            5,
            "safe-user",
            VideoModelCapabilities.KlingDefault,
            true,
            1m,
            CancellationToken.None);

        using var request = JsonDocument.Parse(handler.RequestBody!);
        var instructions = request.RootElement.GetProperty("instructions").GetString();
        var input = request.RootElement.GetProperty("input").GetString();
        Assert.Contains("Chỉ sửa các trường được liệt kê", instructions, StringComparison.Ordinal);
        Assert.Contains("chưa khớp nhịp lời", instructions, StringComparison.Ordinal);
        Assert.Contains("Tuyệt đối giữ nguyên character_key, asset_key", instructions, StringComparison.Ordinal);
        Assert.Contains("- title: language_invalid", input, StringComparison.Ordinal);
        Assert.Contains("scenes[0].spoken_text: lời quá ngắn; ước tính 1.2 giây, mục tiêu 4.25–4.75 giây", input, StringComparison.Ordinal);
        Assert.Contains("A Better Habit", input, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_SendsEachSceneItsOwnDurationContractWithoutWordLimit()
    {
        var handler = new CaptureHandler(CreateResponse("Keep each idea short."));
        var client = new OpenAiContentClient(new StubHttpClientFactory(handler));

        await Assert.ThrowsAsync<ProviderHttpException>(() => client.GenerateAsync(
            CreateProvider(),
            "healthy habits",
            "en-US",
            "YouTube",
            "16:9",
            20,
            "safe-user",
            CancellationToken.None));

        using var request = JsonDocument.Parse(Assert.IsType<string>(handler.RequestBody));
        var input = request.RootElement.GetProperty("input").GetString();
        Assert.Contains("scene 1: exactly 10s", input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scene 2: exactly 10s", input, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, input!.Split("spoken_text must be non-empty and natural for the scene duration", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("maximum word", input, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("whitespace-separated words", input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_RejectsVoiceOverThatStillHasPresenter()
    {
        var handler = new CaptureHandler(CreateResponse(
            "One clear sentence.",
            KlingSpeechModes.NativeVoiceOver,
            ["minh"],
            null,
            "A presenter stands in a bright room."));
        var client = new OpenAiContentClient(new StubHttpClientFactory(handler));

        var exception = await Assert.ThrowsAsync<ProviderHttpException>(() => client.GenerateAsync(
            CreateProvider(), "healthy habits", "en-US", "YouTube", "16:9", 5, "safe-user", CancellationToken.None));

        Assert.Equal("openai_invalid_speech_intent", exception.Code);
    }

    [Fact]
    public async Task GenerateAsync_AcceptsVoiceOverForCharacterFreeBroll()
    {
        var handler = new CaptureHandler(CreateResponse(
            "One clear sentence.",
            KlingSpeechModes.NativeVoiceOver,
            [],
            null,
            "A calm sunrise fills an empty room."));
        var client = new OpenAiContentClient(new StubHttpClientFactory(handler));

        var result = await client.GenerateAsync(
            CreateProvider(), "healthy habits", "en-US", "YouTube", "16:9", 5, "safe-user", CancellationToken.None);

        var scene = Assert.Single(result.Plan.Scenes);
        Assert.Equal(KlingSpeechModes.NativeVoiceOver, scene.SpeechMode);
        Assert.Empty(scene.CharacterKeys);
    }

    [Fact]
    public async Task GenerateWithVideoConstraintsAsync_WhenKlingLongFormPolicyIsOff_KeepsProviderNeutralBehavior()
    {
        var handler = new CaptureHandler(CreateResponse(
            "One clear sentence.",
            KlingSpeechModes.NativeVoiceOver,
            ["minh"],
            null,
            "A presenter stands in a bright room."));
        var client = new OpenAiContentClient(new StubHttpClientFactory(handler));

        var result = await client.GenerateWithVideoConstraintsAsync(
            CreateProvider(),
            "healthy habits",
            "en-US",
            "YouTube",
            "16:9",
            5,
            "safe-user",
            VideoModelCapabilities.KlingDefault,
            false,
            1m,
            CancellationToken.None);

        var scene = Assert.Single(result.Plan.Scenes);
        Assert.Equal(KlingSpeechModes.NativeVoiceOver, scene.SpeechMode);
        Assert.Single(scene.CharacterKeys);
        using var request = JsonDocument.Parse(Assert.IsType<string>(handler.RequestBody));
        Assert.DoesNotContain(
            "one character plus spoken_text requires OnCameraDialogue",
            request.RootElement.GetProperty("input").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateResponse(
        string spokenText,
        string speechMode = KlingSpeechModes.OnCameraDialogue,
        string[]? characterKeys = null,
        string? speakerCharacterKey = "minh",
        string visualPrompt = "Minh speaks directly to the camera with his face and mouth clearly visible, gesturing naturally while speaking.",
        string title = "Small habits",
        string voiceStyle = "warm and confident",
        string ambientAudio = "quiet room tone",
        string soundEffects = "subtle hand movement")
    {
        var output = JsonSerializer.Serialize(new
        {
            title,
            hook = "Start today",
            angle = "Practical",
            audience = "Adults",
            call_to_action = "Try one habit",
            script_full_text = spokenText,
            visual_style = "Natural daylight",
            negative_prompt = "subtitles, watermark",
            characters = new[]
            {
                new
                {
                    character_key = "minh",
                    name = "Minh",
                    role = "Presenter",
                    gender = "male",
                    age = 25,
                    face = "oval face",
                    hair = "short black hair",
                    skin = "warm skin",
                    body = "average build",
                    clothing = "blue shirt",
                    accessories = "brown watch",
                    visual_identity = "Young Vietnamese presenter with an oval face",
                    immutable_traits = new[] { "oval face", "short black hair" },
                    forbidden_changes = new[] { "different face", "different shirt" }
                }
            },
            assets = new[]
            {
                new
                {
                    asset_key = "bright-room",
                    asset_type = "Background",
                    name = "Bright presentation room",
                    canonical_description = "A clean warm room with one large window on the left and soft natural daylight."
                }
            },
            scenes = new[]
            {
                new
                {
                    sequence_number = 1,
                    story_purpose = "Opening hook",
                    visual_prompt = visualPrompt,
                    character_keys = characterKeys ?? new[] { "minh" },
                    asset_keys = new[] { "bright-room" },
                    speech_mode = speechMode,
                    spoken_text = spokenText,
                    speaker_character_key = speakerCharacterKey,
                    voice_style = voiceStyle,
                    ambient_audio = ambientAudio,
                    sound_effects = soundEffects
                }
            }
        });
        return JsonSerializer.Serialize(new
        {
            id = "resp-native-audio",
            output = new[]
            {
                new
                {
                    content = new[] { new { type = "output_text", text = output } }
                }
            },
            usage = new { input_tokens = 120, output_tokens = 240 }
        });
    }

    private static ProviderRuntimeConfiguration CreateProvider() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ProviderCodes.OpenAi,
            "gpt-5.6-luna",
            new Uri("https://api.openai.com/v1/"),
            "Bearer",
            null,
            "test-key-never-sent-to-a-real-provider");

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CaptureHandler(string responseJson) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}

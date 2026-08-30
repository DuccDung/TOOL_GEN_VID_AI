using System.Net;
using System.Text;
using System.Text.Json;
using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class CharacterConsistencyGatewayTests
{
    [Theory]
    [InlineData("Generated", "openai", true)]
    [InlineData("Generated", "byteplus", false)]
    [InlineData("Uploaded", "openai", false)]
    [InlineData("Uploaded", null, false)]
    public void BytePlusReferencePolicy_AllowsOnlySystemGeneratedApprovedAssets(
        string sourceType,
        string? sourceProviderCode,
        bool allowed)
    {
        var reference = new GenerationService.ReferenceSnapshot(
            Guid.NewGuid(),
            "image/png",
            new string('a', 64),
            1024,
            sourceType,
            sourceProviderCode);

        Assert.Equal(allowed, GenerationService.IsBytePlusReferenceAllowed(reference));
    }

    [Fact]
    public void ComposeKlingPrompt_LocksApprovedIdentityWardrobeAndForbiddenChanges()
    {
        var character = new GenerationService.CharacterPromptSnapshot(
            Guid.NewGuid(),
            1,
            "Lan",
            "người dẫn chương trình",
            "Vietnamese woman, oval face, warm skin, short black hair",
            "{\"immutableTraits\":[\"oval face\",\"short black hair\"]}",
            "{\"clothing\":\"teal shirt\",\"accessories\":\"silver watch\"}",
            "[\"different face\",\"different clothes\"]",
            "Approved",
            new GenerationService.ReferenceSnapshot(Guid.NewGuid(), "image/png", new string('a', 64), 1024));

        var prompt = GenerationService.ComposeKlingPrompt(
            "Lan walks into a bright living room.",
            "blurry, watermark",
            character);

        Assert.Contains("IDENTITY LOCK", prompt);
        Assert.Contains("Vietnamese woman, oval face", prompt);
        Assert.Contains("teal shirt, silver watch", prompt);
        Assert.Contains("Immutable traits: oval face, short black hair", prompt);
        Assert.Contains("Never change: different face, different clothes", prompt);
        Assert.Contains("Match the approved reference image throughout the clip", prompt);
        Assert.Contains("NEGATIVE CONSTRAINTS: blurry, watermark", prompt);
        Assert.True(prompt.Length <= 3072);
    }

    [Theory]
    [InlineData("kling-3.0", "/image-to-video/kling-3.0", "first_frame")]
    [InlineData("kling-3.0-omni", "/omni-video/kling-3.0-omni", "refer_image")]
    public async Task SubmitAsync_WithApprovedReference_UsesExpectedKlingReferenceMode(
        string modelCode,
        string expectedPath,
        string expectedReferenceType)
    {
        var handler = new CaptureHandler();
        var client = new KlingVideoClient(new StubHttpClientFactory(handler));
        var reference = new KlingReferenceImageData(
            Guid.NewGuid(),
            "image/png",
            Convert.ToBase64String([1, 2, 3, 4]),
            new string('b', 64));

        var result = await client.SubmitAsync(
            CreateProvider(modelCode),
            "same approved presenter",
            "16:9",
            5,
            "720p",
            true,
            "scene-001",
            reference,
            CancellationToken.None);

        Assert.Equal("task-123", result.ExternalRequestId);
        Assert.Equal(expectedPath, handler.RequestUri?.AbsolutePath);
        using var body = JsonDocument.Parse(Assert.IsType<string>(handler.RequestBody));
        Assert.Equal("720p", body.RootElement.GetProperty("settings").GetProperty("resolution").GetString());
        Assert.Equal("native", body.RootElement.GetProperty("settings").GetProperty("audio").GetString());
        Assert.False(body.RootElement.GetProperty("settings").GetProperty("multi_shot").GetBoolean());
        var content = body.RootElement.GetProperty("contents")[0];
        Assert.Equal(expectedReferenceType, content.GetProperty("type").GetString());
        Assert.Equal("data:image/png;base64,AQIDBA==", content.GetProperty("url").GetString());
    }

    [Fact]
    public async Task SubmitAsync_WithoutReference_UsesTextToVideoAndOmitsImageContents()
    {
        var handler = new CaptureHandler();
        var client = new KlingVideoClient(new StubHttpClientFactory(handler));

        await client.SubmitAsync(
            CreateProvider("kling-3.0"),
            "landscape without recurring character",
            "16:9",
            5,
            "720p",
            true,
            "scene-002",
            null,
            CancellationToken.None);

        Assert.Equal("/text-to-video/kling-3.0", handler.RequestUri?.AbsolutePath);
        using var body = JsonDocument.Parse(Assert.IsType<string>(handler.RequestBody));
        Assert.False(body.RootElement.TryGetProperty("contents", out _));
        Assert.Equal("native", body.RootElement.GetProperty("settings").GetProperty("audio").GetString());
    }

    private static ProviderRuntimeConfiguration CreateProvider(string modelCode) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ProviderCodes.Kling,
            modelCode,
            new Uri("https://api-singapore.klingai.com/"),
            "Bearer",
            null,
            "test-key-never-sent-to-a-real-provider");

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"code\":0,\"data\":{\"id\":\"task-123\",\"status\":\"submitted\"}}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}

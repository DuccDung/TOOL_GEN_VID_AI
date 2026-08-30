using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class OpenAiSpeechClientTests
{
    [Fact]
    public async Task GenerateAsync_SendsFixedModelAndReturnsValidatedWav()
    {
        var wav = CreatePcmWav(sampleRate: 16_000, channels: 1, durationSeconds: 1);
        var handler = new StubHandler(_ =>
        {
            var content = new ByteArrayContent(wav);
            content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            response.Headers.TryAddWithoutValidation("x-request-id", "speech-request-1");
            return response;
        });
        var client = CreateClient(handler);

        var result = await client.GenerateAsync(
            CreateProvider(),
            "Xin chào các bạn.",
            "shimmer",
            "Speak natural Vietnamese.",
            1.05m,
            CancellationToken.None);

        Assert.Equal("audio/wav", result.Voice.MimeType);
        Assert.Equal(16_000, result.Voice.SampleRate);
        Assert.Equal((byte)1, result.Voice.Channels);
        Assert.Equal(1_000, result.Voice.DurationMs);
        Assert.Equal("speech-request-1", result.ProviderRequestId);
        Assert.Equal("/v1/audio/speech", handler.RequestUri?.AbsolutePath);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("gpt-4o-mini-tts", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("Xin chào các bạn.", body.RootElement.GetProperty("input").GetString());
        Assert.Equal("shimmer", body.RootElement.GetProperty("voice").GetString());
        Assert.Equal("wav", body.RootElement.GetProperty("response_format").GetString());
        Assert.Equal(1.05m, body.RootElement.GetProperty("speed").GetDecimal());
    }

    [Fact]
    public async Task GenerateAsync_RejectsUnexpectedMimeBeforeAcceptingPayload()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CreatePcmWav(16_000, 1, 1))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("text/plain") }
            }
        });

        var exception = await Assert.ThrowsAsync<ProviderHttpException>(() =>
            CreateClient(handler).GenerateAsync(
                CreateProvider(), "Lời đọc", "shimmer", "Speak clearly.", 1m, CancellationToken.None));

        Assert.Equal("voice_audio_invalid", exception.Code);
    }

    [Fact]
    public void WaveValidator_RejectsMalformedAndOversizedPayload()
    {
        var malformed = Assert.Throws<ProviderHttpException>(() =>
            WaveAudioValidator.Validate(Encoding.ASCII.GetBytes("not-a-wave"), 1024));
        var oversized = Assert.Throws<ProviderHttpException>(() =>
            WaveAudioValidator.Validate(new byte[1025], 1024));

        Assert.Equal("voice_audio_invalid", malformed.Code);
        Assert.Equal("voice_audio_invalid", oversized.Code);
    }

    [Fact]
    public async Task GenerateAsync_NormalizesRateLimitWithoutEchoingNarration()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(
                "{\"error\":{\"code\":\"rate_limit\",\"message\":\"Sensitive narration\"}}",
                Encoding.UTF8,
                "application/json")
        });

        var exception = await Assert.ThrowsAsync<ProviderHttpException>(() =>
            CreateClient(handler).GenerateAsync(
                CreateProvider(), "Sensitive narration", "shimmer", "Speak clearly.", 1m, CancellationToken.None));

        Assert.Equal("openai_voice_rate_limited", exception.Code);
        Assert.DoesNotContain("Sensitive narration", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(400, "openai_voice_request_rejected")]
    [InlineData(401, "openai_voice_permission_denied")]
    [InlineData(403, "openai_voice_permission_denied")]
    [InlineData(500, "openai_voice_generation_failed")]
    public async Task GenerateAsync_NormalizesProviderStatusCodes(int statusCode, string expectedCode)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new StringContent("{\"error\":{\"message\":\"provider detail\"}}", Encoding.UTF8, "application/json")
        });

        var exception = await Assert.ThrowsAsync<ProviderHttpException>(() =>
            CreateClient(handler).GenerateAsync(
                CreateProvider(), "Lời đọc", "shimmer", "Speak clearly.", 1m, CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
    }

    private static OpenAiSpeechClient CreateClient(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), Options.Create(new OpenAiSpeechOptions()));

    private static ProviderRuntimeConfiguration CreateProvider() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ProviderCodes.OpenAi,
            "gpt-4o-mini-tts",
            new Uri("https://api.openai.com/v1/"),
            "Bearer",
            null,
            "test-key-never-sent-to-a-real-provider");

    private static byte[] CreatePcmWav(int sampleRate, short channels, int durationSeconds)
    {
        const short bitsPerSample = 16;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var dataLength = byteRate * durationSeconds;
        var bytes = new byte[44 + dataLength];
        "RIFF"u8.CopyTo(bytes.AsSpan(0, 4));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), bytes.Length - 8);
        "WAVE"u8.CopyTo(bytes.AsSpan(8, 4));
        "fmt "u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22, 2), channels);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32, 2), checked((short)(channels * bitsPerSample / 8)));
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34, 2), bitsPerSample);
        "data"u8.CopyTo(bytes.AsSpan(36, 4));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40, 4), dataLength);
        return bytes;
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? RequestBody { get; private set; }

        public string? AuthorizationScheme { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}

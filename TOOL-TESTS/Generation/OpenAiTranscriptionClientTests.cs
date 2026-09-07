using System.Buffers.Binary;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class OpenAiTranscriptionClientTests
{
    [Fact]
    public async Task TranscribeAsync_SendsWavAndReturnsWordTimings()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"text\":\"Xin chào Việt Nam\",\"words\":[{\"word\":\"Xin\",\"start\":0.10,\"end\":0.30},{\"word\":\"chào\",\"start\":0.31,\"end\":0.60}]}",
                    Encoding.UTF8,
                    "application/json")
            };
            response.Headers.TryAddWithoutValidation("x-request-id", "transcription-request-1");
            return response;
        });
        var client = new OpenAiTranscriptionClient(
            new StubHttpClientFactory(handler),
            Options.Create(new OpenAiTranscriptionOptions()));

        var result = await client.TranscribeAsync(
            CreateProvider(),
            CreatePcmWav(16_000, 1, 1),
            "vi-VN",
            CancellationToken.None);

        Assert.Equal("Xin chào Việt Nam", result.Transcript);
        Assert.Equal("transcription-request-1", result.ProviderRequestId);
        Assert.Equal(2, result.Words.Count);
        Assert.Equal(100, result.Words[0].StartMs);
        Assert.Equal(600, result.Words[1].EndMs);
        Assert.Equal("/v1/audio/transcriptions", handler.RequestUri?.AbsolutePath);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Contains("name=model", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("whisper-1", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("name=language", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("vi", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranscribeAsync_NormalizesProviderFailureWithoutEchoingBody()
    {
        const string sensitive = "sensitive transcript from provider";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(
                $"{{\"error\":{{\"message\":\"{sensitive}\"}}}}",
                Encoding.UTF8,
                "application/json")
        });
        var client = new OpenAiTranscriptionClient(
            new StubHttpClientFactory(handler),
            Options.Create(new OpenAiTranscriptionOptions()));

        var exception = await Assert.ThrowsAsync<ProviderHttpException>(() => client.TranscribeAsync(
            CreateProvider(),
            CreatePcmWav(16_000, 1, 1),
            "vi-VN",
            CancellationToken.None));

        Assert.Equal("openai_transcription_rate_limited", exception.Code);
        Assert.DoesNotContain(sensitive, exception.Message, StringComparison.Ordinal);
    }

    private static ProviderRuntimeConfiguration CreateProvider() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ProviderCodes.OpenAi,
            "whisper-1",
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

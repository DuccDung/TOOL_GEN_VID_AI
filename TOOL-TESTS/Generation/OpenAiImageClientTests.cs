using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class OpenAiImageClientTests
{
    [Fact]
    public async Task GenerateAsync_SendsFixedMvpVariantAndValidatesPng()
    {
        var png = CreatePngHeader(1024, 1024);
        var handler = new StubHandler(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new
            {
                data = new[] { new { b64_json = Convert.ToBase64String(png) } },
                usage = new { input_tokens = 123, output_tokens = 456 }
            }));
        var client = CreateClient(handler);

        var result = await client.GenerateAsync(CreateProvider(), "canonical character", CancellationToken.None);

        Assert.Equal("image/png", result.Image.MimeType);
        Assert.Equal(1024, result.Image.Width);
        Assert.Equal(1024, result.Image.Height);
        Assert.Equal(123, result.InputTokens);
        Assert.Equal(456, result.OutputTokens);
        Assert.Equal("request-image-1", result.ProviderRequestId);
        Assert.Equal("/v1/images/generations", handler.RequestUri?.AbsolutePath);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("gpt-image-2", request.RootElement.GetProperty("model").GetString());
        Assert.Equal(1, request.RootElement.GetProperty("n").GetInt32());
        Assert.Equal("1024x1024", request.RootElement.GetProperty("size").GetString());
        Assert.Equal("medium", request.RootElement.GetProperty("quality").GetString());
        Assert.Equal("png", request.RootElement.GetProperty("output_format").GetString());
    }

    [Fact]
    public async Task GenerateAsync_RejectsInvalidBase64()
    {
        var client = CreateClient(new StubHandler(
            HttpStatusCode.OK,
            "{\"data\":[{\"b64_json\":\"not-base64!\"}]}"));

        var exception = await Assert.ThrowsAsync<ProviderHttpException>(
            () => client.GenerateAsync(CreateProvider(), "canonical character", CancellationToken.None));

        Assert.Equal("openai_image_base64_invalid", exception.Code);
    }

    [Fact]
    public void Validator_RejectsWrongDimensionsAndUnexpectedJpeg()
    {
        var dimensions = Assert.Throws<ProviderHttpException>(
            () => GeneratedImageValidator.ValidatePng(CreatePngHeader(512, 512), 10 * 1024 * 1024));
        var jpeg = Assert.Throws<ProviderHttpException>(
            () => GeneratedImageValidator.ValidatePng(CreateJpegHeader(1024, 1024), 10 * 1024 * 1024));

        Assert.Equal("openai_image_dimensions_invalid", dimensions.Code);
        Assert.Equal("openai_image_unexpected_format", jpeg.Code);
    }

    [Fact]
    public void Validator_RejectsPayloadOverTenMegabytes()
    {
        var bytes = new byte[(10 * 1024 * 1024) + 1];

        var exception = Assert.Throws<ProviderHttpException>(
            () => GeneratedImageValidator.ValidatePng(bytes, 10 * 1024 * 1024));

        Assert.Equal("openai_image_size_invalid", exception.Code);
    }

    [Theory]
    [InlineData(429, "{\"error\":{\"code\":\"rate_limit\",\"message\":\"slow down\"}}", "openai_image_rate_limited")]
    [InlineData(403, "{\"error\":{\"code\":\"organization_verification_required\",\"message\":\"verify organization\"}}", "openai_organization_verification_required")]
    [InlineData(400, "{\"error\":{\"code\":\"moderation_blocked\",\"message\":\"safety policy\"}}", "openai_image_moderation_blocked")]
    public async Task GenerateAsync_NormalizesExpectedProviderErrors(
        int statusCode,
        string body,
        string expectedCode)
    {
        var client = CreateClient(new StubHandler((HttpStatusCode)statusCode, body));

        var exception = await Assert.ThrowsAsync<ProviderHttpException>(
            () => client.GenerateAsync(CreateProvider(), "canonical character", CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
        Assert.DoesNotContain("canonical character", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_DoesNotForwardUnknownProviderMessageThatMayRepeatPrompt()
    {
        var client = CreateClient(new StubHandler(
            HttpStatusCode.InternalServerError,
            "{\"error\":{\"code\":\"server_error\",\"message\":\"failed for Sensitive Character Prompt and b64_json\"}}"));

        var exception = await Assert.ThrowsAsync<ProviderHttpException>(
            () => client.GenerateAsync(CreateProvider(), "Sensitive Character Prompt", CancellationToken.None));

        Assert.Equal("openai_server_error", exception.Code);
        Assert.DoesNotContain("Sensitive Character Prompt", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("b64_json", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static OpenAiImageClient CreateClient(HttpMessageHandler handler) =>
        new(
            new StubHttpClientFactory(handler),
            Options.Create(new OpenAiImageOptions()));

    private static ProviderRuntimeConfiguration CreateProvider() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ProviderCodes.OpenAi,
            "gpt-image-2",
            new Uri("https://api.openai.com/v1/"),
            "Bearer",
            null,
            "test-key-never-sent-to-a-real-provider");

    private static byte[] CreatePngHeader(int width, int height)
    {
        var bytes = new byte[24];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        return bytes;
    }

    private static byte[] CreateJpegHeader(int width, int height)
    {
        var bytes = new byte[13];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        bytes[3] = 0xC0;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4, 2), 9);
        bytes[6] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(7, 2), checked((ushort)height));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(9, 2), checked((ushort)width));
        bytes[11] = 1;
        bytes[12] = 0;
        return bytes;
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string responseJson) : HttpMessageHandler
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
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
            response.Headers.TryAddWithoutValidation("x-request-id", "request-image-1");
            return response;
        }
    }
}

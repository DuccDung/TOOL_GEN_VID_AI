using System.Net;
using System.Text;
using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class BytePlusVideoClientTests
{
    [Fact]
    public async Task SubmitAsync_UsesModelArkAsyncEndpointAndNativeAudioVariant()
    {
        var handler = new StubHandler("""{ "id": "seedance-task-1" }""");
        var client = new BytePlusVideoClient(new StubHttpClientFactory(handler));

        var result = await client.SubmitAsync(
            CreateProvider(),
            "safe structured prompt",
            "16:9",
            10,
            "720p",
            true,
            "vf-request",
            null,
            CancellationToken.None);

        Assert.Equal("Submitted", result.Status);
        Assert.Equal("seedance-task-1", result.ExternalRequestId);
        Assert.Equal("/api/v3/contents/generations/tasks", handler.RequestUri?.AbsolutePath);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Contains("\"model\":\"dreamina-seedance-2-5-260628\"", handler.RequestBody);
        Assert.Contains("\"generate_audio\":true", handler.RequestBody);
        Assert.Contains("\"resolution\":\"720p\"", handler.RequestBody);
        Assert.DoesNotContain("test-key", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStatusAsync_MapsCompletedOutputAndProviderUsageWithoutLeakingPayload()
    {
        var handler = new StubHandler(
            """
            {
              "id": "seedance-task-2",
              "status": "succeeded",
              "duration": 12,
              "content": { "video_url": "https://cdn.example.com/seedance.mp4" },
              "usage": { "completion_tokens": 345600 },
              "private_prompt": "must not leave the server"
            }
            """);
        var client = new BytePlusVideoClient(new StubHttpClientFactory(handler));

        var result = await client.GetStatusAsync(CreateProvider(), "seedance-task-2", CancellationToken.None);

        Assert.Equal("Completed", result.Status);
        Assert.Equal(100m, result.ProgressPercent);
        Assert.Equal(345600, result.CompletionTokens);
        Assert.Equal(12, result.ActualDurationSeconds);
        Assert.Equal("https://cdn.example.com/seedance.mp4", result.OutputUrl);
        Assert.DoesNotContain("cdn.example.com", result.ResponseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("video_url", result.ResponseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private_prompt", result.ResponseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("must not leave", result.ResponseJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/api/v3/contents/generations/tasks/seedance-task-2", handler.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task GetStatusAsync_NormalizesCredentialFailureWithoutReturningProviderBody()
    {
        var handler = new StubHandler(
            """{ "error": { "code": "invalid_api_key", "message": "secret upstream diagnostic" } }""",
            HttpStatusCode.Unauthorized);
        var client = new BytePlusVideoClient(new StubHttpClientFactory(handler));

        var exception = await Assert.ThrowsAsync<ProviderHttpException>(
            () => client.GetStatusAsync(CreateProvider(), "seedance-task-error", CancellationToken.None));

        Assert.Equal("provider_credential_invalid", exception.Code);
        Assert.DoesNotContain("secret upstream diagnostic", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ProviderRuntimeConfiguration CreateProvider() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ProviderCodes.BytePlus,
            "dreamina-seedance-2-5-260628",
            new Uri("https://ark.ap-southeast.bytepluses.com/api/v3/"),
            "Bearer",
            null,
            "test-key-never-sent-in-a-body");

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(
        string responseJson,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
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
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}

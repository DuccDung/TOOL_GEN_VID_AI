using System.Net;
using System.Text;
using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class FalVeoVideoClientTests
{
    [Fact]
    public async Task SubmitAsync_LocksVeoPayloadPrivacyHeadersAndKeyAuthentication()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK, """{ "request_id": "fal-request-1", "status_url": "https://must-not-persist.example" }"""));
        var client = new FalVeoVideoClient(new StubHttpClientFactory(handler));

        var result = await client.SubmitAsync(
            CreateProvider(FalVeoPolicy.StandardEndpointId),
            "prompt tiếng Việt đã duyệt",
            "16:9",
            8,
            "720p",
            true,
            "vf-request",
            CreateReference(),
            CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/fal-ai/veo3.1/image-to-video", request.Uri.AbsolutePath);
        Assert.Equal("Key", request.AuthorizationScheme);
        Assert.Equal("test-key", request.AuthorizationParameter);
        Assert.Equal("1", request.Headers["X-Fal-No-Retry"]);
        Assert.Equal("0", request.Headers["X-Fal-Store-IO"]);
        Assert.Contains("expiration_duration_seconds", request.Headers["X-Fal-Object-Lifecycle-Preference"]);
        Assert.Contains("\"duration\":\"8s\"", request.Body);
        Assert.Contains("\"generate_audio\":true", request.Body);
        Assert.Contains("\"auto_fix\":false", request.Body);
        Assert.Contains("\"resolution\":\"720p\"", request.Body);
        Assert.DoesNotContain("test-key", request.Body, StringComparison.Ordinal);
        Assert.Equal("Submitted", result.Status);
        Assert.DoesNotContain("status_url", result.ResponseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("must-not-persist", result.ResponseJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStatusAsync_CompletesThroughStatusAndResultWithoutPersistingOutputUrl()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK, """{ "status": "COMPLETED" }"""),
            (HttpStatusCode.OK, """{ "video": { "url": "https://cdn.fal.media/private-output.mp4" }, "duration": "6s", "private_prompt": "secret" }"""));
        var client = new FalVeoVideoClient(new StubHttpClientFactory(handler));

        var result = await client.GetStatusAsync(
            CreateProvider(FalVeoPolicy.FastEndpointId),
            "fal-request-2",
            CancellationToken.None);

        Assert.Equal("Completed", result.Status);
        Assert.Equal(6, result.ActualDurationSeconds);
        Assert.Equal("https://cdn.fal.media/private-output.mp4", result.OutputUrl);
        Assert.DoesNotContain("fal.media", result.ResponseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private_prompt", result.ResponseJson, StringComparison.OrdinalIgnoreCase);
        Assert.Collection(
            handler.Requests,
            request => Assert.EndsWith("/requests/fal-request-2/status", request.Uri.AbsolutePath, StringComparison.Ordinal),
            request => Assert.EndsWith("/requests/fal-request-2", request.Uri.AbsolutePath, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("IN_QUEUE", "Queued")]
    [InlineData("IN_PROGRESS", "Processing")]
    public async Task GetStatusAsync_MapsActiveQueueStatuses(string upstream, string expected)
    {
        var handler = new SequenceHandler((HttpStatusCode.OK, $$"""{ "status": "{{upstream}}" }"""));
        var client = new FalVeoVideoClient(new StubHttpClientFactory(handler));

        var result = await client.GetStatusAsync(CreateProvider(FalVeoPolicy.StandardEndpointId), "request-active", CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetStatusAsync_NormalizesCredentialFailureWithoutReturningProviderDiagnostic()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.Unauthorized, """{ "error": { "message": "secret upstream credential diagnostic" } }"""));
        var client = new FalVeoVideoClient(new StubHttpClientFactory(handler));

        var exception = await Assert.ThrowsAsync<ProviderHttpException>(() =>
            client.GetStatusAsync(CreateProvider(FalVeoPolicy.StandardEndpointId), "request-error", CancellationToken.None));

        Assert.Equal("provider_credential_invalid", exception.Code);
        Assert.DoesNotContain("secret upstream", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitAsync_DoesNotFallbackFromStandardToFast()
    {
        var handler = new SequenceHandler((HttpStatusCode.ServiceUnavailable, """{ "message": "temporary" }"""));
        var client = new FalVeoVideoClient(new StubHttpClientFactory(handler));

        await Assert.ThrowsAsync<ProviderHttpException>(() => client.SubmitAsync(
            CreateProvider(FalVeoPolicy.StandardEndpointId),
            "prompt",
            "16:9",
            4,
            "720p",
            true,
            "vf-request",
            CreateReference(),
            CancellationToken.None));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/fal-ai/veo3.1/image-to-video", request.Uri.AbsolutePath);
    }

    [Fact]
    public async Task SubmitAsync_RejectsUnapprovedEndpointBeforeOutbound()
    {
        var handler = new SequenceHandler((HttpStatusCode.OK, """{ "request_id": "must-not-run" }"""));
        var client = new FalVeoVideoClient(new StubHttpClientFactory(handler));

        var exception = await Assert.ThrowsAsync<ProviderHttpException>(() => client.SubmitAsync(
            CreateProvider("fal-ai/unapproved-model"),
            "prompt",
            "16:9",
            4,
            "720p",
            true,
            "vf-request",
            CreateReference(),
            CancellationToken.None));

        Assert.Equal("fal_endpoint_not_allowed", exception.Code);
        Assert.Empty(handler.Requests);
    }

    private static ProviderRuntimeConfiguration CreateProvider(string endpointId) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ProviderCodes.Fal,
            endpointId,
            new Uri("https://queue.fal.run/"),
            "Key",
            null,
            "test-key");

    private static VideoProviderReferenceImage CreateReference() =>
        new(Guid.NewGuid(), "image/png", Convert.ToBase64String([1, 2, 3]), new string('a', 64));

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed record CapturedRequest(
        Uri Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        IReadOnlyDictionary<string, string> Headers,
        string Body);

    private sealed class SequenceHandler(params (HttpStatusCode Status, string Json)[] responses) : HttpMessageHandler
    {
        private int responseIndex;
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(
                pair => pair.Key,
                pair => string.Join(",", pair.Value),
                StringComparer.OrdinalIgnoreCase);
            Requests.Add(new CapturedRequest(
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                headers,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            var response = responses[Math.Min(responseIndex++, responses.Length - 1)];
            return new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(response.Json, Encoding.UTF8, "application/json")
            };
        }
    }
}

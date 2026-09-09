using System.Net;
using System.Text;
using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class FalLipSyncClientTests
{
    [Fact]
    public async Task SubmitAsync_UsesLockedEndpointPayloadAndDoesNotPersistInputUrls()
    {
        var handler = new SequenceHandler((HttpStatusCode.OK, """{ "request_id": "lip-request-1" }"""));
        var client = new FalLipSyncClient(new StubHttpClientFactory(handler));

        var result = await client.SubmitAsync(
            CreateProvider(FalLipSyncPolicy.EndpointId),
            "https://inputs.example.com/video?token=signed-video",
            "https://inputs.example.com/audio?token=signed-audio",
            CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/fal-ai/sync-lipsync/v2", request.Uri.AbsolutePath);
        Assert.Equal("Key", request.AuthorizationScheme);
        Assert.Equal("test-key", request.AuthorizationParameter);
        Assert.Equal("1", request.Headers["X-Fal-No-Retry"]);
        Assert.Equal("0", request.Headers["X-Fal-Store-IO"]);
        Assert.Contains("\"model\":\"lipsync-2\"", request.Body);
        Assert.Contains("\"sync_mode\":\"cut_off\"", request.Body);
        Assert.Contains("signed-video", request.Body);
        Assert.Contains("signed-audio", request.Body);
        Assert.DoesNotContain("test-key", request.Body, StringComparison.Ordinal);
        Assert.Equal("Submitted", result.Status);
        Assert.DoesNotContain("signed-video", result.ResponseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("signed-audio", result.ResponseJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStatusAsync_CompletesWithoutPersistingProviderOutputUrl()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK, """{ "status": "COMPLETED" }"""),
            (HttpStatusCode.OK, """{ "video": { "url": "https://cdn.fal.media/lip.mp4", "duration": 8.2 }, "private": "secret" }"""));
        var client = new FalLipSyncClient(new StubHttpClientFactory(handler));

        var result = await client.GetStatusAsync(CreateProvider(FalLipSyncPolicy.EndpointId), "lip-request-2", CancellationToken.None);

        Assert.Equal("Completed", result.Status);
        Assert.Equal(9, result.ActualDurationSeconds);
        Assert.Equal("https://cdn.fal.media/lip.mp4", result.OutputUrl);
        Assert.DoesNotContain("fal.media", result.ResponseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", result.ResponseJson, StringComparison.OrdinalIgnoreCase);
        Assert.Collection(
            handler.Requests,
            request => Assert.Equal("/fal-ai/sync-lipsync/v2/requests/lip-request-2/status", request.Uri.AbsolutePath),
            request => Assert.Equal("/fal-ai/sync-lipsync/v2/requests/lip-request-2", request.Uri.AbsolutePath));
    }

    [Fact]
    public async Task GetStatusAsync_MapsTerminalProviderErrorWithoutRequestingResult()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK, """{ "status": "FAILED", "error": { "message": "generation failed" } }"""));
        var client = new FalLipSyncClient(new StubHttpClientFactory(handler));

        var result = await client.GetStatusAsync(CreateProvider(FalLipSyncPolicy.EndpointId), "lip-request-failed", CancellationToken.None);

        Assert.Equal("Failed", result.Status);
        Assert.Equal("provider_generation_failed", result.ErrorCode);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("generation failed", result.ResponseJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitAsync_RejectsUnapprovedEndpointBeforeOutbound()
    {
        var handler = new SequenceHandler((HttpStatusCode.OK, """{ "request_id": "must-not-run" }"""));
        var client = new FalLipSyncClient(new StubHttpClientFactory(handler));

        var exception = await Assert.ThrowsAsync<ProviderHttpException>(() => client.SubmitAsync(
            CreateProvider("fal-ai/unapproved-lipsync"),
            "https://inputs.example.com/video",
            "https://inputs.example.com/audio",
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

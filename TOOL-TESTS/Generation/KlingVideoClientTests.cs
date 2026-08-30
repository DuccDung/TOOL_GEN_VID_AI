using System.Net;
using System.Text;
using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class KlingVideoClientTests
{
    [Fact]
    public async Task GetStatusAsync_ParsesStringBillingWithoutBlockingCompletedResult()
    {
        var handler = new StubHandler(
            """
            {
              "code": 0,
              "data": {
                "result": [{
                  "id": "task-123",
                  "status": "succeeded",
                  "billing": { "amount": "1.260000" },
                  "outputs": [{ "type": "video", "url": "https://cdn.example.com/video.mp4" }]
                }]
              }
            }
            """);
        var client = new KlingVideoClient(new StubHttpClientFactory(handler));

        var result = await client.GetStatusAsync(CreateProvider(), "task-123", CancellationToken.None);

        Assert.Equal("Completed", result.Status);
        Assert.Equal(100m, result.ProgressPercent);
        Assert.Equal(1.26m, result.ReportedBillingAmount);
        Assert.Equal("https://cdn.example.com/video.mp4", result.OutputUrl);
        Assert.DoesNotContain("outputs", result.ResponseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"data\"", result.ResponseJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "https://cdn.example.com/video.mp4",
            GenerationService.ExtractOutputUrl(result.ResponseJson));
        Assert.Equal("/tasks", handler.RequestUri?.AbsolutePath);
        Assert.Equal("?task_ids=task-123", handler.RequestUri?.Query);
    }

    [Fact]
    public async Task GetStatusAsync_SumsNumericAndStringBillingItems()
    {
        var handler = new StubHandler(
            """
            {
              "code": 0,
              "data": [{
                "id": "task-456",
                "status": "processing",
                "billing": [
                  { "amount": 0.25 },
                  { "cost": "0.50" },
                  { "total": "2.5e-1" }
                ]
              }]
            }
            """);
        var client = new KlingVideoClient(new StubHttpClientFactory(handler));

        var result = await client.GetStatusAsync(CreateProvider(), "task-456", CancellationToken.None);

        Assert.Equal("Processing", result.Status);
        Assert.Equal(1.00m, result.ReportedBillingAmount);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("\"not-a-number\"")]
    [InlineData("{\"value\":1}")]
    public async Task GetStatusAsync_IgnoresUnsupportedBillingValues(string billingValue)
    {
        var handler = new StubHandler(
            $$"""
            {
              "code": 0,
              "data": {
                "id": "task-789",
                "status": "processing",
                "billing": { "amount": {{billingValue}} }
              }
            }
            """);
        var client = new KlingVideoClient(new StubHttpClientFactory(handler));

        var result = await client.GetStatusAsync(CreateProvider(), "task-789", CancellationToken.None);

        Assert.Equal("Processing", result.Status);
        Assert.Null(result.ReportedBillingAmount);
    }

    [Fact]
    public async Task GetStatusAsync_WithoutBillingStillReturnsProviderStatus()
    {
        var handler = new StubHandler(
            """
            { "code": 0, "data": { "id": "task-no-billing", "status": "processing" } }
            """);
        var client = new KlingVideoClient(new StubHttpClientFactory(handler));

        var result = await client.GetStatusAsync(CreateProvider(), "task-no-billing", CancellationToken.None);

        Assert.Equal("Processing", result.Status);
        Assert.Null(result.ReportedBillingAmount);
    }

    [Fact]
    public async Task GetStatusAsync_KlingApiErrorRemainsAProviderError()
    {
        var handler = new StubHandler(
            """
            { "code": 1102, "message": "Account balance not enough" }
            """);
        var client = new KlingVideoClient(new StubHttpClientFactory(handler));

        var exception = await Assert.ThrowsAsync<ProviderHttpException>(
            () => client.GetStatusAsync(CreateProvider(), "task-error", CancellationToken.None));

        Assert.Equal("kling_quota_exhausted", exception.Code);
        Assert.DoesNotContain("balance", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStatusAsync_SucceededWithoutHttpsOutput_IsFailedSafely()
    {
        var handler = new StubHandler(
            """
            { "code": 0, "data": { "id": "task-no-output", "status": "succeeded", "prompt": "private prompt" } }
            """);
        var client = new KlingVideoClient(new StubHttpClientFactory(handler));

        var result = await client.GetStatusAsync(CreateProvider(), "task-no-output", CancellationToken.None);

        Assert.Equal("Failed", result.Status);
        Assert.Equal("kling_output_missing", result.ErrorCode);
        Assert.DoesNotContain("private prompt", result.ResponseJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStatusAsync_HttpModerationError_IsNormalizedWithoutProviderPayload()
    {
        var handler = new StubHandler(
            """{ "error": { "code": "unsafe_prompt", "message": "moderation rejected: secret prompt" } }""",
            HttpStatusCode.BadRequest);
        var client = new KlingVideoClient(new StubHttpClientFactory(handler));

        var exception = await Assert.ThrowsAsync<ProviderHttpException>(
            () => client.GetStatusAsync(CreateProvider(), "task-error", CancellationToken.None));

        Assert.Equal("kling_moderation_blocked", exception.Code);
        Assert.DoesNotContain("secret prompt", exception.Message, StringComparison.Ordinal);
    }

    private static ProviderRuntimeConfiguration CreateProvider() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ProviderCodes.Kling,
            "kling-3.0",
            new Uri("https://api-singapore.klingai.com/"),
            "Bearer",
            null,
            "test-key-never-sent-to-a-real-provider");

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(
        string responseJson,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }
}

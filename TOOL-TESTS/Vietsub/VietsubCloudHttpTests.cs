using System.Net;
using System.Text;
using System.Text.Json;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Vietsub.Translation;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubCloudHttpTests
{
    [Theory]
    [InlineData(429, "CLOUD_RATE_LIMITED")]
    [InlineData(500, "CLOUD_PROVIDER_FAILED")]
    [InlineData(302, "CLOUD_PROVIDER_FAILED")]
    public async Task Adapter_UsesAllowedServerEndpointAndDoesNotTreatUpstreamFailureAsFree(int status, string expected)
    {
        using var handler = new Handler((HttpStatusCode)status);
        var client = new OpenAiSubtitleTranslationClient(new Factory(handler));
        var input = VietsubCloudProviderTests.Request();
        var result = await client.TranslateAsync(Runtime("https://api.openai.com/v1/"), input, input.Cues, 6000, "owner", default);
        Assert.Equal(expected, result.ErrorCode); Assert.Null(result.InputTokens); Assert.Null(result.OutputTokens);
        Assert.Equal(1, handler.Calls); Assert.Equal("https://api.openai.com/v1/responses", handler.Url);
        Assert.Equal("Bearer", handler.Authentication); Assert.NotNull(handler.Body);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
        Assert.True(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
    }

    [Theory]
    [InlineData("https://api.openai.com.evil.test/v1/")]
    [InlineData("http://api.openai.com/v1/")]
    [InlineData("https://api.openai.com:444/v1/")]
    [InlineData("https://api.openai.com/another/")]
    public async Task Adapter_RejectsUnapprovedEndpointBeforeSending(string uri)
    {
        using var handler = new Handler(HttpStatusCode.OK); var input = VietsubCloudProviderTests.Request();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenAiSubtitleTranslationClient(new Factory(handler))
            .TranslateAsync(Runtime(uri), input, input.Cues, 6000, "owner", default));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Adapter_RejectsOversizedBodyWithoutReturningPartialOutput()
    {
        using var handler = new Handler(HttpStatusCode.OK) { Response = new string('x', 1024 * 1024 + 1) };
        var input = VietsubCloudProviderTests.Request();
        var result = await new OpenAiSubtitleTranslationClient(new Factory(handler))
            .TranslateAsync(Runtime("https://api.openai.com/v1/"), input, input.Cues, 6000, "owner", default);
        Assert.Equal("CLOUD_RESULT_TOO_LARGE", result.ErrorCode); Assert.Empty(result.Items); Assert.Null(result.InputTokens);
    }
    private static ProviderRuntimeConfiguration Runtime(string uri) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "openai", "fixture-model", new Uri(uri), "Bearer", null, "synthetic-test-key");
    private sealed class Factory(Handler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) { Assert.Equal("VietsubOpenAiRuntime", name); return new(handler, false); } }
    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls; public string? Url, Body, Authentication; public string Response = "{}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Url = request.RequestUri!.AbsoluteUri; Body = await request.Content!.ReadAsStringAsync(ct);
            Authentication = request.Headers.Authorization?.Scheme;
            return new(status) { Content = new StringContent(Response, Encoding.UTF8, "application/json") };
        }
    }
}

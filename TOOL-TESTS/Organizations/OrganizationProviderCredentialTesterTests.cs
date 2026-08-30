using System.Net;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Organizations;

namespace TOOL_TESTS.Organizations;

public sealed class OrganizationProviderCredentialTesterTests
{
    [Fact]
    public async Task TestAsync_UsesFakeProviderAndAcceptsSuccessfulCredential()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var tester = new OrganizationProviderCredentialTester(new StubHttpClientFactory(handler));

        await tester.TestAsync(
            "openai",
            "https://api.openai.com/v1/",
            "test-secret-1234",
            CancellationToken.None);

        Assert.Equal("https://api.openai.com/v1/models", handler.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("test-secret-1234", handler.AuthorizationParameter);
    }

    [Fact]
    public async Task TestAsync_RejectedCredentialReturnsStableCodeWithoutLeakingSecret()
    {
        const string secret = "should-never-appear-9876";
        var tester = new OrganizationProviderCredentialTester(
            new StubHttpClientFactory(new StubHandler(HttpStatusCode.Unauthorized)));

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => tester.TestAsync(
            "openai",
            "https://api.openai.com/v1/",
            secret,
            CancellationToken.None));

        Assert.Equal("provider_credential_test_failed", exception.Code);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Credential cũ vẫn được giữ nguyên", exception.Message);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}

using System.Net;
using System.Net.Http.Headers;
using TOOL_LOCAL.Generation;

namespace TOOL_TESTS.Generation;

public sealed class GatewayReadRetryTests
{
    [Theory]
    [InlineData("GET", 3)]
    [InlineData("POST", 1)]
    public async Task RateLimit_ReplaysOnlyReadsAndHonorsRetryAfter(string method, int calls)
    {
        var waits = new List<TimeSpan>();
        var transport = new Transport(n => n < 3 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
        using var client = new HttpClient(new GatewayReadRetryHandler
        {
            InnerHandler = transport,
            DelayAsync = (delay, ct) => { waits.Add(delay); return Task.CompletedTask; }
        });
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://localhost/api/generation/videos/test");
        request.Headers.Add("X-Test-Context", "unchanged");
        using var response = await client.SendAsync(request);
        Assert.Equal(calls, transport.Calls);
        Assert.All(transport.Contexts, context => Assert.Equal("unchanged", context));
        Assert.Equal(calls - 1, waits.Count);
        Assert.All(waits, delay => Assert.Equal(TimeSpan.FromSeconds(12), delay));
        Assert.Equal(method == "GET" ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task PersistentRateLimit_StopsAfterTwoRetries()
    {
        var transport = new Transport(_ => HttpStatusCode.TooManyRequests);
        using var client = new HttpClient(new GatewayReadRetryHandler { InnerHandler = transport, DelayAsync = (_, _) => Task.CompletedTask });
        using var response = await client.GetAsync("https://localhost/status");
        Assert.Equal(3, transport.Calls);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task CancellationDuringBackoff_DoesNotSendAnotherRequest()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new Transport(_ => HttpStatusCode.TooManyRequests);
        using var client = new HttpClient(new GatewayReadRetryHandler
        {
            InnerHandler = transport,
            DelayAsync = (_, ct) => { cancellation.Cancel(); return Task.FromCanceled(ct); }
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://localhost/status", cancellation.Token));
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task OtherErrors_AreReturnedWithoutRetry(HttpStatusCode status)
    {
        var transport = new Transport(_ => status);
        using var client = new HttpClient(new GatewayReadRetryHandler { InnerHandler = transport });
        using var response = await client.GetAsync("https://localhost/status");
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(1, transport.Calls);
    }

    private sealed class Transport(Func<int, HttpStatusCode> status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public List<string?> Contexts { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Contexts.Add(request.Headers.TryGetValues("X-Test-Context", out var values) ? values.Single() : null);
            var response = new HttpResponseMessage(status(++Calls));
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
            return Task.FromResult(response);
        }
    }
}

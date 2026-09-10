using System.Net;

namespace TOOL_LOCAL.Generation;

// Only replay bodyless reads. Paid POST requests must retain their explicit workflow/idempotency handling.
internal sealed class GatewayReadRetryHandler : DelegatingHandler
{
    internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = Task.Delay;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method != HttpMethod.Get || request.Content is not null)
            return await base.SendAsync(request, ct);

        for (var attempt = 0; ; attempt++)
        {
            using var copy = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version,
                VersionPolicy = request.VersionPolicy
            };
            foreach (var header in request.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            var response = await base.SendAsync(copy, ct);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= 2) return response;
            var retry = response.Headers.RetryAfter;
            var delay = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(5 * (attempt + 1));
            // Do not retry earlier than a server-directed delay that exceeds our bounded wait.
            if (delay > TimeSpan.FromSeconds(60)) return response;
            response.Dispose();
            await DelayAsync(delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay, ct);
        }
    }
}

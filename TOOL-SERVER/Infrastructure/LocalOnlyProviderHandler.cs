using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_SERVER.Infrastructure;

internal sealed class LocalOnlyProviderHandler(IOptions<ApplicationFeaturePolicy> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Polling existing requests and downloading their output must remain available for settlement.
        if (options.Value.VietsubLocalOnly && request.Method != HttpMethod.Get && request.Method != HttpMethod.Head
            && request.Method != HttpMethod.Delete
            && !(request.Method == HttpMethod.Post
                && request.RequestUri?.AbsolutePath.TrimEnd('/').EndsWith("/cancel", StringComparison.Ordinal) == true))
            throw new AccountApiException(403, ApplicationFeaturePolicy.LocalOnlyErrorCode, ApplicationFeaturePolicy.LocalOnlyMessage);
        return base.SendAsync(request, cancellationToken);
    }
}

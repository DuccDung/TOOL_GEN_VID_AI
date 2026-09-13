using TOOL_LOCAL.Authentication;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_LOCAL.Configuration;

internal sealed class LocalOnlyGatewayHandler(ApplicationFeaturePolicy policy) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (policy.VietsubLocalOnly && (ApplicationFeaturePolicy.IsVideoApiPath(path)
            || ApplicationFeaturePolicy.IsCloudSubmission(path, request.Method.Method)))
            throw new AccountClientException(ApplicationFeaturePolicy.LocalOnlyErrorCode,
                ApplicationFeaturePolicy.LocalOnlyMessage, 403);
        return base.SendAsync(request, cancellationToken);
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_SERVER.Infrastructure;

internal sealed class LocalOnlyFeatureFilter(IOptions<ApplicationFeaturePolicy> options) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var path = request.Path.Value ?? string.Empty;
        if (options.Value.VietsubLocalOnly
            && ((ApplicationFeaturePolicy.IsVideoApiPath(path) && !HttpMethods.IsGet(request.Method))
                || ApplicationFeaturePolicy.IsCloudSubmission(path, request.Method)))
        {
            context.Result = new ObjectResult(new ApiErrorResponse(ApplicationFeaturePolicy.LocalOnlyErrorCode,
                ApplicationFeaturePolicy.LocalOnlyMessage)) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }
        await next();
    }
}

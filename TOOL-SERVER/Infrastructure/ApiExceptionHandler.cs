using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_SERVER.Infrastructure;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        var (statusCode, code, message, errors) = exception switch
        {
            AccountApiException accountException =>
                (accountException.StatusCode, accountException.Code, accountException.Message, accountException.Errors),
            DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict, "concurrency_conflict", "Dữ liệu đã được thay đổi bởi một tiến trình khác.", null),
            ArgumentException argumentException =>
                (StatusCodes.Status400BadRequest, "invalid_request", argumentException.Message, null),
            _ =>
                (StatusCodes.Status500InternalServerError, "unexpected_error", "Server không thể xử lý yêu cầu.", null)
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled API exception. TraceId: {TraceId}", httpContext.TraceIdentifier);
        }
        else
        {
            logger.LogInformation("API request rejected with {Code}. TraceId: {TraceId}", code, httpContext.TraceIdentifier);
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new ApiErrorResponse(code, message, errors, httpContext.TraceIdentifier),
            cancellationToken);
        return true;
    }
}

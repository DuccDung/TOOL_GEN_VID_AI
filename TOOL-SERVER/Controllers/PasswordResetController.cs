using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TOOL_SERVER.Authentication;
using TOOL_SHARED.Contracts.Authentication;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_SERVER.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
public sealed class PasswordResetController(IPasswordResetService passwordResetService) : ControllerBase
{
    [HttpPost("forgot-password")]
    [EnableRateLimiting("password-reset-request")]
    [ProducesResponseType<ForgotPasswordResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await passwordResetService.RequestAsync(request, GetClientContext(), cancellationToken);
        return Accepted(new ForgotPasswordResponse(PasswordResetService.RequestAcceptedMessage));
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("password-reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await passwordResetService.ResetAsync(request, GetClientContext(), cancellationToken);
        return NoContent();
    }

    private ClientRequestContext GetClientContext() =>
        new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            HttpContext.TraceIdentifier);
}

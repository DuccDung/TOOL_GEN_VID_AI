using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Authentication;
using TOOL_SHARED.Contracts.Authentication;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthTokenResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authService.RegisterAsync(request, GetClientContext(), cancellationToken));
        }
        catch (AccountApiException exception) when (exception.StatusCode is
            StatusCodes.Status400BadRequest or StatusCodes.Status409Conflict)
        {
            return StatusCode(
                exception.StatusCode,
                new ApiErrorResponse(
                    exception.Code,
                    exception.Message,
                    exception.Errors,
                    HttpContext.TraceIdentifier));
        }
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status423Locked)]
    public async Task<ActionResult<AuthTokenResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, GetClientContext(), cancellationToken);
        if (result.Response is { } response)
        {
            return Ok(response);
        }

        var failure = result.Failure
            ?? throw new InvalidOperationException("Login result does not contain a response or failure.");
        return StatusCode(
            failure.StatusCode,
            new ApiErrorResponse(
                failure.Code,
                failure.Message,
                failure.Errors,
                HttpContext.TraceIdentifier));
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthTokenResponse>> Refresh(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authService.RefreshAsync(request, GetClientContext(), cancellationToken));
        }
        catch (AccountApiException exception) when (exception.StatusCode is
            StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
        {
            return StatusCode(
                exception.StatusCode,
                new ApiErrorResponse(
                    exception.Code,
                    exception.Message,
                    exception.Errors,
                    HttpContext.TraceIdentifier));
        }
    }

    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        var userId = GetRequiredClaim(ClaimTypes.NameIdentifier);
        var sessionId = Guid.Parse(GetRequiredClaim(AuthClaimTypes.SessionId));
        await authService.LogoutAsync(userId, sessionId, request, GetClientContext(), cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<UserProfileResponse>(StatusCodes.Status200OK)]
    public Task<UserProfileResponse> Me(CancellationToken cancellationToken) =>
        authService.GetProfileAsync(GetRequiredClaim(ClaimTypes.NameIdentifier), cancellationToken);

    private ClientRequestContext GetClientContext() =>
        new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            HttpContext.TraceIdentifier);

    private string GetRequiredClaim(string claimType) =>
        User.FindFirstValue(claimType)
        ?? throw new AccountApiException(StatusCodes.Status401Unauthorized, "invalid_access_token", "Access token không hợp lệ.");
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Authentication;
using TOOL_SHARED.Contracts.Accounts;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize]
[Route("api/license")]
public sealed class LicenseController(IAccountManagementService accountService) : ControllerBase
{
    [HttpGet("current")]
    public Task<ActionResult<CurrentLicenseResponse>> GetCurrent(CancellationToken cancellationToken) =>
        ExecuteAsync(() => accountService.GetCurrentLicenseAsync(UserId(), DeviceId(), cancellationToken));

    [HttpPost("activate-current-device")]
    public Task<ActionResult<CurrentLicenseResponse>> ActivateCurrentDevice(CancellationToken cancellationToken) =>
        ExecuteAsync(() => accountService.ActivateCurrentDeviceAsync(UserId(), DeviceId(), SessionId(), cancellationToken));

    [HttpPost("heartbeat")]
    public Task<ActionResult<CurrentLicenseResponse>> Heartbeat(CancellationToken cancellationToken) =>
        ExecuteAsync(() => accountService.VerifyHeartbeatAsync(UserId(), DeviceId(), SessionId(), cancellationToken));

    private async Task<ActionResult<CurrentLicenseResponse>> ExecuteAsync(Func<Task<CurrentLicenseResponse>> operation)
    {
        try
        {
            return Ok(await operation());
        }
        catch (AccountApiException exception) when (exception.StatusCode is 400 or 401 or 403 or 404 or 409 or 423)
        {
            // Expected license denials are handled in application code, before returning to MVC.
            return StatusCode(exception.StatusCode, new ApiErrorResponse(
                exception.Code, exception.Message, exception.Errors, HttpContext.TraceIdentifier));
        }
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private Guid DeviceId() => Guid.Parse(User.FindFirstValue(AuthClaimTypes.DeviceId)!);

    private Guid SessionId() => Guid.Parse(User.FindFirstValue(AuthClaimTypes.SessionId)!);
}

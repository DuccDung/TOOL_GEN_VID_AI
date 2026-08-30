using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Authentication;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize]
[Route("api/license")]
public sealed class LicenseController(IAccountManagementService accountService) : ControllerBase
{
    [HttpGet("current")]
    public Task<CurrentLicenseResponse> GetCurrent(CancellationToken cancellationToken) =>
        accountService.GetCurrentLicenseAsync(UserId(), DeviceId(), cancellationToken);

    [HttpPost("activate-current-device")]
    public Task<CurrentLicenseResponse> ActivateCurrentDevice(CancellationToken cancellationToken) =>
        accountService.ActivateCurrentDeviceAsync(UserId(), DeviceId(), SessionId(), cancellationToken);

    [HttpPost("heartbeat")]
    public Task<CurrentLicenseResponse> Heartbeat(CancellationToken cancellationToken) =>
        accountService.VerifyHeartbeatAsync(UserId(), DeviceId(), SessionId(), cancellationToken);

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private Guid DeviceId() => Guid.Parse(User.FindFirstValue(AuthClaimTypes.DeviceId)!);

    private Guid SessionId() => Guid.Parse(User.FindFirstValue(AuthClaimTypes.SessionId)!);
}

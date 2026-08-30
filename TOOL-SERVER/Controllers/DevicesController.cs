using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Authentication;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize]
[Route("api/devices")]
public sealed class DevicesController(IAccountManagementService accountService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<RegisteredDeviceResponse>> Get(CancellationToken cancellationToken) =>
        accountService.GetDevicesAsync(UserId(), DeviceId(), cancellationToken);

    [HttpDelete("{deviceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(Guid deviceId, CancellationToken cancellationToken)
    {
        await accountService.RevokeDeviceAsync(UserId(), deviceId, cancellationToken);
        return NoContent();
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private Guid DeviceId() => Guid.Parse(User.FindFirstValue(AuthClaimTypes.DeviceId)!);
}

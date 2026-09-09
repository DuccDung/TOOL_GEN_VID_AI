using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Providers;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/runtime-settings")]
public sealed class AdminRuntimeSettingsController(
    ILipSyncRuntimeSettingsAdminService settingsService) : ControllerBase
{
    [HttpGet("lip-sync")]
    public Task<LipSyncRuntimeSettingsResponse> GetLipSync(CancellationToken cancellationToken) =>
        settingsService.GetAsync(cancellationToken);

    [HttpPut("lip-sync")]
    public Task<LipSyncRuntimeSettingsResponse> UpdateLipSync(
        UpdateLipSyncRuntimeSettingsRequest request,
        CancellationToken cancellationToken) =>
        settingsService.UpdateAsync(request, Context(), cancellationToken);

    private AdminRequestContext Context() =>
        new(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            HttpContext.TraceIdentifier);
}

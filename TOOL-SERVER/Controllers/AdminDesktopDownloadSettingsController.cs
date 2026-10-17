using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/desktop-download-settings")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminDesktopDownloadSettingsController(DesktopDownloadSettingsService service) : ControllerBase
{
    [HttpGet]
    public Task<DesktopDownloadSettingsResponse> Get(CancellationToken token) => service.GetAsync(token);

    [HttpPut]
    [HttpPost]
    [RequestSizeLimit(16384)]
    public Task<DesktopDownloadSettingsResponse> Save(DesktopDownloadSettingsRequest request, CancellationToken token) =>
        service.SaveAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException(), request, token);
}

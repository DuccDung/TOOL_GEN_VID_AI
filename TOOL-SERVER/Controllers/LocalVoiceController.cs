using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("ai-status")]
[Route("api/generation/local-voice")]
public sealed class LocalVoiceController(IGenerationAccessService accessService) : ControllerBase
{
    // Authorization only: no pricing, reservation, provider or local media upload.
    [HttpPost("access")]
    public async Task<LocalVoiceAccessResponse> AuthorizeProject(LocalVoiceAccessRequest request, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            throw new AccountApiException(401, "missing_user_claim", "Phiên đăng nhập không hợp lệ.");
        if (!Guid.TryParse(User.FindFirstValue(AuthClaimTypes.DeviceId), out var deviceId) || deviceId == Guid.Empty)
            throw new AccountApiException(401, "missing_device_claim", "Phiên đăng nhập thiếu thiết bị hợp lệ.");
        if (request.OrganizationId == Guid.Empty || request.ProjectId == Guid.Empty)
            throw new ArgumentException("Context xử lý giọng local không hợp lệ.");
        var access = await accessService.RequireAsync(userId, deviceId, request.OrganizationId, request.ProjectId, cancellationToken);
        return new LocalVoiceAccessResponse(access.OrganizationId, access.Project!.ProjectId);
    }
}

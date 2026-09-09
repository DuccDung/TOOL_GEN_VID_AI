using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.TikTok;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize]
[Route("api/tiktok")]
public sealed class TikTokController(
    ITikTokService service,
    ITikTokAccessService accessService,
    ITikTokCredentialRuntime credentialRuntime) : ControllerBase
{
    [HttpGet("state")]
    public Task<TikTokFeatureStateResponse> GetState(CancellationToken cancellationToken) =>
        service.GetStateAsync(UserId(), cancellationToken);

    [HttpPost("oauth/start")]
    [EnableRateLimiting("tiktok-write")]
    public async Task<StartTikTokOAuthResponse> StartOAuth(
        StartTikTokOAuthRequest request,
        CancellationToken cancellationToken)
    {
        var userId = UserId();
        var deviceId = DeviceId();
        await RequireLicenseUnlessVerifyingCredentialAsync(userId, deviceId, cancellationToken);
        return await service.StartOAuthAsync(userId, deviceId, request, cancellationToken);
    }

    [HttpPost("oauth/complete")]
    [EnableRateLimiting("tiktok-write")]
    public async Task<TikTokFeatureStateResponse> CompleteOAuth(
        CompleteTikTokOAuthRequest request,
        CancellationToken cancellationToken)
    {
        var userId = UserId();
        var deviceId = DeviceId();
        await RequireLicenseUnlessVerifyingCredentialAsync(userId, deviceId, cancellationToken);
        return await service.CompleteOAuthAsync(userId, deviceId, request, cancellationToken);
    }

    [HttpDelete("connection")]
    [EnableRateLimiting("tiktok-write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        await service.DisconnectAsync(UserId(), cancellationToken);
        return NoContent();
    }

    [HttpPost("creator-info")]
    [EnableRateLimiting("tiktok-status")]
    public async Task<TikTokCreatorInfoResponse> GetCreatorInfo(CancellationToken cancellationToken)
    {
        var userId = UserId();
        await accessService.RequireActiveLicenseAsync(userId, DeviceId(), cancellationToken);
        return await service.GetCreatorInfoAsync(userId, cancellationToken);
    }

    [HttpPost("publish")]
    [EnableRateLimiting("tiktok-write")]
    public async Task<InitializeTikTokPublishResponse> InitializePublish(
        InitializeTikTokPublishRequest request,
        CancellationToken cancellationToken)
    {
        var userId = UserId();
        await accessService.RequireActiveLicenseAsync(userId, DeviceId(), cancellationToken);
        return await service.InitializePublishAsync(userId, request, cancellationToken);
    }

    [HttpGet("publish/{publishJobId:guid}")]
    [EnableRateLimiting("tiktok-status")]
    public Task<TikTokPublishStatusResponse> GetPublishStatus(
        Guid publishJobId,
        CancellationToken cancellationToken) =>
        service.GetPublishStatusAsync(UserId(), publishJobId, cancellationToken);

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private Guid DeviceId() => Guid.Parse(User.FindFirstValue(AuthClaimTypes.DeviceId)!);

    private async Task RequireLicenseUnlessVerifyingCredentialAsync(
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var hasPendingVerification = await credentialRuntime.HasPendingVerificationAsync(
            userId,
            cancellationToken);
        if (hasPendingVerification)
        {
            if (!User.IsInRole("Admin"))
            {
                throw new AccountApiException(
                    StatusCodes.Status403Forbidden,
                    "tiktok_credential_verification_admin_required",
                    "Chỉ Global Admin đã yêu cầu xác minh mới có thể kiểm tra TikTok credential.");
            }
            return;
        }

        await accessService.RequireActiveLicenseAsync(userId, deviceId, cancellationToken);
    }
}

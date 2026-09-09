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

    [HttpGet("connections")]
    [EnableRateLimiting("tiktok-status")]
    public Task<TikTokFeatureStateResponse> GetConnections(CancellationToken cancellationToken) =>
        service.GetConnectionsStateAsync(UserId(), cancellationToken);

    [HttpDelete("connections/{connectionId:guid}")]
    [EnableRateLimiting("tiktok-write")]
    public async Task<IActionResult> DisconnectAccount(Guid connectionId, CancellationToken cancellationToken)
    {
        await service.DisconnectAsync(UserId(), cancellationToken, connectionId);
        return NoContent();
    }

    [HttpPost("connections/{connectionId:guid}/creator-info")]
    [EnableRateLimiting("tiktok-status")]
    public async Task<TikTokCreatorInfoResponse> GetAccountCreatorInfo(Guid connectionId, CancellationToken cancellationToken)
    {
        var userId = UserId();
        await accessService.RequireActiveLicenseAsync(userId, DeviceId(), cancellationToken);
        return await service.GetCreatorInfoAsync(userId, cancellationToken, connectionId);
    }

    [HttpGet("publish-history")]
    [EnableRateLimiting("tiktok-status")]
    public Task<TikTokPublishHistoryResponse> GetHistory(CancellationToken cancellationToken,
        [FromQuery] Guid? connectionId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        service.GetPublishHistoryAsync(UserId(), connectionId, page, pageSize, cancellationToken);

    [HttpGet("connections/{connectionId:guid}/avatar")]
    [EnableRateLimiting("tiktok-status")]
    public async Task<IActionResult> GetAvatar(Guid connectionId,
        [FromServices] TikTokAvatarCache avatars,
        [FromServices] TOOL_SERVER.TikTok.Data.TikTokDbContext db,
        [FromServices] ITikTokTokenProtector protector, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        try
        {
            var avatar = await avatars.GetAsync(db, protector, UserId(), connectionId, cancellationToken);
            return File(avatar.Bytes, avatar.MimeType);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or
            System.Security.Cryptography.CryptographicException or OperationCanceledException)
        {
            // Upstream image addresses may contain signatures; never log or return the exception.
            return NotFound();
        }
    }

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
        service.ReadPublishStatusAsync(UserId(), publishJobId, cancellationToken);

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

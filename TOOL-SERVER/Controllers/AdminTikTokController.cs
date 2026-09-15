using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TOOL_SERVER.TikTok;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/tiktok")]
public sealed class AdminTikTokController(ITikTokAdminService service) : ControllerBase
{
    [HttpGet]
    public Task<TikTokAdminStateResponse> GetState(CancellationToken cancellationToken) =>
        service.GetStateAsync(AdminUserId(), cancellationToken);

    [HttpPost("credentials")]
    [EnableRateLimiting("tiktok-write")]
    public Task<TikTokAdminStateResponse> SaveCredential(
        SaveTikTokAdminCredentialRequest request,
        CancellationToken cancellationToken) =>
        service.SaveCredentialAsync(request, Context(), cancellationToken);

    [HttpPost("credentials/{credentialId:guid}/verification")]
    [EnableRateLimiting("tiktok-write")]
    public Task<TikTokAdminStateResponse> RequestVerification(
        Guid credentialId,
        CancellationToken cancellationToken) =>
        service.RequestVerificationAsync(credentialId, Context(), cancellationToken);

    [HttpDelete("credentials/{credentialId:guid}")]
    [EnableRateLimiting("tiktok-write")]
    public Task<TikTokAdminStateResponse> RevokePendingCredential(
        Guid credentialId,
        CancellationToken cancellationToken) =>
        service.RevokePendingCredentialAsync(credentialId, Context(), cancellationToken);

    [HttpPut("settings")]
    [EnableRateLimiting("tiktok-write")]
    public Task<TikTokAdminStateResponse> UpdateSettings(
        UpdateTikTokAdminSettingsRequest request,
        CancellationToken cancellationToken) =>
        service.UpdateSettingsAsync(request, Context(), cancellationToken);

    private string AdminUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Admin user claim is missing.");

    private TikTokAdminRequestContext Context() =>
        new(
            AdminUserId(),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            HttpContext.TraceIdentifier);
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Publishing;
using TOOL_SHARED.Contracts.Publishing;

namespace TOOL_SERVER.Controllers;

[ApiController, Authorize, Route("api/publishing"), EnableRateLimiting("ai-gateway")]
public sealed class PublishingController : ControllerBase
{
    private readonly PublishingService _service;
    private readonly PublishingSocialService _social;
    private readonly PublishingReviewService _review;
    private readonly PublishingMedia _media;
    // Resolve internal services without exposing implementation types in public contracts.
    public PublishingController(IServiceProvider services)
    { _service = services.GetRequiredService<PublishingService>(); _social = services.GetRequiredService<PublishingSocialService>(); _review = services.GetRequiredService<PublishingReviewService>(); _media = services.GetRequiredService<PublishingMedia>(); }

    [HttpGet("state"), EnableRateLimiting("ai-status")]
    public Task<PublishingState> State([FromQuery] Guid organizationId, CancellationToken ct) => _service.StateAsync(organizationId, UserId(), DeviceId(), ct);

    [HttpPost("images"), RequestSizeLimit(16 * 1024 * 1024)]
    public Task<PublishingImage> Upload(UploadPublishingImageRequest request, CancellationToken ct) => _service.UploadImageAsync(request, UserId(), DeviceId(), ct);

    [HttpPost("schedules")]
    public Task<PublishingScheduleSummary> Save(SavePublishingScheduleRequest request, CancellationToken ct) => _service.SaveAsync(request, UserId(), DeviceId(), SessionId(), ct);

    [HttpPost("schedules/change")]
    public Task<PublishingScheduleSummary> Change(ChangePublishingScheduleRequest request, CancellationToken ct) => _service.ChangeAsync(request, UserId(), DeviceId(), SessionId(), ct);

    [HttpPost("oauth/start")]
    public Task<StartPublishingOAuthResponse> Start(StartPublishingOAuthRequest request, CancellationToken ct)
    { _service.RequireEnabled(); return _social.StartAsync(request, UserId(), DeviceId(), SessionId(), ct); }

    [HttpGet("oauth/callback"), AllowAnonymous]
    public async Task<IActionResult> Callback([FromQuery] string? state, [FromQuery] string? code, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store"; Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        _service.RequireEnabled(); await _social.CompleteAsync(state ?? "", code ?? "", ct);
        return Content("Đã kết nối. Bạn có thể đóng cửa sổ này, quay lại VideoMaker và bấm Làm mới.", "text/plain; charset=utf-8");
    }

    [HttpDelete("connections/{connectionId:guid}")]
    public async Task<IActionResult> Disconnect(Guid connectionId, CancellationToken ct)
    { _service.RequireEnabled(); await _social.DisconnectAsync(connectionId, UserId(), ct); return NoContent(); }

    [HttpPost("runs/action")]
    public async Task<IActionResult> Action(PublishingRunActionRequest request, CancellationToken ct)
    { await _review.ActionAsync(request, UserId(), DeviceId(), SessionId(), ct); return NoContent(); }

    [HttpPost("runs/review")]
    public async Task<IActionResult> Review(ApprovePublishingRunRequest request, CancellationToken ct)
    { await _review.ApproveAsync(request, UserId(), DeviceId(), SessionId(), ct); return NoContent(); }

    [HttpGet("runs/{runId:guid}/content"), EnableRateLimiting("ai-status")]
    public async Task<IActionResult> Content(Guid runId, [FromQuery] Guid organizationId, CancellationToken ct)
    {
        var run = await _review.OwnedAsync(organizationId, runId, UserId(), DeviceId(), ct);
        if (run.MediaSha256 is null) return NotFound();
        var stream = await _media.OpenVerifiedAsync(run, ct);
        Response.Headers.CacheControl = "private, no-store"; Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ETag = $"\"{run.MediaSha256}\"";
        return File(stream, "video/mp4", enableRangeProcessing: true);
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    private Guid DeviceId() => Guid.Parse(User.FindFirstValue(AuthClaimTypes.DeviceId)!);
    private Guid SessionId() => Guid.Parse(User.FindFirstValue(AuthClaimTypes.SessionId)!);
}

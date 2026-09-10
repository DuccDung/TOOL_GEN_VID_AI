using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Generation;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_SERVER.Controllers;

[ApiController, Authorize, EnableRateLimiting("ai-gateway")]
[Route("api/generation/short-video")]
public sealed class ShortVideoOutfitController(IShortVideoOutfitService service, IGeneratedImageContentService content) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new AccountApiException(401, "missing_user_claim", "Phiên không hợp lệ.");
    private Guid DeviceId => Guid.TryParse(User.FindFirstValue(AuthClaimTypes.DeviceId), out var id) ? id : throw new AccountApiException(401, "missing_device_claim", "Thiết bị không hợp lệ.");

    [HttpGet("state")]
    [EnableRateLimiting("ai-status")]
    public Task<ShortVideoState> Get(Guid projectId, Guid organizationId, CancellationToken ct) => service.GetAsync(projectId, organizationId, UserId, DeviceId, ct);
    [HttpPost("settings")]
    public Task<ShortVideoState> Save(ShortVideoSettingsRequest request, CancellationToken ct) => service.SaveAsync(request, UserId, DeviceId, ct);
    [HttpPost("migrate-veo")]
    public Task<ShortVideoVeoMigrationResponse> Migrate(ShortVideoVeoMigrationRequest request, CancellationToken ct) => service.MigrateToVeoAsync(request, UserId, DeviceId, ct);
    [HttpPost("quote")]
    public Task<ShortVideoQuote> Quote(ShortVideoQuoteRequest request, CancellationToken ct) => service.QuoteAsync(request, UserId, DeviceId, ct);
    [HttpPost("quote-text-video")]
    public Task<ShortVideoQuote> QuoteText(ShortVideoQuoteRequest request, CancellationToken ct) => service.QuoteTextVideoAsync(request, UserId, DeviceId, ct);
    [HttpPost("compose"), RequestSizeLimit(30 * 1024 * 1024)]
    public Task<ShortVideoComposition> Compose(ShortVideoComposeRequest request, CancellationToken ct) => service.ComposeAsync(request, UserId, DeviceId, ct);
    [HttpPost("approval")]
    public Task<ShortVideoState> Approve(ShortVideoApprovalRequest request, CancellationToken ct) => service.ApproveAsync(request, UserId, DeviceId, ct);
    [HttpGet("images/{id:guid}")]
    [EnableRateLimiting("ai-status")]
    public async Task<IActionResult> Image(Guid id, CancellationToken ct)
    {
        var image = await content.GetAsync(id, UserId, DeviceId, ct, GeneratedImageContentKind.ShortVideoOutfit);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.ContentLength = image.SizeBytes;
        Response.Headers.ETag = $"\"{image.Sha256}\"";
        return File(image.Payload, image.MimeType);
    }
}

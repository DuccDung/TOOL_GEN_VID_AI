using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TOOL_SERVER.Payments;

namespace TOOL_SERVER.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/payments/sepay")]
public sealed class SepayWebhookController(ILicensePaymentService paymentService) : ControllerBase
{
    [HttpPost("webhook")]
    [RequestSizeLimit(64 * 1024)]
    [EnableRateLimiting("sepay-webhook")]
    public async Task<IActionResult> Webhook(
        [FromBody] SepayWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        await paymentService.HandleWebhookAsync(
            payload,
            cancellationToken);
        return Ok(new { success = true });
    }
}

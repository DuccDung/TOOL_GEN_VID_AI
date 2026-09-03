using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TOOL_SERVER.Payments;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize]
[Route("api/license")]
public sealed class LicensePaymentsController(ILicensePaymentService paymentService) : ControllerBase
{
    [HttpGet("offers")]
    public Task<IReadOnlyList<LicenseOfferResponse>> GetOffers(CancellationToken cancellationToken) =>
        paymentService.GetOffersAsync(cancellationToken);

    [HttpPost("payments")]
    [EnableRateLimiting("license-payment-create")]
    public Task<LicensePaymentCheckoutResponse> CreatePayment(
        [FromBody] CreateLicensePaymentRequest request,
        CancellationToken cancellationToken) =>
        paymentService.CreateOrReuseAsync(UserId(), request, cancellationToken);

    [HttpGet("payments/current")]
    public Task<CurrentLicensePaymentResponse> GetCurrentPayment(CancellationToken cancellationToken) =>
        paymentService.GetCurrentAsync(UserId(), cancellationToken);

    [HttpGet("payments/{orderCode}/status")]
    [EnableRateLimiting("license-payment-status")]
    public Task<LicensePaymentStatusResponse> GetPaymentStatus(
        string orderCode,
        CancellationToken cancellationToken) =>
        paymentService.GetStatusAsync(UserId(), orderCode, cancellationToken);

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}

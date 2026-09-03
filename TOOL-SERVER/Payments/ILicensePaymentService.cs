using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Payments;

public interface ILicensePaymentService
{
    Task<IReadOnlyList<LicenseOfferResponse>> GetOffersAsync(CancellationToken cancellationToken);

    Task<LicensePaymentCheckoutResponse> CreateOrReuseAsync(
        string userId,
        CreateLicensePaymentRequest request,
        CancellationToken cancellationToken);

    Task<CurrentLicensePaymentResponse> GetCurrentAsync(
        string userId,
        CancellationToken cancellationToken);

    Task<LicensePaymentStatusResponse> GetStatusAsync(
        string userId,
        string orderCode,
        CancellationToken cancellationToken);

    Task HandleWebhookAsync(
        SepayWebhookPayload payload,
        CancellationToken cancellationToken);
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TOOL_LOCAL.Authentication;
using TOOL_SHARED.Contracts.Accounts;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_LOCAL.Payments;

internal sealed class LicensePaymentApiClient(
    HttpClient httpClient,
    AccountSessionManager sessionManager)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<LicenseOfferResponse>> GetOffersAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<LicenseOfferResponse>>(
            HttpMethod.Get,
            "api/license/offers",
            null,
            cancellationToken);

    public Task<LicensePaymentCheckoutResponse> CreatePaymentAsync(
        CreateLicensePaymentRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<LicensePaymentCheckoutResponse>(
            HttpMethod.Post,
            "api/license/payments",
            request,
            cancellationToken);

    public Task<CurrentLicensePaymentResponse> GetCurrentPaymentAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<CurrentLicensePaymentResponse>(
            HttpMethod.Get,
            "api/license/payments/current",
            null,
            cancellationToken);

    public Task<LicensePaymentStatusResponse> GetStatusAsync(
        string orderCode,
        CancellationToken cancellationToken = default) =>
        SendAsync<LicensePaymentStatusResponse>(
            HttpMethod.Get,
            $"api/license/payments/{Uri.EscapeDataString(orderCode)}/status",
            null,
            cancellationToken);

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string uri,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await sessionManager.GetValidAccessTokenAsync(cancellationToken));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ApiErrorResponse? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
            }

            var exception = new AccountClientException(
                error?.Code ?? "license_payment_server_error",
                error?.Message ?? $"Server trả về HTTP {(int)response.StatusCode}.",
                (int)response.StatusCode,
                error?.Errors,
                error?.TraceId);
            if (exception.StatusCode == 401)
            {
                await sessionManager.InvalidateAsync(CancellationToken.None);
            }
            throw exception;
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new AccountClientException(
                "invalid_license_payment_response",
                "Server trả về dữ liệu thanh toán không hợp lệ.",
                (int)response.StatusCode);
    }
}

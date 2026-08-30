using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TOOL_SHARED.Contracts.Accounts;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_LOCAL.Authentication;

public sealed class LicenseApiClient(HttpClient httpClient, AccountSessionManager sessionManager)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<CurrentLicenseResponse> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "api/license/current", cancellationToken);

    public Task<CurrentLicenseResponse> ActivateCurrentDeviceAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/license/activate-current-device", cancellationToken);

    public Task<CurrentLicenseResponse> HeartbeatAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/license/heartbeat", cancellationToken);

    private async Task<CurrentLicenseResponse> SendAsync(
        HttpMethod method,
        string uri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await sessionManager.GetValidAccessTokenAsync(cancellationToken));
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
                error?.Code ?? "license_server_error",
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

        return await response.Content.ReadFromJsonAsync<CurrentLicenseResponse>(JsonOptions, cancellationToken)
            ?? throw new AccountClientException(
                "invalid_license_response",
                "Server trả về thông tin license không hợp lệ.",
                (int)response.StatusCode);
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TOOL_SHARED.Contracts.Authentication;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_LOCAL.Authentication;

public sealed class AccountApiClient(HttpClient httpClient) : IAccountApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        PostForTokenAsync("api/auth/register", request, cancellationToken);

    public Task<AuthTokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        PostForTokenAsync("api/auth/login", request, cancellationToken);

    public async Task RequestPasswordResetAsync(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/auth/forgot-password",
            request,
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/auth/reset-password",
            request,
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<AuthTokenResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default) =>
        PostForTokenAsync("api/auth/refresh", request, cancellationToken);

    public async Task LogoutAsync(
        string accessToken,
        LogoutRequest request,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<AuthTokenResponse> PostForTokenAsync<TRequest>(
        string uri,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(uri, request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AuthTokenResponse>(JsonOptions, cancellationToken)
            ?? throw new AccountClientException("invalid_server_response", "Server trả về dữ liệu không hợp lệ.", (int)response.StatusCode);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ApiErrorResponse? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            // A proxy or unavailable server may return a non-JSON body.
        }

        throw new AccountClientException(
            error?.Code ?? "server_error",
            error?.Message ?? $"Server trả về HTTP {(int)response.StatusCode}.",
            (int)response.StatusCode,
            error?.Errors,
            error?.TraceId);
    }
}

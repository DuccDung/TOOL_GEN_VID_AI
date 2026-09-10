using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TOOL_LOCAL.Authentication;
using System.Diagnostics;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_LOCAL.TikTok;

internal interface ITikTokGatewayClient
{
    Task<TikTokFeatureStateResponse> GetStateAsync(CancellationToken cancellationToken);
    Task<StartTikTokOAuthResponse> StartOAuthAsync(StartTikTokOAuthRequest request, CancellationToken cancellationToken);
    Task<TikTokFeatureStateResponse> CompleteOAuthAsync(CompleteTikTokOAuthRequest request, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken, Guid? connectionId = null);
    Task<TikTokCreatorInfoResponse> GetCreatorInfoAsync(CancellationToken cancellationToken, Guid? connectionId = null);
    Task<TikTokPublishHistoryResponse> GetHistoryAsync(Guid? connectionId, int page, CancellationToken cancellationToken);
    Task<TikTokDesktopAvatar?> GetAvatarAsync(Guid connectionId, CancellationToken cancellationToken) => Task.FromResult<TikTokDesktopAvatar?>(null);
    Task<InitializeTikTokPublishResponse> InitializePublishAsync(InitializeTikTokPublishRequest request, CancellationToken cancellationToken);
    Task<TikTokPublishStatusResponse> GetPublishStatusAsync(Guid publishJobId, CancellationToken cancellationToken);
}

internal sealed class TikTokGatewayClient(
    HttpClient httpClient,
    AccountSessionManager sessionManager,
    LicenseSessionManager licenseManager) : ITikTokGatewayClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<TikTokFeatureStateResponse> GetStateAsync(CancellationToken cancellationToken) =>
        SendAsync<TikTokFeatureStateResponse>(HttpMethod.Get, "api/tiktok/connections", null, cancellationToken, requireLicense: false);

    public Task<StartTikTokOAuthResponse> StartOAuthAsync(
        StartTikTokOAuthRequest request,
        CancellationToken cancellationToken) =>
        // The server checks the license, with an exception only for the requesting Admin's verification.
        SendAsync<StartTikTokOAuthResponse>(HttpMethod.Post, "api/tiktok/oauth/start", request, cancellationToken, requireLicense: false);

    public Task<TikTokFeatureStateResponse> CompleteOAuthAsync(
        CompleteTikTokOAuthRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<TikTokFeatureStateResponse>(HttpMethod.Post, "api/tiktok/oauth/complete", request, cancellationToken, requireLicense: false);

    public Task DisconnectAsync(CancellationToken cancellationToken, Guid? connectionId = null) =>
        SendWithoutResponseAsync(HttpMethod.Delete, connectionId is { } id ? $"api/tiktok/connections/{id:D}" : "api/tiktok/connection", null, cancellationToken, requireLicense: false);

    public Task<TikTokCreatorInfoResponse> GetCreatorInfoAsync(CancellationToken cancellationToken, Guid? connectionId = null) =>
        SendAsync<TikTokCreatorInfoResponse>(HttpMethod.Post, connectionId is { } id ? $"api/tiktok/connections/{id:D}/creator-info" : "api/tiktok/creator-info", new { }, cancellationToken);

    public Task<TikTokPublishHistoryResponse> GetHistoryAsync(Guid? connectionId, int page, CancellationToken cancellationToken) =>
        SendAsync<TikTokPublishHistoryResponse>(HttpMethod.Get,
            $"api/tiktok/publish-history?page={page}&pageSize=20" + (connectionId is { } id ? $"&connectionId={id:D}" : ""),
            null, cancellationToken, requireLicense: false);

    public Task<InitializeTikTokPublishResponse> InitializePublishAsync(
        InitializeTikTokPublishRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<InitializeTikTokPublishResponse>(HttpMethod.Post, "api/tiktok/publish", request, cancellationToken);

    public Task<TikTokPublishStatusResponse> GetPublishStatusAsync(
        Guid publishJobId,
        CancellationToken cancellationToken) =>
        SendAsync<TikTokPublishStatusResponse>(HttpMethod.Get, $"api/tiktok/publish/{publishJobId:D}", null, cancellationToken, requireLicense: false);

    public async Task<TikTokDesktopAvatar?> GetAvatarAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendCoreAsync(HttpMethod.Get, $"api/tiktok/connections/{connectionId:D}/avatar", null, cancellationToken, false);
            if (response.Content.Headers.ContentLength > TikTokAvatarValidation.MaximumBytes) return null;
            var mime = response.Content.Headers.ContentType?.MediaType;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var output = new MemoryStream();
            var buffer = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (output.Length + read > TikTokAvatarValidation.MaximumBytes) return null;
                output.Write(buffer, 0, read);
            }
            var bytes = output.ToArray();
            return TikTokAvatarValidation.IsValid(bytes, mime) ? new(bytes, mime!) : null;
        }
        catch (Exception error) when (error is AccountClientException or HttpRequestException or IOException or OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string uri,
        object? body,
        CancellationToken cancellationToken,
        bool requireLicense = true)
    {
        using var response = await SendCoreAsync(method, uri, body, cancellationToken, requireLicense);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
            ?? throw new AccountClientException(
                "tiktok_invalid_server_response",
                "Server trả về dữ liệu TikTok không hợp lệ.",
                (int)response.StatusCode);
    }

    private async Task SendWithoutResponseAsync(
        HttpMethod method,
        string uri,
        object? body,
        CancellationToken cancellationToken,
        bool requireLicense = true)
    {
        using var response = await SendCoreAsync(method, uri, body, cancellationToken, requireLicense);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpMethod method,
        string uri,
        object? body,
        CancellationToken cancellationToken,
        bool requireLicense)
    {
        if (requireLicense)
        {
            await licenseManager.EnsureAccessAsync(cancellationToken);
        }
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await sessionManager.GetValidAccessTokenAsync(cancellationToken));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }
        try
        {
            await ThrowServerErrorAsync(response, cancellationToken);
            throw new UnreachableException();
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task ThrowServerErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
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
            error?.Code ?? "tiktok_server_error",
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
}

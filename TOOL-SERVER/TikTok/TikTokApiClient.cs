using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TOOL_SERVER.TikTok;

public sealed record TikTokTokenResult(
    string OpenId,
    string Scope,
    string AccessToken,
    long ExpiresInSeconds,
    string RefreshToken,
    long RefreshExpiresInSeconds);

public sealed record TikTokCreatorResult(
    string Username,
    string Nickname,
    IReadOnlyList<string> PrivacyLevels,
    bool CommentDisabled,
    bool DuetDisabled,
    bool StitchDisabled,
    int MaximumVideoDurationSeconds,
    string? AvatarUrl = null);

public sealed record TikTokPublishInitResult(string PublishId, string UploadUrl);

public sealed record TikTokStatusResult(
    string Status,
    string? FailureReason,
    long UploadedBytes,
    IReadOnlyList<string> PublicPostIds);

public interface ITikTokApiClient
{
    Task<TikTokTokenResult> ExchangeCodeAsync(
        TikTokAppCredentialMaterial credential,
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken);

    Task<TikTokTokenResult> RefreshTokenAsync(
        TikTokAppCredentialMaterial credential,
        string refreshToken,
        CancellationToken cancellationToken);

    Task RevokeAsync(
        TikTokAppCredentialMaterial credential,
        string accessToken,
        CancellationToken cancellationToken);

    Task<TikTokCreatorResult> GetCreatorInfoAsync(string accessToken, CancellationToken cancellationToken);

    Task<TikTokPublishInitResult> InitializePublishAsync(
        string accessToken,
        TikTokPublishInitPayload payload,
        CancellationToken cancellationToken);

    Task<TikTokStatusResult> GetPublishStatusAsync(
        string accessToken,
        string publishId,
        CancellationToken cancellationToken);
}

public sealed record TikTokPublishInitPayload(
    string Title,
    string PrivacyLevel,
    bool DisableComment,
    bool DisableDuet,
    bool DisableStitch,
    bool BrandContent,
    bool BrandOrganic,
    bool IsAiGenerated,
    long VideoSizeBytes,
    long ChunkSizeBytes,
    int TotalChunkCount);

public sealed class TikTokProviderException(
    int statusCode,
    string providerCode,
    string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string ProviderCode { get; } = providerCode;
}

public sealed class TikTokApiClient(HttpClient httpClient) : ITikTokApiClient
{
    private static readonly Uri TokenEndpoint = new("https://open.tiktokapis.com/v2/oauth/token/");
    private static readonly Uri RevokeEndpoint = new("https://open.tiktokapis.com/v2/oauth/revoke/");
    private static readonly Uri CreatorEndpoint = new("https://open.tiktokapis.com/v2/post/publish/creator_info/query/");
    private static readonly Uri PublishEndpoint = new("https://open.tiktokapis.com/v2/post/publish/video/init/");
    private static readonly Uri StatusEndpoint = new("https://open.tiktokapis.com/v2/post/publish/status/fetch/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public Task<TikTokTokenResult> ExchangeCodeAsync(
        TikTokAppCredentialMaterial credential,
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken) =>
        RequestTokenAsync(
            new Dictionary<string, string>
            {
                ["client_key"] = credential.ClientKey,
                ["client_secret"] = credential.ClientSecret,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = codeVerifier
            },
            cancellationToken);

    public Task<TikTokTokenResult> RefreshTokenAsync(
        TikTokAppCredentialMaterial credential,
        string refreshToken,
        CancellationToken cancellationToken) =>
        RequestTokenAsync(
            new Dictionary<string, string>
            {
                ["client_key"] = credential.ClientKey,
                ["client_secret"] = credential.ClientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            },
            cancellationToken);

    public async Task RevokeAsync(
        TikTokAppCredentialMaterial credential,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RevokeEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_key"] = credential.ClientKey,
                ["client_secret"] = credential.ClientSecret,
                ["token"] = accessToken
            })
        };
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowTokenErrorAsync(response, cancellationToken);
        }
    }

    public async Task<TikTokCreatorResult> GetCreatorInfoAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedJsonRequest(HttpMethod.Post, CreatorEndpoint, accessToken, new { });
        var envelope = await SendEnvelopeAsync<CreatorData>(request, cancellationToken);
        return new TikTokCreatorResult(
            envelope.CreatorUsername ?? string.Empty,
            envelope.CreatorNickname ?? string.Empty,
            envelope.PrivacyLevelOptions ?? [],
            envelope.CommentDisabled,
            envelope.DuetDisabled,
            envelope.StitchDisabled,
            envelope.MaximumVideoPostDurationSeconds,
            envelope.CreatorAvatarUrl);
    }

    public async Task<TikTokPublishInitResult> InitializePublishAsync(
        string accessToken,
        TikTokPublishInitPayload payload,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            post_info = new
            {
                title = payload.Title,
                privacy_level = payload.PrivacyLevel,
                disable_duet = payload.DisableDuet,
                disable_comment = payload.DisableComment,
                disable_stitch = payload.DisableStitch,
                brand_content_toggle = payload.BrandContent,
                brand_organic_toggle = payload.BrandOrganic,
                is_aigc = payload.IsAiGenerated
            },
            source_info = new
            {
                source = "FILE_UPLOAD",
                video_size = payload.VideoSizeBytes,
                chunk_size = payload.ChunkSizeBytes,
                total_chunk_count = payload.TotalChunkCount
            }
        };
        using var request = AuthorizedJsonRequest(HttpMethod.Post, PublishEndpoint, accessToken, body);
        var data = await SendEnvelopeAsync<PublishData>(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(data.PublishId) || string.IsNullOrWhiteSpace(data.UploadUrl))
        {
            throw new TikTokProviderException(502, "invalid_response", "TikTok không trả về phiên upload hợp lệ.");
        }
        return new TikTokPublishInitResult(data.PublishId, data.UploadUrl);
    }

    public async Task<TikTokStatusResult> GetPublishStatusAsync(
        string accessToken,
        string publishId,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedJsonRequest(
            HttpMethod.Post,
            StatusEndpoint,
            accessToken,
            new { publish_id = publishId });
        var data = await SendEnvelopeAsync<StatusData>(request, cancellationToken);
        return new TikTokStatusResult(
            data.Status ?? "PROCESSING_UPLOAD",
            data.FailReason,
            data.UploadedBytes,
            data.PubliclyAvailablePostIds?.Select(x => x.ToString()).ToArray() ?? []);
    }

    private async Task<TikTokTokenResult> RequestTokenAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(values)
        };
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowTokenErrorAsync(response, cancellationToken);
        }
        var token = await response.Content.ReadFromJsonAsync<TokenData>(JsonOptions, cancellationToken)
            ?? throw new TikTokProviderException(502, "invalid_response", "TikTok không trả về token hợp lệ.");
        if (string.IsNullOrWhiteSpace(token.AccessToken) ||
            string.IsNullOrWhiteSpace(token.RefreshToken) ||
            string.IsNullOrWhiteSpace(token.OpenId))
        {
            throw new TikTokProviderException(502, "invalid_response", "TikTok không trả về token hợp lệ.");
        }
        return new TikTokTokenResult(
            token.OpenId,
            token.Scope ?? string.Empty,
            token.AccessToken,
            token.ExpiresIn,
            token.RefreshToken,
            token.RefreshExpiresIn);
    }

    private async Task<TData> SendEnvelopeAsync<TData>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        where TData : class
    {
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<TData>>(JsonOptions, cancellationToken);
        var errorCode = envelope?.Error?.Code;
        if (!response.IsSuccessStatusCode ||
            !string.Equals(errorCode, "ok", StringComparison.OrdinalIgnoreCase) ||
            envelope?.Data is null)
        {
            throw new TikTokProviderException(
                (int)response.StatusCode,
                string.IsNullOrWhiteSpace(errorCode) ? "invalid_response" : errorCode,
                SafeProviderMessage(envelope?.Error?.Message));
        }
        return envelope!.Data!;
    }

    private static HttpRequestMessage AuthorizedJsonRequest(
        HttpMethod method,
        Uri endpoint,
        string accessToken,
        object body)
    {
        var request = new HttpRequestMessage(method, endpoint)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task ThrowTokenErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        TokenError? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<TokenError>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
        }
        throw new TikTokProviderException(
            (int)response.StatusCode,
            string.IsNullOrWhiteSpace(error?.Error) ? "oauth_failed" : error.Error,
            SafeProviderMessage(error?.ErrorDescription));
    }

    private static string SafeProviderMessage(string? message) =>
        string.IsNullOrWhiteSpace(message)
            ? "TikTok không thể xử lý yêu cầu. Vui lòng thử lại."
            : message.Trim().Length <= 500 ? message.Trim() : message.Trim()[..500];

    private sealed record ApiEnvelope<T>(
        [property: JsonPropertyName("data")] T? Data,
        [property: JsonPropertyName("error")] ApiError? Error)
        where T : class;

    private sealed record ApiError(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("message")] string? Message);

    private sealed record TokenError(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);

    private sealed record TokenData(
        [property: JsonPropertyName("open_id")] string? OpenId,
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] long ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("refresh_expires_in")] long RefreshExpiresIn);

    private sealed record CreatorData(
        [property: JsonPropertyName("creator_username")] string? CreatorUsername,
        [property: JsonPropertyName("creator_nickname")] string? CreatorNickname,
        [property: JsonPropertyName("privacy_level_options")] string[]? PrivacyLevelOptions,
        [property: JsonPropertyName("comment_disabled")] bool CommentDisabled,
        [property: JsonPropertyName("duet_disabled")] bool DuetDisabled,
        [property: JsonPropertyName("stitch_disabled")] bool StitchDisabled,
        [property: JsonPropertyName("max_video_post_duration_sec")] int MaximumVideoPostDurationSeconds,
        [property: JsonPropertyName("creator_avatar_url")] string? CreatorAvatarUrl);

    private sealed record PublishData(
        [property: JsonPropertyName("publish_id")] string? PublishId,
        [property: JsonPropertyName("upload_url")] string? UploadUrl);

    private sealed record StatusData(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("fail_reason")] string? FailReason,
        [property: JsonPropertyName("uploaded_bytes")] long UploadedBytes,
        [property: JsonPropertyName("publicaly_available_post_id")] long[]? PubliclyAvailablePostIds);
}

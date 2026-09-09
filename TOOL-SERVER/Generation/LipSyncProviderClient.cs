using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TOOL_SERVER.Generation;

internal sealed record LipSyncProviderTaskResult(
    string ExternalRequestId,
    string Status,
    decimal ProgressPercent,
    string? OutputUrl,
    string? ErrorCode,
    string? ErrorMessage,
    int? ActualDurationSeconds,
    string ResponseJson);

internal interface ILipSyncProviderClient
{
    string ProviderCode { get; }

    Task<LipSyncProviderTaskResult> SubmitAsync(
        ProviderRuntimeConfiguration provider,
        string videoUrl,
        string audioUrl,
        CancellationToken cancellationToken);

    Task<LipSyncProviderTaskResult> GetStatusAsync(
        ProviderRuntimeConfiguration provider,
        string externalRequestId,
        CancellationToken cancellationToken);
}

internal interface ILipSyncProviderRouter
{
    ILipSyncProviderClient Resolve(string providerCode);
}

internal sealed class LipSyncProviderRouter(IEnumerable<ILipSyncProviderClient> clients) : ILipSyncProviderRouter
{
    private readonly IReadOnlyDictionary<string, ILipSyncProviderClient> _clients = clients
        .ToDictionary(x => x.ProviderCode, StringComparer.OrdinalIgnoreCase);

    public ILipSyncProviderClient Resolve(string providerCode) =>
        _clients.TryGetValue(providerCode, out var client)
            ? client
            : throw new ProviderHttpException(providerCode, "lip_sync_provider_not_supported", "Provider lip-sync chưa được server hỗ trợ.");
}

internal sealed class FalLipSyncClient(IHttpClientFactory httpClientFactory) : ILipSyncProviderClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ProviderCode => ProviderCodes.Fal;

    public async Task<LipSyncProviderTaskResult> SubmitAsync(
        ProviderRuntimeConfiguration provider,
        string videoUrl,
        string audioUrl,
        CancellationToken cancellationToken)
    {
        ValidateProvider(provider);
        RequirePublicHttps(videoUrl, "video");
        RequirePublicHttps(audioUrl, "audio");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.BaseUri, provider.ModelCode.Trim('/')))
        {
            Content = JsonContent.Create(new
            {
                model = FalLipSyncPolicy.ModelVariant,
                video_url = videoUrl,
                audio_url = audioUrl,
                sync_mode = FalLipSyncPolicy.SyncMode
            }, options: JsonOptions)
        };
        ApplyHeaders(request, provider);
        using var response = await httpClientFactory.CreateClient("FalRuntime")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw NormalizeHttpError(response.StatusCode, json);
        }
        using var document = Parse(json);
        var requestId = ReadString(document.RootElement, "request_id");
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 200)
        {
            throw InvalidResponse("Fal không trả về request_id cho tác vụ lip-sync.");
        }
        return Safe(requestId, "Submitted", 5m);
    }

    public async Task<LipSyncProviderTaskResult> GetStatusAsync(
        ProviderRuntimeConfiguration provider,
        string externalRequestId,
        CancellationToken cancellationToken)
    {
        ValidateProvider(provider);
        if (string.IsNullOrWhiteSpace(externalRequestId) || externalRequestId.Length > 200)
        {
            throw new ProviderHttpException(ProviderCodes.Fal, "provider_request_id_invalid", "Request ID Fal không hợp lệ.");
        }
        using var statusRequest = new HttpRequestMessage(HttpMethod.Get, QueueUri(provider, externalRequestId, true));
        ApplyHeaders(statusRequest, provider);
        using var statusResponse = await httpClientFactory.CreateClient("FalRuntime")
            .SendAsync(statusRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var statusJson = await statusResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!statusResponse.IsSuccessStatusCode)
        {
            throw NormalizeHttpError(statusResponse.StatusCode, statusJson);
        }
        using var statusDocument = Parse(statusJson);
        var status = ReadString(statusDocument.RootElement, "status")?.ToUpperInvariant() ?? "UNKNOWN";
        if (HasError(statusDocument.RootElement) || status is "FAILED" or "CANCELLED")
        {
            return Failure(statusDocument.RootElement, externalRequestId);
        }
        if (status == "IN_QUEUE")
        {
            return Safe(externalRequestId, "Queued", 10m);
        }
        if (status == "IN_PROGRESS")
        {
            return Safe(externalRequestId, "Processing", 50m);
        }
        if (status != "COMPLETED")
        {
            return Safe(externalRequestId, "Unknown", 0m);
        }
        if (HasError(statusDocument.RootElement))
        {
            return Failure(statusDocument.RootElement, externalRequestId);
        }

        using var resultRequest = new HttpRequestMessage(HttpMethod.Get, QueueUri(provider, externalRequestId, false));
        ApplyHeaders(resultRequest, provider);
        using var resultResponse = await httpClientFactory.CreateClient("FalRuntime")
            .SendAsync(resultRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var resultJson = await resultResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!resultResponse.IsSuccessStatusCode)
        {
            throw NormalizeHttpError(resultResponse.StatusCode, resultJson);
        }
        using var resultDocument = Parse(resultJson);
        if (HasError(resultDocument.RootElement))
        {
            return Failure(resultDocument.RootElement, externalRequestId);
        }
        var outputUrl = ReadVideoUrl(resultDocument.RootElement);
        if (!Uri.TryCreate(outputUrl, UriKind.Absolute, out var outputUri) || outputUri.Scheme != Uri.UriSchemeHttps)
        {
            return Safe(externalRequestId, "Failed", 0m, "provider_output_missing", "Fal hoàn tất lip-sync nhưng không cung cấp output HTTPS hợp lệ.");
        }
        return new LipSyncProviderTaskResult(
            externalRequestId,
            "Completed",
            100m,
            outputUrl,
            null,
            null,
            ReadDurationSeconds(resultDocument.RootElement),
            JsonSerializer.Serialize(new { requestId = externalRequestId, status = "Completed" }, JsonOptions));
    }

    private static void ValidateProvider(ProviderRuntimeConfiguration provider)
    {
        if (!provider.ProviderCode.Equals(ProviderCodes.Fal, StringComparison.OrdinalIgnoreCase) ||
            !FalLipSyncPolicy.IsApprovedEndpoint(provider.ModelCode) ||
            !ProviderRuntimeResolver.IsAllowedBaseUri(ProviderCodes.Fal, provider.BaseUri))
        {
            throw new ProviderHttpException(ProviderCodes.Fal, "fal_endpoint_not_allowed", "Endpoint Fal lip-sync không nằm trong allowlist của server.");
        }
    }

    private static void RequirePublicHttps(string value, string kind)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.Port != 443 ||
            uri.IsLoopback ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ProviderHttpException(ProviderCodes.Fal, "lip_sync_input_url_invalid", $"URL input {kind} lip-sync không hợp lệ.");
        }
    }

    private static Uri QueueUri(ProviderRuntimeConfiguration provider, string requestId, bool status)
    {
        var path = $"{provider.ModelCode.Trim('/')}/requests/{Uri.EscapeDataString(requestId)}";
        return new Uri(provider.BaseUri, status ? $"{path}/status" : path);
    }

    private static void ApplyHeaders(HttpRequestMessage request, ProviderRuntimeConfiguration provider)
    {
        OpenAiContentClient.ApplyAuthentication(request, provider);
        request.Headers.TryAddWithoutValidation("X-Fal-No-Retry", "1");
        request.Headers.TryAddWithoutValidation("X-Fal-Store-IO", "0");
    }

    private static LipSyncProviderTaskResult Safe(
        string requestId,
        string status,
        decimal progress,
        string? errorCode = null,
        string? errorMessage = null) =>
        new(requestId, status, progress, null, errorCode, errorMessage, null,
            JsonSerializer.Serialize(new { requestId, status, errorCode }, JsonOptions));

    private static LipSyncProviderTaskResult Failure(JsonElement root, string requestId)
    {
        var diagnostic = $"{ReadErrorType(root)} {ReadErrorMessage(root)}".ToLowerInvariant();
        var moderation = ContainsAny(diagnostic, "moderation", "safety", "policy", "content");
        return Safe(
            requestId,
            "Failed",
            0m,
            moderation ? "provider_moderation_rejected" : "provider_generation_failed",
            moderation ? "Input lip-sync không vượt qua kiểm duyệt của Fal." : "Fal không thể hoàn tất lip-sync cho cảnh này.");
    }

    private static bool HasError(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        ((root.TryGetProperty("error", out var error) && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined) ||
         (root.TryGetProperty("error_type", out var errorType) && errorType.ValueKind == JsonValueKind.String));

    private static string? ReadVideoUrl(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (root.TryGetProperty("video", out var video) && video.ValueKind == JsonValueKind.Object)
        {
            return ReadString(video, "url");
        }
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("video", out video) && video.ValueKind == JsonValueKind.Object)
        {
            return ReadString(video, "url");
        }
        return null;
    }

    private static int? ReadDurationSeconds(JsonElement root)
    {
        JsonElement video;
        if (root.TryGetProperty("video", out video) ||
            root.TryGetProperty("data", out var data) && data.TryGetProperty("video", out video))
        {
            if (video.ValueKind == JsonValueKind.Object && video.TryGetProperty("duration", out var duration) && duration.TryGetDecimal(out var seconds))
            {
                return Math.Max(1, (int)Math.Ceiling(seconds));
            }
        }
        return null;
    }

    private static string ReadErrorType(JsonElement root) => ReadString(root, "error_type") ?? string.Empty;

    private static string ReadErrorMessage(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error))
        {
            return string.Empty;
        }
        return error.ValueKind switch
        {
            JsonValueKind.String => error.GetString() ?? string.Empty,
            JsonValueKind.Object => ReadString(error, "message") ?? string.Empty,
            _ => string.Empty
        };
    }

    private static ProviderHttpException NormalizeHttpError(HttpStatusCode statusCode, string responseJson)
    {
        var upstream = ProviderHttpException.FromResponse(ProviderCodes.Fal, statusCode, responseJson);
        var diagnostic = $"{upstream.Code} {upstream.Message}".ToLowerInvariant();
        var (code, message) = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ("provider_credential_invalid", "Credential Fal không hợp lệ hoặc không có quyền dùng lip-sync."),
            HttpStatusCode.TooManyRequests => ("provider_rate_limited", "Fal đang giới hạn tần suất. Vui lòng thử lại sau."),
            HttpStatusCode.UnprocessableEntity when ContainsAny(diagnostic, "moderation", "safety", "policy") => ("provider_moderation_rejected", "Input lip-sync không vượt qua kiểm duyệt của Fal."),
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest => ("provider_invalid_request", "Fal không chấp nhận input lip-sync của cảnh."),
            _ when ContainsAny(diagnostic, "quota", "balance", "billing", "credit") => ("provider_quota_exhausted", "Tài khoản Fal không đủ hạn mức."),
            _ => ("provider_unavailable", "Fal lip-sync đang tạm thời gián đoạn.")
        };
        return new ProviderHttpException(ProviderCodes.Fal, code, message, statusCode: statusCode);
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ProviderHttpException(ProviderCodes.Fal, "provider_invalid_response", "Fal trả về dữ liệu lip-sync không hợp lệ.", exception);
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static bool ContainsAny(string value, params string[] candidates) => candidates.Any(value.Contains);

    private static ProviderHttpException InvalidResponse(string message) =>
        new(ProviderCodes.Fal, "provider_invalid_response", message);
}

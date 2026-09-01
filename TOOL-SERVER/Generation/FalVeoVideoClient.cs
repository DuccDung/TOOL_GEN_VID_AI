using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TOOL_SERVER.Generation;

internal sealed class FalVeoVideoClient(IHttpClientFactory httpClientFactory) : IVideoProviderClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ProviderCode => ProviderCodes.Fal;

    public async Task<VideoProviderTaskResult> SubmitAsync(
        ProviderRuntimeConfiguration provider,
        string prompt,
        string aspectRatio,
        int durationSeconds,
        string resolution,
        bool nativeAudio,
        string safetyIdentifier,
        VideoProviderReferenceImage? referenceImage,
        CancellationToken cancellationToken)
    {
        ValidateRuntime(provider, aspectRatio, durationSeconds, resolution, nativeAudio, referenceImage);
        var body = new
        {
            prompt,
            image_url = $"data:{referenceImage!.MimeType};base64,{referenceImage.Base64Data}",
            aspect_ratio = aspectRatio,
            duration = $"{durationSeconds}s",
            resolution = FalVeoPolicy.Resolution,
            generate_audio = true,
            auto_fix = false,
            safety_tolerance = FalVeoPolicy.SafetyTolerance
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            EndpointUri(provider, string.Empty))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
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
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw InvalidResponse("Fal không trả về request_id cho tác vụ Veo.");
        }
        return CreateSafeResult(
            requestId,
            "Submitted",
            5m,
            durationSeconds: durationSeconds);
    }

    public async Task<VideoProviderTaskResult> GetStatusAsync(
        ProviderRuntimeConfiguration provider,
        string externalRequestId,
        CancellationToken cancellationToken)
    {
        ValidateProvider(provider);
        if (string.IsNullOrWhiteSpace(externalRequestId) || externalRequestId.Length > 200)
        {
            throw new ProviderHttpException(
                ProviderCodes.Fal,
                "provider_request_id_invalid",
                "Request ID Fal không hợp lệ.");
        }

        using var statusRequest = new HttpRequestMessage(
            HttpMethod.Get,
            EndpointUri(provider, $"requests/{Uri.EscapeDataString(externalRequestId)}/status"));
        ApplyHeaders(statusRequest, provider);
        using var statusResponse = await httpClientFactory.CreateClient("FalRuntime")
            .SendAsync(statusRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var statusJson = await statusResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!statusResponse.IsSuccessStatusCode)
        {
            throw NormalizeHttpError(statusResponse.StatusCode, statusJson);
        }
        using var statusDocument = Parse(statusJson);
        var upstreamStatus = ReadString(statusDocument.RootElement, "status")?.ToUpperInvariant() ?? "UNKNOWN";
        if (upstreamStatus == "IN_QUEUE")
        {
            return CreateSafeResult(externalRequestId, "Queued", 10m);
        }
        if (upstreamStatus == "IN_PROGRESS")
        {
            return CreateSafeResult(externalRequestId, "Processing", 50m);
        }
        if (upstreamStatus != "COMPLETED")
        {
            return CreateSafeResult(externalRequestId, "Unknown", 0m);
        }
        if (HasError(statusDocument.RootElement))
        {
            return CreateTerminalFailure(statusDocument.RootElement, externalRequestId);
        }

        using var resultRequest = new HttpRequestMessage(
            HttpMethod.Get,
            EndpointUri(provider, $"requests/{Uri.EscapeDataString(externalRequestId)}"));
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
            return CreateTerminalFailure(resultDocument.RootElement, externalRequestId);
        }

        var outputUrl = ReadVideoUrl(resultDocument.RootElement);
        if (!Uri.TryCreate(outputUrl, UriKind.Absolute, out var outputUri) || outputUri.Scheme != Uri.UriSchemeHttps)
        {
            return new VideoProviderTaskResult(
                externalRequestId,
                "Failed",
                0m,
                null,
                "provider_output_missing",
                "Fal báo hoàn tất nhưng không cung cấp video đầu ra HTTPS hợp lệ.",
                null,
                null,
                null,
                JsonSerializer.Serialize(new { requestId = externalRequestId, status = "Failed", errorCode = "provider_output_missing" }, JsonOptions));
        }
        return new VideoProviderTaskResult(
            externalRequestId,
            "Completed",
            100m,
            outputUrl,
            null,
            null,
            null,
            null,
            ReadDurationSeconds(resultDocument.RootElement),
            JsonSerializer.Serialize(new { requestId = externalRequestId, status = "Completed" }, JsonOptions));
    }

    private static void ValidateRuntime(
        ProviderRuntimeConfiguration provider,
        string aspectRatio,
        int durationSeconds,
        string resolution,
        bool nativeAudio,
        VideoProviderReferenceImage? referenceImage)
    {
        ValidateProvider(provider);
        if (aspectRatio is not ("16:9" or "9:16") ||
            durationSeconds is not (4 or 6 or 8) ||
            !resolution.Equals(FalVeoPolicy.Resolution, StringComparison.OrdinalIgnoreCase) ||
            !nativeAudio)
        {
            throw new ProviderHttpException(
                ProviderCodes.Fal,
                "provider_invalid_request",
                "Cấu hình Veo phải là 720p, Native Audio, tỷ lệ 16:9/9:16 và thời lượng 4/6/8 giây.");
        }
        if (referenceImage is null ||
            referenceImage.MimeType is not ("image/jpeg" or "image/png") ||
            string.IsNullOrWhiteSpace(referenceImage.Base64Data))
        {
            throw new ProviderHttpException(
                ProviderCodes.Fal,
                "fal_first_frame_required",
                "Veo Image-to-Video cần first-frame PNG/JPEG đã duyệt.");
        }
    }

    private static void ValidateProvider(ProviderRuntimeConfiguration provider)
    {
        if (!provider.ProviderCode.Equals(ProviderCodes.Fal, StringComparison.OrdinalIgnoreCase) ||
            !FalVeoPolicy.IsApprovedEndpoint(provider.ModelCode) ||
            !ProviderRuntimeResolver.IsAllowedBaseUri(ProviderCodes.Fal, provider.BaseUri))
        {
            throw new ProviderHttpException(
                ProviderCodes.Fal,
                "fal_endpoint_not_allowed",
                "Endpoint Fal/Veo không nằm trong allowlist của server.");
        }
    }

    private static Uri EndpointUri(ProviderRuntimeConfiguration provider, string suffix)
    {
        var endpoint = provider.ModelCode.Trim('/');
        var path = suffix.Length == 0 ? endpoint : $"{endpoint}/{suffix.TrimStart('/')}";
        return new Uri(provider.BaseUri, path);
    }

    private static void ApplyHeaders(HttpRequestMessage request, ProviderRuntimeConfiguration provider)
    {
        OpenAiContentClient.ApplyAuthentication(request, provider);
        request.Headers.TryAddWithoutValidation("X-Fal-No-Retry", "1");
        request.Headers.TryAddWithoutValidation("X-Fal-Store-IO", "0");
        request.Headers.TryAddWithoutValidation(
            "X-Fal-Object-Lifecycle-Preference",
            $"{{\"expiration_duration_seconds\":{FalVeoPolicy.ObjectLifecycleSeconds}}}");
    }

    private static VideoProviderTaskResult CreateSafeResult(
        string requestId,
        string status,
        decimal progress,
        string? errorCode = null,
        string? errorMessage = null,
        int? durationSeconds = null) =>
        new(
            requestId,
            status,
            progress,
            null,
            errorCode,
            errorMessage,
            null,
            null,
            durationSeconds,
            JsonSerializer.Serialize(new { requestId, status, errorCode, actualDurationSeconds = durationSeconds }, JsonOptions));

    private static VideoProviderTaskResult CreateTerminalFailure(JsonElement root, string requestId)
    {
        var diagnostic = $"{ReadErrorType(root)} {ReadErrorMessage(root)}".ToLowerInvariant();
        var moderation = ContainsAny(diagnostic, "moderation", "safety", "policy", "content");
        return CreateSafeResult(
            requestId,
            "Failed",
            0m,
            moderation ? "provider_moderation_rejected" : "provider_generation_failed",
            moderation
                ? "Nội dung cảnh không vượt qua kiểm duyệt của Fal/Veo."
                : "Fal/Veo không thể tạo video cho cảnh này.");
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
        var value = ReadString(root, "duration") ?? ReadString(root, "duration_seconds");
        if (value?.EndsWith('s') == true)
        {
            value = value[..^1];
        }
        return int.TryParse(value, out var duration) && duration > 0 ? duration : null;
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
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                ("provider_credential_invalid", "Credential Fal không hợp lệ hoặc không có quyền dùng endpoint Veo."),
            HttpStatusCode.TooManyRequests =>
                ("provider_rate_limited", "Fal đang giới hạn tần suất. Vui lòng thử lại sau."),
            HttpStatusCode.UnprocessableEntity when ContainsAny(diagnostic, "moderation", "safety", "policy") =>
                ("provider_moderation_rejected", "Nội dung cảnh không vượt qua kiểm duyệt của Fal/Veo."),
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest =>
                ("provider_invalid_request", "Fal/Veo không chấp nhận cấu hình hoặc nội dung của cảnh."),
            _ when ContainsAny(diagnostic, "quota", "balance", "billing", "credit") =>
                ("provider_quota_exhausted", "Tài khoản Fal tạm thời không đủ hạn mức."),
            _ =>
                ("provider_unavailable", "Fal/Veo đang tạm thời gián đoạn. Vui lòng thử lại sau.")
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
            throw new ProviderHttpException(
                ProviderCodes.Fal,
                "provider_invalid_response",
                "Fal trả về dữ liệu không hợp lệ.",
                exception);
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

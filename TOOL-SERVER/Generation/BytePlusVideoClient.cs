using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TOOL_SERVER.Generation;

internal sealed class BytePlusVideoClient(IHttpClientFactory httpClientFactory) : IVideoProviderClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ProviderCode => ProviderCodes.BytePlus;

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
        var content = new List<object>
        {
            new { type = "text", text = prompt }
        };
        if (referenceImage is not null)
        {
            content.Add(new
            {
                type = "image_url",
                image_url = new
                {
                    url = $"data:{referenceImage.MimeType};base64,{referenceImage.Base64Data}"
                },
                role = "reference_image"
            });
        }
        var body = new
        {
            model = provider.ModelCode,
            content,
            ratio = aspectRatio,
            duration = durationSeconds,
            resolution,
            generate_audio = nativeAudio,
            watermark = false,
            return_last_frame = false,
            execution_expires_after = 172800,
            safety_identifier = safetyIdentifier
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(provider.BaseUri, "contents/generations/tasks"))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        OpenAiContentClient.ApplyAuthentication(request, provider);
        using var response = await httpClientFactory.CreateClient("BytePlusRuntime")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw NormalizeHttpError(response.StatusCode, json);
        }
        using var document = Parse(json);
        var taskId = ReadString(document.RootElement, "id");
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw InvalidResponse("BytePlus không trả về task ID.");
        }
        return new VideoProviderTaskResult(
            taskId,
            "Submitted",
            5m,
            null,
            null,
            null,
            null,
            null,
            durationSeconds,
            JsonSerializer.Serialize(new { taskId, status = "Submitted" }, JsonOptions));
    }

    public async Task<VideoProviderTaskResult> GetStatusAsync(
        ProviderRuntimeConfiguration provider,
        string externalRequestId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(provider.BaseUri, $"contents/generations/tasks/{Uri.EscapeDataString(externalRequestId)}"));
        OpenAiContentClient.ApplyAuthentication(request, provider);
        using var response = await httpClientFactory.CreateClient("BytePlusRuntime")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw NormalizeHttpError(response.StatusCode, json);
        }
        using var document = Parse(json);
        return CreateResult(document.RootElement, externalRequestId);
    }

    private static VideoProviderTaskResult CreateResult(JsonElement root, string fallbackId)
    {
        var externalId = ReadString(root, "id") ?? fallbackId;
        var upstreamStatus = ReadString(root, "status")?.ToLowerInvariant() ?? "unknown";
        var status = upstreamStatus switch
        {
            "queued" => "Queued",
            "running" => "Processing",
            "succeeded" => "Completed",
            "failed" => "Failed",
            "cancelled" => "Cancelled",
            "expired" => "Expired",
            _ => "Unknown"
        };
        var progress = status switch
        {
            "Queued" => 10m,
            "Processing" => 50m,
            "Completed" => 100m,
            _ => 0m
        };
        string? outputUrl = null;
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object)
        {
            outputUrl = ReadString(content, "video_url");
            if (!Uri.TryCreate(outputUrl, UriKind.Absolute, out var outputUri) || outputUri.Scheme != Uri.UriSchemeHttps)
            {
                outputUrl = null;
            }
        }
        long? completionTokens = null;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object &&
            usage.TryGetProperty("completion_tokens", out var tokenValue) &&
            tokenValue.TryGetInt64(out var parsedTokens) && parsedTokens >= 0)
        {
            completionTokens = parsedTokens;
        }
        int? actualDuration = root.TryGetProperty("duration", out var durationValue) &&
                              durationValue.TryGetInt32(out var parsedDuration)
            ? parsedDuration
            : null;
        string? errorCode = null;
        string? errorMessage = null;
        if (status == "Completed" && string.IsNullOrWhiteSpace(outputUrl))
        {
            status = "Failed";
            progress = 0;
            errorCode = "provider_output_missing";
            errorMessage = "BytePlus báo hoàn tất nhưng không cung cấp video đầu ra hợp lệ.";
        }
        else if (status is "Failed" or "Expired")
        {
            (errorCode, errorMessage) = NormalizeTaskFailure(root, status);
        }
        var responseJson = JsonSerializer.Serialize(new
        {
            taskId = externalId,
            status,
            completionTokens,
            actualDuration
        }, JsonOptions);
        return new VideoProviderTaskResult(
            externalId,
            status,
            progress,
            outputUrl,
            errorCode,
            errorMessage,
            null,
            completionTokens,
            actualDuration,
            responseJson);
    }

    private static (string Code, string Message) NormalizeTaskFailure(JsonElement root, string status)
    {
        var upstreamCode = string.Empty;
        var upstreamMessage = string.Empty;
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            upstreamCode = ReadString(error, "code") ?? string.Empty;
            upstreamMessage = ReadString(error, "message") ?? string.Empty;
        }
        var diagnostic = $"{upstreamCode} {upstreamMessage}".ToLowerInvariant();
        if (ContainsAny(diagnostic, "moderation", "safety", "policy", "content_filter"))
        {
            return ("provider_moderation_rejected", "Nội dung cảnh không vượt qua kiểm duyệt của BytePlus.");
        }
        if (status == "Expired")
        {
            return ("provider_task_expired", "Tác vụ BytePlus đã hết thời gian xử lý.");
        }
        return ("provider_generation_failed", "BytePlus không thể tạo video cho cảnh này.");
    }

    private static ProviderHttpException NormalizeHttpError(HttpStatusCode statusCode, string responseJson)
    {
        var upstream = ProviderHttpException.FromResponse(ProviderCodes.BytePlus, statusCode, responseJson);
        var diagnostic = $"{upstream.Code} {upstream.Message}".ToLowerInvariant();
        var (code, message) = statusCode switch
        {
            HttpStatusCode.TooManyRequests =>
                ("provider_rate_limited", "BytePlus đang giới hạn tần suất. Vui lòng thử lại sau."),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                ("provider_credential_invalid", "Credential BytePlus không hợp lệ hoặc không có quyền dùng model."),
            HttpStatusCode.BadRequest when ContainsAny(diagnostic, "moderation", "safety", "policy", "content_filter") =>
                ("provider_moderation_rejected", "Nội dung cảnh không vượt qua kiểm duyệt của BytePlus."),
            HttpStatusCode.BadRequest =>
                ("provider_invalid_request", "BytePlus không chấp nhận cấu hình hoặc nội dung của cảnh."),
            _ when ContainsAny(diagnostic, "quota", "balance", "billing", "credit") =>
                ("provider_quota_exhausted", "Tài khoản BytePlus tạm thời không đủ hạn mức."),
            _ =>
                ("provider_unavailable", "BytePlus đang tạm thời gián đoạn. Vui lòng thử lại sau.")
        };
        return new ProviderHttpException(ProviderCodes.BytePlus, code, message, statusCode: statusCode);
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
                ProviderCodes.BytePlus,
                "provider_invalid_response",
                "BytePlus trả về dữ liệu không hợp lệ.",
                exception);
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
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

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(value.Contains);

    private static ProviderHttpException InvalidResponse(string message) =>
        new(ProviderCodes.BytePlus, "provider_invalid_response", message);
}

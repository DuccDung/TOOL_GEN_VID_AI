using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;

namespace TOOL_SERVER.Generation;

internal sealed record KlingTaskResult(
    string ExternalRequestId,
    string Status,
    decimal ProgressPercent,
    string? OutputUrl,
    string? ErrorCode,
    string? ErrorMessage,
    decimal? ReportedBillingAmount,
    string ResponseJson);

internal sealed record KlingReferenceImageData(
    Guid CharacterReferenceId,
    string MimeType,
    string Base64Data,
    string Sha256);

internal interface IKlingVideoClient
{
    Task<KlingTaskResult> SubmitAsync(
        ProviderRuntimeConfiguration provider,
        string prompt,
        string aspectRatio,
        int durationSeconds,
        string resolution,
        bool nativeAudio,
        string externalTaskId,
        KlingReferenceImageData? referenceImage,
        CancellationToken cancellationToken);

    Task<KlingTaskResult> GetStatusAsync(
        ProviderRuntimeConfiguration provider,
        string externalRequestId,
        CancellationToken cancellationToken);
}

internal sealed class KlingVideoClient(IHttpClientFactory httpClientFactory) : IKlingVideoClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<KlingTaskResult> SubmitAsync(
        ProviderRuntimeConfiguration provider,
        string prompt,
        string aspectRatio,
        int durationSeconds,
        string resolution,
        bool nativeAudio,
        string externalTaskId,
        KlingReferenceImageData? referenceImage,
        CancellationToken cancellationToken)
    {
        var settings = new
        {
            resolution,
            aspect_ratio = aspectRatio,
            duration = durationSeconds,
            audio = nativeAudio ? "native" : "off",
            multi_shot = false
        };
        var options = new
        {
            external_task_id = externalTaskId,
            watermark_info = new { enabled = false }
        };
        object body;
        string endpoint;
        if (referenceImage is null)
        {
            endpoint = $"text-to-video/{Uri.EscapeDataString(provider.ModelCode)}";
            body = new { prompt, settings, options };
        }
        else
        {
            var imageData = $"data:{referenceImage.MimeType};base64,{referenceImage.Base64Data}";
            var isOmni = provider.ModelCode.Contains("omni", StringComparison.OrdinalIgnoreCase);
            endpoint = isOmni
                ? $"omni-video/{Uri.EscapeDataString(provider.ModelCode)}"
                : $"image-to-video/{Uri.EscapeDataString(provider.ModelCode)}";
            body = new
            {
                prompt,
                settings,
                options,
                contents = new[]
                {
                    new
                    {
                        type = isOmni ? "refer_image" : "first_frame",
                        url = imageData
                    }
                }
            };
        }
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(provider.BaseUri, endpoint))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        OpenAiContentClient.ApplyAuthentication(request, provider);

        using var response = await httpClientFactory.CreateClient("KlingRuntime")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw NormalizeHttpError(response.StatusCode, json);
        }

        using var document = ParseAndValidate(json);
        var data = document.RootElement.TryGetProperty("data", out var value) ? value : default;
        var taskId = GetString(data, "id");
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw InvalidResponse("Kling không trả về task ID.");
        }

        return CreateResult(data, taskId);
    }

    public async Task<KlingTaskResult> GetStatusAsync(
        ProviderRuntimeConfiguration provider,
        string externalRequestId,
        CancellationToken cancellationToken)
    {
        var relativeUrl = $"tasks?task_ids={Uri.EscapeDataString(externalRequestId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(provider.BaseUri, relativeUrl));
        OpenAiContentClient.ApplyAuthentication(request, provider);
        using var response = await httpClientFactory.CreateClient("KlingRuntime")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw NormalizeHttpError(response.StatusCode, json);
        }

        using var document = ParseAndValidate(json);
        var task = ExtractTask(document.RootElement);
        if (task.ValueKind != JsonValueKind.Object)
        {
            throw InvalidResponse("Kling không tìm thấy task đã gửi.");
        }

        return CreateResult(task, externalRequestId);
    }

    private static JsonDocument ParseAndValidate(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ProviderHttpException(
                ProviderCodes.Kling,
                "kling_invalid_response",
                "Kling trả về dữ liệu không hợp lệ.",
                exception);
        }

        var root = document.RootElement;
        if (root.TryGetProperty("code", out var code) && code.TryGetInt32(out var number) && number != 0)
        {
            var message = GetString(root, "message");
            document.Dispose();
            throw NormalizeApiError(number, message);
        }

        return document;
    }

    private static JsonElement ExtractTask(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data))
        {
            return default;
        }

        if (data.ValueKind == JsonValueKind.Array)
        {
            return data.GetArrayLength() > 0 ? data[0] : default;
        }

        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array)
        {
            return result.GetArrayLength() > 0 ? result[0] : default;
        }

        return data;
    }

    private static KlingTaskResult CreateResult(JsonElement task, string fallbackTaskId)
    {
        var externalId = GetString(task, "id") ?? fallbackTaskId;
        var upstreamStatus = GetString(task, "status")?.ToLowerInvariant() ?? "unknown";
        var status = upstreamStatus switch
        {
            "submitted" => "Submitted",
            "processing" => "Processing",
            "succeeded" => "Completed",
            "failed" => "Failed",
            _ => "Unknown"
        };
        var progress = status switch
        {
            "Submitted" => 5m,
            "Processing" => 50m,
            "Completed" => 100m,
            _ => 0m
        };
        var outputUrl = ExtractVideoUrl(task);
        var reportedBillingAmount = ExtractReportedBillingAmount(task);
        string? errorCode = null;
        string? errorMessage = null;
        if (status == "Completed" && string.IsNullOrWhiteSpace(outputUrl))
        {
            status = "Failed";
            progress = 0m;
            errorCode = "kling_output_missing";
            errorMessage = "Kling báo hoàn tất nhưng chưa cung cấp video đầu ra hợp lệ.";
        }
        else if (status == "Failed")
        {
            (errorCode, errorMessage) = NormalizeTaskFailure(GetString(task, "message"));
        }
        var responseJson = JsonSerializer.Serialize(new
        {
            taskId = externalId,
            status,
            outputUrl,
            reportedBillingAmount
        }, JsonOptions);
        return new KlingTaskResult(
            externalId,
            status,
            progress,
            outputUrl,
            errorCode,
            errorMessage,
            reportedBillingAmount,
            responseJson);
    }

    private static ProviderHttpException NormalizeHttpError(HttpStatusCode statusCode, string responseJson)
    {
        var upstream = ProviderHttpException.FromResponse(ProviderCodes.Kling, statusCode, responseJson);
        var diagnostic = $"{upstream.Code} {upstream.Message}".ToLowerInvariant();
        var (code, message) = statusCode switch
        {
            HttpStatusCode.TooManyRequests =>
                ("kling_rate_limited", "Kling đang giới hạn tần suất. Vui lòng thử lại sau."),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                ("kling_credential_invalid", "Kết nối Kling chưa sẵn sàng. Hãy liên hệ quản trị viên."),
            HttpStatusCode.BadRequest when ContainsAny(diagnostic, "moderation", "policy", "safety") =>
                ("kling_moderation_blocked", "Nội dung cảnh không vượt qua kiểm duyệt của Kling."),
            HttpStatusCode.BadRequest when ContainsAny(diagnostic, "native audio", "native_audio", "audio") =>
                ("kling_native_audio_unsupported", "Model Kling hiện tại không chấp nhận Native Audio cho yêu cầu này."),
            HttpStatusCode.BadRequest =>
                ("kling_invalid_request", "Kling không chấp nhận cấu hình hoặc nội dung của cảnh."),
            _ when ContainsAny(diagnostic, "balance", "quota", "billing", "credit") =>
                ("kling_quota_exhausted", "Tài khoản Kling tạm thời không đủ hạn mức."),
            _ =>
                ("kling_unavailable", "Kling đang tạm thời gián đoạn. Vui lòng thử lại sau.")
        };
        return new ProviderHttpException(ProviderCodes.Kling, code, message, statusCode: statusCode);
    }

    private static ProviderHttpException NormalizeApiError(int upstreamCode, string? upstreamMessage)
    {
        var diagnostic = $"{upstreamCode} {upstreamMessage}".ToLowerInvariant();
        var (code, message) = ContainsAny(diagnostic, "balance", "quota", "billing", "credit")
            ? ("kling_quota_exhausted", "Tài khoản Kling tạm thời không đủ hạn mức.")
            : ContainsAny(diagnostic, "moderation", "policy", "safety")
                ? ("kling_moderation_blocked", "Nội dung cảnh không vượt qua kiểm duyệt của Kling.")
                : ("kling_api_error", "Kling từ chối yêu cầu. Vui lòng kiểm tra cảnh và thử lại.");
        return new ProviderHttpException(ProviderCodes.Kling, code, message);
    }

    private static (string Code, string Message) NormalizeTaskFailure(string? upstreamMessage)
    {
        var diagnostic = upstreamMessage?.ToLowerInvariant() ?? string.Empty;
        if (ContainsAny(diagnostic, "moderation", "policy", "safety"))
        {
            return ("kling_moderation_blocked", "Nội dung cảnh không vượt qua kiểm duyệt của Kling.");
        }
        if (ContainsAny(diagnostic, "native audio", "native_audio", "audio"))
        {
            return ("kling_native_audio_unsupported", "Kling không thể tạo Native Audio cho cảnh này.");
        }
        return ("kling_generation_failed", "Kling không thể tạo video cho cảnh này.");
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static decimal? ExtractReportedBillingAmount(JsonElement task)
    {
        if (!task.TryGetProperty("billing", out var billing))
        {
            return null;
        }

        var items = billing.ValueKind == JsonValueKind.Array
            ? billing.EnumerateArray().ToArray()
            : [billing];
        decimal total = 0;
        var found = false;
        foreach (var item in items)
        {
            foreach (var name in new[] { "amount", "cost", "total" })
            {
                if (!item.TryGetProperty(name, out var value))
                {
                    continue;
                }

                if (TryReadDecimal(value, out var number))
                {
                    total += number;
                    found = true;
                    break;
                }
            }
        }
        return found ? Math.Max(0, total) : null;
    }

    private static bool TryReadDecimal(JsonElement value, out decimal number)
    {
        number = 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDecimal(out number),
            JsonValueKind.String => decimal.TryParse(
                value.GetString(),
                NumberStyles.Number | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out number),
            _ => false
        };
    }

    private static string? ExtractVideoUrl(JsonElement task)
    {
        if (!task.TryGetProperty("outputs", out var outputs) || outputs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var output in outputs.EnumerateArray())
        {
            if (GetString(output, "type") == "video")
            {
                var url = GetString(output, "url");
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                {
                    return uri.AbsoluteUri;
                }
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName)
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

    private static ProviderHttpException InvalidResponse(string message) =>
        new(ProviderCodes.Kling, "kling_invalid_response", message);
}

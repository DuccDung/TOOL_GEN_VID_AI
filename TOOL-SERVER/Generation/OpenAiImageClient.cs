using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TOOL_SERVER.Generation;

internal sealed record OpenAiImageResult(
    ValidatedGeneratedImage Image,
    long InputTokens,
    long OutputTokens,
    string ProviderRequestId);

internal interface IOpenAiImageClient
{
    Task<OpenAiImageResult> GenerateAsync(
        ProviderRuntimeConfiguration provider,
        string prompt,
        CancellationToken cancellationToken);
}

internal sealed class OpenAiImageClient(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiImageOptions> options) : IOpenAiImageClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenAiImageOptions _options = ValidateOptions(options.Value);

    public async Task<OpenAiImageResult> GenerateAsync(
        ProviderRuntimeConfiguration provider,
        string prompt,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(provider.ProviderCode, ProviderCodes.OpenAi, StringComparison.Ordinal) ||
            !string.Equals(provider.ModelCode, "gpt-image-2", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Runtime tạo ảnh phải dùng đúng openai/gpt-image-2.");
        }
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 8_000)
        {
            throw new ArgumentException("Prompt ảnh nhân vật do server tạo không hợp lệ.", nameof(prompt));
        }

        var requestBody = new
        {
            model = "gpt-image-2",
            prompt,
            n = 1,
            size = "1024x1024",
            quality = _options.Quality,
            output_format = "png"
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.BaseUri, "images/generations"))
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions)
        };
        OpenAiContentClient.ApplyAuthentication(request, provider);

        using var response = await httpClientFactory.CreateClient("OpenAiRuntime")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseJson = await ReadLimitedTextAsync(response.Content, MaximumResponseBytes(), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw NormalizeProviderError(response.StatusCode, responseJson);
        }

        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array ||
                data.GetArrayLength() != 1 ||
                !data[0].TryGetProperty("b64_json", out var base64Element) ||
                base64Element.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(base64Element.GetString()))
            {
                throw InvalidResponse("openai_image_missing_output", "OpenAI không trả về đúng một ảnh Base64.");
            }

            var base64 = base64Element.GetString()!;
            if (base64.Length > MaximumBase64Characters())
            {
                throw InvalidResponse("openai_image_size_invalid", "Ảnh Base64 của OpenAI vượt quá giới hạn dung lượng.");
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException exception)
            {
                throw new ProviderHttpException(
                    ProviderCodes.OpenAi,
                    "openai_image_base64_invalid",
                    "OpenAI trả về ảnh Base64 không hợp lệ.",
                    exception);
            }

            var usage = root.TryGetProperty("usage", out var usageElement) ? usageElement : default;
            var requestId = response.Headers.TryGetValues("x-request-id", out var values)
                ? values.FirstOrDefault() ?? string.Empty
                : string.Empty;
            return new OpenAiImageResult(
                GeneratedImageValidator.ValidatePng(bytes, _options.MaximumBytes),
                ReadUsage(usage, "input_tokens"),
                ReadUsage(usage, "output_tokens"),
                requestId);
        }
        catch (ProviderHttpException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_image_invalid_response",
                "OpenAI trả về dữ liệu tạo ảnh không hợp lệ.",
                exception);
        }
    }

    private static async Task<string> ReadLimitedTextAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0 && content.Headers.ContentLength > maximumBytes)
        {
            throw InvalidResponse("openai_image_response_too_large", "Phản hồi tạo ảnh của OpenAI vượt quá giới hạn cho phép.");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream(Math.Min(maximumBytes, 1024 * 1024));
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (destination.Length + read > maximumBytes)
            {
                throw InvalidResponse("openai_image_response_too_large", "Phản hồi tạo ảnh của OpenAI vượt quá giới hạn cho phép.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return Encoding.UTF8.GetString(destination.GetBuffer(), 0, checked((int)destination.Length));
    }

    private ProviderHttpException NormalizeProviderError(HttpStatusCode statusCode, string responseJson)
    {
        var providerError = ProviderHttpException.FromResponse(ProviderCodes.OpenAi, statusCode, responseJson);
        var diagnostic = $"{providerError.Code} {providerError.Message}";
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_image_rate_limited",
                "OpenAI đang giới hạn tần suất tạo ảnh. Vui lòng thử lại sau.",
                statusCode: statusCode);
        }
        if (statusCode == HttpStatusCode.Forbidden &&
            (diagnostic.Contains("verification", StringComparison.OrdinalIgnoreCase) ||
             diagnostic.Contains("organization", StringComparison.OrdinalIgnoreCase)))
        {
            return new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_organization_verification_required",
                "Tổ chức OpenAI cần hoàn tất xác minh trước khi dùng GPT-Image-2.",
                statusCode: statusCode);
        }
        if (statusCode == HttpStatusCode.BadRequest &&
            (diagnostic.Contains("moderation", StringComparison.OrdinalIgnoreCase) ||
             diagnostic.Contains("safety", StringComparison.OrdinalIgnoreCase) ||
             diagnostic.Contains("policy", StringComparison.OrdinalIgnoreCase)))
        {
            return new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_image_moderation_blocked",
                "Yêu cầu tạo ảnh không đáp ứng chính sách an toàn của OpenAI.",
                statusCode: statusCode);
        }
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_image_permission_denied",
                "Credential OpenAI hiện không có quyền dùng GPT-Image-2.",
                statusCode: statusCode);
        }
        if (diagnostic.Contains("balance", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("billing", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("credit", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_image_billing_unavailable",
                "Dịch vụ tạo ảnh OpenAI đang tạm thời không sẵn sàng.",
                statusCode: statusCode);
        }

        // Không chuyển tiếp message thô của provider vì nó có thể lặp lại một
        // phần prompt. Chỉ giữ mã lỗi đã chuẩn hóa và HTTP status để chẩn đoán.
        return new ProviderHttpException(
            ProviderCodes.OpenAi,
            providerError.Code,
            $"OpenAI từ chối yêu cầu tạo ảnh, HTTP {(int)statusCode}.",
            statusCode: statusCode);
    }

    private int MaximumResponseBytes() => checked(MaximumBase64Characters() + 512 * 1024);

    private int MaximumBase64Characters() => checked(((_options.MaximumBytes + 2) / 3 * 4) + 16);

    private static long ReadUsage(JsonElement usage, string propertyName) =>
        usage.ValueKind == JsonValueKind.Object &&
        usage.TryGetProperty(propertyName, out var value) &&
        value.TryGetInt64(out var result)
            ? Math.Max(0, result)
            : 0;

    private static ProviderHttpException InvalidResponse(string code, string message) =>
        new(ProviderCodes.OpenAi, code, message);

    private static OpenAiImageOptions ValidateOptions(OpenAiImageOptions options)
    {
        options.Validate();
        return options;
    }
}

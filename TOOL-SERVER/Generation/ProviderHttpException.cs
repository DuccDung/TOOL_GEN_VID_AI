using System.Net;
using System.Text.Json;

namespace TOOL_SERVER.Generation;

internal sealed class ProviderHttpException(
    string providerCode,
    string code,
    string message,
    Exception? innerException = null,
    HttpStatusCode? statusCode = null,
    IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message, innerException)
{
    public string ProviderCode { get; } = providerCode;

    public string Code { get; } = code;

    public HttpStatusCode? StatusCode { get; } = statusCode;

    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;

    public static ProviderHttpException FromResponse(
        string providerCode,
        HttpStatusCode statusCode,
        string responseJson)
    {
        var fallback = $"{providerCode} từ chối yêu cầu, HTTP {(int)statusCode}.";
        var message = fallback;
        var code = $"{providerCode}_http_{(int)statusCode}";
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                message = GetString(error, "message") ?? fallback;
                code = GetString(error, "code") ?? code;
            }
            else
            {
                message = GetString(root, "message") ?? fallback;
                code = GetString(root, "code") ?? code;
            }
        }
        catch (JsonException)
        {
            // The status code remains enough to produce a safe diagnostic.
        }

        return new ProviderHttpException(
            providerCode,
            NormalizeCode(providerCode, code),
            SafeMessage(message),
            statusCode: statusCode);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
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

    private static string NormalizeCode(string providerCode, string code)
    {
        var safe = new string(code
            .ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_')
            .Take(80)
            .ToArray()).Trim('_');
        return string.IsNullOrWhiteSpace(safe) ? $"{providerCode}_error" : $"{providerCode}_{safe}";
    }

    private static string SafeMessage(string message) =>
        message.Length <= 1000 ? message : message[..1000];
}

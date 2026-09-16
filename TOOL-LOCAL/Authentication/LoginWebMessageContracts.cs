using System.Net.Mail;
using System.Text.Json;

namespace TOOL_LOCAL.Authentication;

internal sealed record LoginWebRequest(string Type, string RequestId, JsonElement Payload);

internal sealed record LoginWebViewState(
    bool Busy,
    string Status,
    bool Error,
    string? EmailError = null,
    string? PasswordError = null,
    string? FocusField = null);

internal static class LoginWebMessageContracts
{
    public const string HostName = "auth.app.local";
    private const int MaximumMessageLength = 8192;
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static bool IsTrustedPage(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        uri.Host.Equals(HostName, StringComparison.OrdinalIgnoreCase) &&
        uri.Port == 443 &&
        uri.AbsolutePath.Equals("/login.html", StringComparison.Ordinal);

    public static bool TryRead(string? source, string? json, out LoginWebRequest? request)
    {
        request = null;
        if (!IsTrustedPage(source) || string.IsNullOrWhiteSpace(json) || json.Length > MaximumMessageLength)
            return false;

        try
        {
            request = JsonSerializer.Deserialize<LoginWebRequest>(json, WebJson);
            if (request is null || string.IsNullOrWhiteSpace(request.Type) ||
                !Guid.TryParse(request.RequestId, out _))
            {
                request = null;
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            request = null;
            return false;
        }
    }

    public static bool TryReadLogin(JsonElement payload, out string email, out string password,
        out bool rememberMe, out LoginWebViewState? error)
    {
        email = string.Empty;
        password = string.Empty;
        rememberMe = true;
        error = null;

        if (payload.ValueKind != JsonValueKind.Object ||
            !TryReadString(payload, "email", 320, out email) ||
            !TryReadString(payload, "password", 1024, out password) ||
            !payload.TryGetProperty("rememberMe", out var rememberElement) ||
            rememberElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            error = new LoginWebViewState(false, "Thông tin đăng nhập không hợp lệ.", true);
            return false;
        }

        email = email.Trim();
        rememberMe = rememberElement.GetBoolean();
        string? emailError = null;
        string? passwordError = null;
        if (string.IsNullOrWhiteSpace(email)) emailError = "Vui lòng nhập email.";
        else if (!MailAddress.TryCreate(email, out var address) ||
                 !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
            emailError = "Email không đúng định dạng.";
        if (password.Length == 0) passwordError = "Vui lòng nhập mật khẩu.";

        if (emailError is null && passwordError is null) return true;
        error = new LoginWebViewState(false, "Vui lòng kiểm tra lại thông tin đăng nhập.", true,
            emailError, passwordError, emailError is not null ? "email" : "password");
        return false;
    }

    public static bool TryReadString(JsonElement payload, string propertyName, int maximumLength,
        out string value)
    {
        value = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.String)
            return false;

        value = element.GetString() ?? string.Empty;
        return value.Length <= maximumLength;
    }
}

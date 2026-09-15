using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_LOCAL.TikTok;

internal sealed class TikTokOAuthCoordinator(ITikTokGatewayClient gatewayClient)
{
    public async Task<TikTokFeatureStateResponse> ConnectAsync(CancellationToken cancellationToken, Guid? targetConnectionId = null)
    {
        var verifier = CreateVerifier();
        var challenge = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).ToLowerInvariant();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirectUri = $"http://127.0.0.1:{port}/callback/";
            var started = await gatewayClient.StartOAuthAsync(
                new StartTikTokOAuthRequest(redirectUri, challenge, targetConnectionId, MultiAccount: true),
                cancellationToken);
            OpenSystemBrowser(started.AuthorizationUrl);
            var callback = await ReceiveCallbackAsync(listener, started.State, cancellationToken);
            if (!string.IsNullOrWhiteSpace(callback.Error))
            {
                throw new TikTokDesktopException(
                    "tiktok_oauth_denied",
                    string.IsNullOrWhiteSpace(callback.ErrorDescription)
                        ? "Bạn chưa cấp quyền cho ứng dụng trên TikTok."
                        : callback.ErrorDescription);
            }
            if (string.IsNullOrWhiteSpace(callback.Code))
            {
                throw new TikTokDesktopException("tiktok_oauth_code_missing", "TikTok không trả về mã ủy quyền.");
            }
            return await gatewayClient.CompleteOAuthAsync(
                new CompleteTikTokOAuthRequest(
                    started.OAuthSessionId,
                    callback.Code,
                    callback.State,
                    verifier),
                cancellationToken);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string CreateVerifier()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
        var bytes = RandomNumberGenerator.GetBytes(64);
        var chars = new char[64];
        for (var index = 0; index < chars.Length; index++)
        {
            chars[index] = alphabet[bytes[index] % alphabet.Length];
        }
        return new string(chars);
    }

    private static void OpenSystemBrowser(string authorizationUrl)
    {
        if (!Uri.TryCreate(authorizationUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.Port != 443 ||
            !uri.Host.Equals("www.tiktok.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Equals("/v2/auth/authorize/", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new TikTokDesktopException("tiktok_authorization_url_invalid", "Địa chỉ đăng nhập TikTok không hợp lệ.");
        }
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new TikTokDesktopException("tiktok_browser_open_failed", "Không thể mở trình duyệt để kết nối TikTok.", exception);
        }
    }

    private static async Task<OAuthCallback> ReceiveCallbackAsync(
        TcpListener listener,
        string expectedState,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(timeout.Token);
            if (string.IsNullOrWhiteSpace(requestLine) || requestLine.Length > 8192)
            {
                await WriteBrowserResponseAsync(stream, false, timeout.Token);
                throw new TikTokDesktopException("tiktok_oauth_callback_invalid", "Phản hồi đăng nhập TikTok không hợp lệ.");
            }
            for (var index = 0; index < 64; index++)
            {
                var header = await reader.ReadLineAsync(timeout.Token);
                if (string.IsNullOrEmpty(header)) break;
                if (header.Length > 8192)
                    throw new TikTokDesktopException("tiktok_oauth_callback_invalid", "Phản hồi đăng nhập TikTok không hợp lệ.");
            }
            var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[0].Equals("GET", StringComparison.Ordinal))
            {
                await WriteBrowserResponseAsync(stream, false, timeout.Token);
                throw new TikTokDesktopException("tiktok_oauth_callback_invalid", "Phản hồi đăng nhập TikTok không hợp lệ.");
            }
            var uri = new Uri("http://127.0.0.1" + parts[1]);
            if (!uri.AbsolutePath.Equals("/callback/", StringComparison.Ordinal))
            {
                await WriteBrowserResponseAsync(stream, false, timeout.Token);
                throw new TikTokDesktopException("tiktok_oauth_callback_invalid", "Đường dẫn phản hồi TikTok không hợp lệ.");
            }
            var query = ParseQuery(uri.Query);
            var state = query.GetValueOrDefault("state") ?? string.Empty;
            if (!FixedEquals(expectedState, state))
            {
                await WriteBrowserResponseAsync(stream, false, timeout.Token);
                throw new TikTokDesktopException("tiktok_oauth_state_mismatch", "Mã bảo vệ phiên TikTok không khớp.");
            }
            var callback = new OAuthCallback(
                query.GetValueOrDefault("code"),
                state,
                query.GetValueOrDefault("error"),
                query.GetValueOrDefault("error_description"));
            await WriteBrowserResponseAsync(stream, string.IsNullOrWhiteSpace(callback.Error), timeout.Token);
            return callback;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TikTokDesktopException("tiktok_oauth_timeout", "Đã hết thời gian chờ kết nối TikTok.");
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = Uri.UnescapeDataString((separator < 0 ? pair : pair[..separator]).Replace('+', ' '));
            var value = Uri.UnescapeDataString((separator < 0 ? string.Empty : pair[(separator + 1)..]).Replace('+', ' '));
            if (key.Length <= 100 && value.Length <= 4096) result[key] = value;
        }
        return result;
    }

    private static bool FixedEquals(string expected, string actual)
    {
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static async Task WriteBrowserResponseAsync(Stream stream, bool success, CancellationToken cancellationToken)
    {
        var title = success ? "Đã kết nối TikTok" : "Không thể kết nối TikTok";
        var detail = success ? "Bạn có thể đóng cửa sổ này và quay lại VideoMaker." : "Hãy quay lại VideoMaker và thử lại.";
        var body = $"<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><title>{title}</title>" +
                   $"<body style=\"font-family:Segoe UI,sans-serif;padding:40px\"><h1>{title}</h1><p>{detail}</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(body);
        var headers = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private sealed record OAuthCallback(string? Code, string State, string? Error, string? ErrorDescription);
}

internal sealed class TikTokDesktopException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

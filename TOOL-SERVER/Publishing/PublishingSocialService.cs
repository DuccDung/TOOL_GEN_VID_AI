using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Data;
using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Publishing;

namespace TOOL_SERVER.Publishing;

internal sealed record PublishingTokens(string AccessToken, string? RefreshToken, DateTime ExpiresAtUtc, string ClientFingerprint);

internal sealed class PublishingSocialService(PublishingDbContext db, AccountDbContext accounts,
    IGenerationAccessService access, IDataProtectionProvider protection, IHttpClientFactory clients,
    IOptions<PublishingOptions> options, TimeProvider time)
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;
    private PublishingOAuthOptions Settings(string platform) => platform switch
    {
        "YouTube" => options.Value.YouTube,
        "Facebook" => options.Value.Facebook,
        _ => throw PublishingCalendar.Error("publishing_platform_invalid", "Nền tảng không hợp lệ.")
    };
    internal IReadOnlyList<PublishingPlatformReadiness> Readiness() =>
    [new("TikTok", true, "Kết nối tại mục Đăng TikTok. Mỗi video cần xem trước và xác nhận đăng."),
     new("YouTube", Configured(options.Value.YouTube, false), Configured(options.Value.YouTube, false) ? null : "Quản trị viên cần cấu hình ứng dụng Google OAuth và YouTube Data API."),
     new("Facebook", Configured(options.Value.Facebook, true), Configured(options.Value.Facebook, true) ? null : "Quản trị viên cần cấu hình ứng dụng Meta và quyền đăng lên Facebook Page.")];

    private static bool Configured(PublishingOAuthOptions settings, bool facebook) => settings.Enabled &&
        !string.IsNullOrWhiteSpace(settings.ClientId) && !string.IsNullOrWhiteSpace(settings.ClientSecret) &&
        Uri.TryCreate(settings.RedirectUri, UriKind.Absolute, out var redirect) && redirect.Scheme == "https" && redirect.Port == 443 &&
        string.IsNullOrEmpty(redirect.UserInfo) && string.IsNullOrEmpty(redirect.Query) && string.IsNullOrEmpty(redirect.Fragment) &&
        (!facebook || Regex.IsMatch(settings.ApiVersion ?? "", @"^v\d{2}\.0$"));

    internal PublishingOAuthOptions RequireConfigured(string platform, bool publicPosting = false)
    {
        var settings = Settings(platform);
        if (!Configured(settings, platform == "Facebook")) throw PublishingCalendar.Error("publishing_platform_not_configured", $"Kết nối {platform} chưa được quản trị viên cấu hình.", 409);
        if (publicPosting && !settings.PublicPostingApproved) throw PublishingCalendar.Error("publishing_public_not_approved", $"Ứng dụng {platform} chưa được xác nhận đủ quyền đăng công khai.", 409);
        return settings;
    }

    public async Task<StartPublishingOAuthResponse> StartAsync(StartPublishingOAuthRequest request, string user, Guid device, Guid session, CancellationToken ct)
    {
        await access.RequireAsync(user, device, request.OrganizationId, null, ct);
        var settings = RequireConfigured(request.Platform);
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var row = new PublishingOAuthSession { OAuthSessionId = Guid.NewGuid(), UserId = user, DeviceId = device, SessionId = session,
            OrganizationId = request.OrganizationId, Platform = request.Platform, StateHash = PublishingService.Hash(state),
            ProtectedVerifier = protection.CreateProtector("VideoMaker.Publishing.OAuth.v1").Protect(verifier), ExpiresAtUtc = Now.AddMinutes(10) };
        var expired = await db.OAuthSessions.Where(x => x.UserId == user && x.ExpiresAtUtc <= Now).ToListAsync(ct);
        db.OAuthSessions.RemoveRange(expired);
        if (await db.OAuthSessions.CountAsync(x => x.UserId == user && !x.Consumed && x.ExpiresAtUtc > Now, ct) >= 5)
            throw PublishingCalendar.Error("publishing_oauth_pending", "Hoàn tất cửa sổ kết nối đang mở hoặc chờ phiên cũ hết hạn.", 409);
        db.OAuthSessions.Add(row); await db.SaveChangesAsync(ct);
        var parameters = new Dictionary<string, string?> { ["client_id"] = settings.ClientId, ["redirect_uri"] = settings.RedirectUri,
            ["response_type"] = "code", ["state"] = state };
        string url;
        if (request.Platform == "YouTube")
        {
            url = "https://accounts.google.com/o/oauth2/v2/auth";
            parameters["scope"] = "https://www.googleapis.com/auth/youtube.upload https://www.googleapis.com/auth/youtube.readonly";
            parameters["access_type"] = "offline"; parameters["prompt"] = "consent select_account";
            parameters["code_challenge"] = challenge; parameters["code_challenge_method"] = "S256";
        }
        else
        {
            url = $"https://www.facebook.com/{settings.ApiVersion}/dialog/oauth";
            parameters["scope"] = "pages_show_list,pages_read_engagement,pages_manage_posts";
        }
        return new(url + "?" + string.Join('&', parameters.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value!))));
    }

    public async Task CompleteAsync(string state, string code, CancellationToken ct)
    {
        if (state is null || state.Length != 64 || !state.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(code) || code.Length > 8192)
            throw PublishingCalendar.Error("publishing_oauth_invalid", "Phiên kết nối không hợp lệ.");
        var hash = PublishingService.Hash(state);
        var row = await db.OAuthSessions.SingleOrDefaultAsync(x => x.StateHash == hash && !x.Consumed && x.ExpiresAtUtc > Now, ct)
            ?? throw PublishingCalendar.Error("publishing_oauth_expired", "Phiên kết nối đã hết hạn hoặc đã sử dụng.", 409);
        var active = await accounts.UserSessions.AnyAsync(x => x.SessionId == row.SessionId && x.UserId == row.UserId &&
            x.DeviceId == row.DeviceId && x.Status == "Active" && x.RevokedAtUtc == null && x.AbsoluteExpiresAtUtc > Now &&
            !x.Device!.IsRevoked && x.User.AccountStatus == "Active" && x.User.DeletedAtUtc == null, ct);
        if (!active) throw PublishingCalendar.Error("publishing_session_expired", "Phiên đăng nhập không còn hiệu lực.", 403);
        await access.RequireAsync(row.UserId, row.DeviceId, row.OrganizationId, null, ct);
        var settings = RequireConfigured(row.Platform);
        row.Consumed = true; await db.SaveChangesAsync(ct); // Consume before exchanging a single-use authorization code.
        using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = settings.ClientId!, ["client_secret"] = settings.ClientSecret!,
            ["code"] = code, ["redirect_uri"] = settings.RedirectUri!, ["grant_type"] = "authorization_code",
            ["code_verifier"] = protection.CreateProtector("VideoMaker.Publishing.OAuth.v1").Unprotect(row.ProtectedVerifier) });
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, row.Platform == "YouTube" ? "https://oauth2.googleapis.com/token" : $"https://graph.facebook.com/{settings.ApiVersion}/oauth/access_token") { Content = body };
        using var token = await JsonAsync(tokenRequest, ct);
        var accessToken = token.RootElement.GetProperty("access_token").GetString()!;
        var refresh = token.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expires = token.RootElement.TryGetProperty("expires_in", out var exp) ? Math.Clamp(exp.GetInt32(), 1, 90 * 86400) : 3600;
        if (row.Platform == "YouTube")
        {
            var scopes = token.RootElement.TryGetProperty("scope", out var granted) ? (granted.GetString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
            if (!scopes.Contains("https://www.googleapis.com/auth/youtube.upload") || !scopes.Contains("https://www.googleapis.com/auth/youtube.readonly"))
                throw PublishingCalendar.Error("publishing_scope_missing", "Google chưa cấp đủ quyền tải video và đọc kênh. Hãy kết nối lại và chọn đủ quyền.", 409);
            if (string.IsNullOrWhiteSpace(refresh)) throw PublishingCalendar.Error("publishing_offline_access_missing", "Google chưa cấp quyền truy cập ngoại tuyến. Hãy kết nối lại và chấp nhận quyền.", 409);
            using var channel = Authorized(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true", accessToken);
            using var response = await JsonAsync(channel, ct);
            var channels = response.RootElement.GetProperty("items").EnumerateArray().ToArray();
            if (channels.Length != 1) throw PublishingCalendar.Error("publishing_channel_missing", "Tài khoản Google chưa có kênh YouTube phù hợp.", 409);
            await SaveConnectionAsync(row.UserId, "YouTube", channels[0].GetProperty("id").GetString()!,
                channels[0].GetProperty("snippet").GetProperty("title").GetString()!, new(accessToken, refresh, Now.AddSeconds(expires), Fingerprint(settings)), ct);
        }
        else
        {
            // Exchange the short-lived user token before deriving Page tokens for future dates.
            using var exchange = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/{settings.ApiVersion}/oauth/access_token")
            { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "fb_exchange_token", ["client_id"] = settings.ClientId!,
                ["client_secret"] = settings.ClientSecret!, ["fb_exchange_token"] = accessToken }) };
            using var extended = await JsonAsync(exchange, ct);
            accessToken = extended.RootElement.GetProperty("access_token").GetString()!;
            expires = extended.RootElement.TryGetProperty("expires_in", out var lifetime) ? Math.Clamp(lifetime.GetInt32(), 1, 60 * 86400) : expires;
            using var permissionsRequest = Authorized(HttpMethod.Get, $"https://graph.facebook.com/{settings.ApiVersion}/me/permissions", accessToken);
            using var permissions = await JsonAsync(permissionsRequest, ct);
            var granted = permissions.RootElement.GetProperty("data").EnumerateArray()
                .Where(x => x.GetProperty("status").GetString() == "granted").Select(x => x.GetProperty("permission").GetString()).ToHashSet();
            if (!new[] { "pages_show_list", "pages_read_engagement", "pages_manage_posts" }.All(granted.Contains))
                throw PublishingCalendar.Error("publishing_scope_missing", "Facebook chưa cấp đủ quyền đọc Page và đăng bài. Hãy kết nối lại và chọn đủ quyền.", 409);
            // Page tokens are derived from the user's granted Pages; no arbitrary page/token input is accepted.
            using var pagesRequest = Authorized(HttpMethod.Get, $"https://graph.facebook.com/{settings.ApiVersion}/me/accounts?fields=id,name,access_token,tasks&limit=100", accessToken);
            using var pages = await JsonAsync(pagesRequest, ct);
            var count = 0;
            foreach (var page in pages.RootElement.GetProperty("data").EnumerateArray())
            {
                if (!page.TryGetProperty("access_token", out var pageToken) || !page.TryGetProperty("tasks", out var tasks) ||
                    !tasks.EnumerateArray().Any(x => x.GetString() is "CREATE_CONTENT" or "MANAGE")) continue;
                await SaveConnectionAsync(row.UserId, "Facebook", page.GetProperty("id").GetString()!, page.GetProperty("name").GetString()!,
                    new(pageToken.GetString()!, null, Now.AddSeconds(expires), Fingerprint(settings)), ct);
                count++;
            }
            if (count == 0) throw PublishingCalendar.Error("publishing_pages_missing", "Không có Facebook Page được cấp quyền tạo nội dung. Hãy kiểm tra quyền kết nối.", 409);
        }
    }

    private async Task SaveConnectionAsync(string user, string platform, string externalId, string name, PublishingTokens tokens, CancellationToken ct)
    {
        if (!Regex.IsMatch(externalId, platform == "Facebook" ? @"^[0-9]{1,150}$" : @"^[a-zA-Z0-9_-]{1,150}$") || name.Length > 200)
            throw PublishingCalendar.Error("publishing_identity_invalid", "Thông tin tài khoản không hợp lệ.");
        var row = await db.Connections.SingleOrDefaultAsync(x => x.UserId == user && x.Platform == platform && x.ExternalId == externalId, ct);
        if (row is null) { row = new() { ConnectionId = Guid.NewGuid(), UserId = user, Platform = platform, ExternalId = externalId }; db.Connections.Add(row); }
        row.DisplayName = name; row.Status = "Connected"; row.UpdatedAtUtc = Now;
        row.ProtectedTokens = Protector(row).Protect(PublishingService.Write(tokens));
        await db.SaveChangesAsync(ct);
    }

    public async Task DisconnectAsync(Guid id, string user, CancellationToken ct)
    {
        var connection = await db.Connections.SingleOrDefaultAsync(x => x.ConnectionId == id && x.UserId == user, ct)
            ?? throw PublishingCalendar.Error("publishing_connection_missing", "Không tìm thấy kết nối của tài khoản.", 404);
        connection.Status = "Disconnected"; connection.ProtectedTokens = ""; connection.UpdatedAtUtc = Now;
        await db.SaveChangesAsync(ct);
    }

    internal async Task<(PublishingConnection Connection, string Token)> TokenAsync(Guid id, string user, string platform, CancellationToken ct)
    {
        var settings = RequireConfigured(platform);
        var connection = await db.Connections.SingleOrDefaultAsync(x => x.ConnectionId == id && x.UserId == user && x.Platform == platform && x.Status == "Connected", ct)
            ?? throw PublishingCalendar.Error("publishing_connection_missing", "Kết nối đã bị ngắt. Hãy kết nối lại.", 409);
        var tokens = PublishingService.Read<PublishingTokens>(Protector(connection).Unprotect(connection.ProtectedTokens));
        if (tokens.ClientFingerprint != Fingerprint(settings)) throw PublishingCalendar.Error("publishing_app_changed", "Ứng dụng kết nối đã thay đổi. Hãy kết nối lại tài khoản.", 409);
        if (tokens.ExpiresAtUtc > Now.AddMinutes(5)) return (connection, tokens.AccessToken);
        if (platform != "YouTube" || string.IsNullOrEmpty(tokens.RefreshToken))
            throw PublishingCalendar.Error("publishing_reconnect_required", "Quyền đăng đã hết hạn. Hãy kết nối lại tài khoản.", 409);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = settings.ClientId!, ["client_secret"] = settings.ClientSecret!, ["grant_type"] = "refresh_token", ["refresh_token"] = tokens.RefreshToken }) };
        using var response = await JsonAsync(request, ct);
        tokens = tokens with { AccessToken = response.RootElement.GetProperty("access_token").GetString()!,
            ExpiresAtUtc = Now.AddSeconds(Math.Clamp(response.RootElement.GetProperty("expires_in").GetInt32(), 1, 86400)) };
        connection.ProtectedTokens = Protector(connection).Protect(PublishingService.Write(tokens)); connection.UpdatedAtUtc = Now;
        await db.SaveChangesAsync(ct); return (connection, tokens.AccessToken);
    }

    private static string Fingerprint(PublishingOAuthOptions settings) => PublishingService.Hash(settings.ClientId + "\n" + settings.RedirectUri);
    private IDataProtector Protector(PublishingConnection connection) => protection.CreateProtector("VideoMaker.Publishing.Tokens.v1", connection.UserId, connection.ConnectionId.ToString("N"), connection.Platform);
    internal static HttpRequestMessage Authorized(HttpMethod method, string url, string token)
    { var request = new HttpRequestMessage(method, url); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request; }
    internal async Task<JsonDocument> JsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await SendAsync(request, ct);
        return await ReadJsonAsync(response, ct);
    }
    internal static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode) throw PublishingCalendar.Error("publishing_platform_rejected", $"Nền tảng từ chối yêu cầu (HTTP {(int)response.StatusCode}). Kiểm tra quyền, hạn mức và kết nối.", 409);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(); var chunk = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        { if (buffer.Length + read > 1024 * 1024) throw PublishingCalendar.Error("publishing_response_invalid", "Phản hồi nền tảng vượt giới hạn.", 502); buffer.Write(chunk, 0, read); }
        return JsonDocument.Parse(buffer.ToArray());
    }
    internal Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ValidateUri(request.RequestUri!);
        return clients.CreateClient("publishing-platform").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }
    internal static void ValidateUri(Uri uri)
    {
        string[] hosts = ["oauth2.googleapis.com", "www.googleapis.com", "graph.facebook.com", "graph-video.facebook.com", "rupload.facebook.com",
            "open-upload.tiktokapis.com", "open-upload-sg.tiktokapis.com", "upload.us.tiktokapis.com"];
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Port != 443 || !hosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase) ||
            uri.UserInfo.Length > 0 || uri.Fragment.Length > 0 || uri.AbsoluteUri.Length > 8192)
            throw PublishingCalendar.Error("publishing_unsafe_url", "Địa chỉ nền tảng không hợp lệ.", 502);
    }
}

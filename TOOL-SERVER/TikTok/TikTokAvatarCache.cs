using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.TikTok.Data;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_SERVER.TikTok;

public sealed record TikTokAvatarContent(byte[] Bytes, string MimeType);

public sealed class TikTokAvatarCache(IHttpClientFactory clients) : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 16 * 1024 * 1024 });
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "lf16-tt4d.tiktokcdn.com", "p16-sign.tiktokcdn-us.com", "p19-sign.tiktokcdn-us.com",
        "p16-sign-va.tiktokcdn.com", "p16-sign-sg.tiktokcdn.com", "p19-sign-sg.tiktokcdn.com"
    };

    public static bool IsAllowedUrl(string? value) => value is { Length: > 0 and <= 4096 } &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) && Hosts.Contains(uri.IdnHost);

    public string? Register(string userId, Guid connectionId, string? url, ITikTokTokenProtector protector)
    {
        if (!IsAllowedUrl(url)) return null;
        _cache.Set((userId, connectionId, "source"), protector.ProtectAvatarUrl(userId, url!),
            new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(90)).SetSize(8192));
        return $"api/tiktok/connections/{connectionId:D}/avatar";
    }

    public async Task<TikTokAvatarContent> GetAsync(TikTokDbContext db, ITikTokTokenProtector protector,
        string userId, Guid connectionId, CancellationToken cancellationToken)
    {
        var account = await db.Connections.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId &&
            x.TikTokConnectionId == connectionId && x.RevokedAtUtc == null, cancellationToken)
            ?? throw new AccountApiException(404, "tiktok_avatar_not_found", "Không tìm thấy ảnh tài khoản.");
        var cacheKey = (userId, connectionId, account.UpdatedAtUtc);
        if (_cache.TryGetValue<TikTokAvatarContent>(cacheKey, out var cached)) return cached!;
        if (!_cache.TryGetValue<string>((userId, connectionId, "source"), out var protectedUrl))
            protectedUrl = account.AvatarExpiresAtUtc > DateTime.UtcNow ? account.ProtectedAvatarUrl : null;
        if (protectedUrl is null) throw new AccountApiException(404, "tiktok_avatar_not_found", "Ảnh tài khoản chưa sẵn sàng.");
        var url = protector.UnprotectAvatarUrl(userId, protectedUrl);
        if (!IsAllowedUrl(url)) throw new AccountApiException(404, "tiktok_avatar_not_found", "Ảnh tài khoản chưa sẵn sàng.");
        using var client = clients.CreateClient("TikTokAvatarDownload");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > TikTokAvatarValidation.MaximumBytes)
            throw new AccountApiException(404, "tiktok_avatar_unavailable", "Không tải được ảnh tài khoản.");
        var mime = response.Content.Headers.ContentType?.MediaType;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16384];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > TikTokAvatarValidation.MaximumBytes)
                throw new AccountApiException(404, "tiktok_avatar_unavailable", "Ảnh tài khoản quá lớn.");
            output.Write(buffer, 0, read);
        }
        var bytes = output.ToArray();
        if (!TikTokAvatarValidation.IsValid(bytes, mime))
            throw new AccountApiException(404, "tiktok_avatar_unavailable", "Ảnh tài khoản không hợp lệ.");
        var content = new TikTokAvatarContent(bytes, mime!);
        _cache.Set(cacheKey, content, new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(15)).SetSize(bytes.Length));
        return content;
    }

    public void Dispose() => _cache.Dispose();
}

using System.Net;
using Microsoft.AspNetCore.DataProtection;
using TOOL_LOCAL.TikTok;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.TikTok;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_TESTS.TikTok;

public sealed partial class TikTokServiceTests
{
    [Theory]
    [InlineData("https://127.0.0.1/avatar.png")]
    [InlineData("https://p16-sign.tiktokcdn-us.com.attacker.test/avatar.png")]
    [InlineData("https://p16-sign.tiktokcdn-us.com:8443/avatar.png")]
    [InlineData("https://user@p16-sign.tiktokcdn-us.com/avatar.png")]
    [InlineData("http://p16-sign.tiktokcdn-us.com/avatar.png")]
    public void Avatar_RejectsNonAllowlistedDestinations(string url) => Assert.False(TikTokAvatarCache.IsAllowedUrl(url));

    [Fact]
    public async Task Avatar_EnforcesOwnershipAndSizeAndServesOnlyValidatedBytes()
    {
        await using var db = CreateDb(); var account = Account(); db.Connections.Add(account); await db.SaveChangesAsync();
        var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0, 0 };
        using var factory = new AvatarHttpFactory(bytes, "image/png");
        using var cache = new TikTokAvatarCache(factory);
        var protector = new TikTokTokenProtector(new EphemeralDataProtectionProvider());
        var route = cache.Register("user-a", account.TikTokConnectionId, "https://p16-sign.tiktokcdn-us.com/image.png?signature=synthetic", protector);
        Assert.Equal($"api/tiktok/connections/{account.TikTokConnectionId:D}/avatar", route);
        await Assert.ThrowsAsync<AccountApiException>(() => cache.GetAsync(db, protector, "user-b", account.TikTokConnectionId, default));
        Assert.Equal(0, factory.Calls);
        Assert.Equal(bytes, (await cache.GetAsync(db, protector, "user-a", account.TikTokConnectionId, default)).Bytes);
        await cache.GetAsync(db, protector, "user-a", account.TikTokConnectionId, default);
        Assert.Equal(1, factory.Calls);
        Assert.False(TikTokAvatarValidation.IsValid(bytes, "text/html"));
        Assert.False(TikTokAvatarValidation.IsValid("<svg onload='alert(1)'/>"u8, "image/svg+xml"));
        var oversized = new byte[TikTokAvatarValidation.MaximumBytes + 1]; bytes.CopyTo(oversized, 0);
        Assert.False(TikTokAvatarValidation.IsValid(oversized, "image/png"));
        account.RevokedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AccountApiException>(() => cache.GetAsync(db, protector, "user-a", account.TikTokConnectionId, default));
        Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public void Avatar_DesktopVirtualRoutesBindExactAccountAndExpireWhenRemoved()
    {
        var media = new TikTokMediaService(null!, null!); var preview = new TikTokMediaPreviewService(media);
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var content = new TikTokDesktopAvatar([137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0, 0], "image/png");
        var urlA = media.SetAvatar(a, content); var urlB = media.SetAvatar(b, content);
        Assert.Equal(200, preview.Open(new Uri(urlA), "GET", null).StatusCode);
        Assert.Equal(404, preview.Open(new Uri(urlA.Replace(a.ToString("N"), b.ToString("N"))), "GET", null).StatusCode);
        Assert.NotEqual(200, preview.Open(new Uri(urlA.Replace(".local/", ".local:8443/")), "GET", null).StatusCode);
        media.RetainAvatars([b]);
        Assert.Equal(404, preview.Open(new Uri(urlA), "GET", null).StatusCode);
        Assert.Equal(200, preview.Open(new Uri(urlB), "HEAD", null).StatusCode);
        media.RetainAvatars([]); Assert.Null(media.GetAvatarUrl(b));
    }

    private sealed class AvatarHttpFactory(byte[] bytes, string mime) : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls { get; private set; }
        public HttpClient CreateClient(string name) { Assert.Equal("TikTokAvatarDownload", name); return new(this, false); }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Assert.Null(request.Headers.Authorization);
            var content = new ByteArrayContent(bytes); content.Headers.ContentType = new(mime);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}

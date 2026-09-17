using TOOL_LOCAL.TikTok;

namespace TOOL_TESTS.TikTok;

public sealed class TikTokDesktopAvatarTests
{
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
}

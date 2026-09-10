using System.Text.RegularExpressions;

namespace TOOL_LOCAL.Bilibili;

internal static partial class BilibiliUrl
{
    public static string Normalize(string? input, bool allowCollection = false)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 2048 || input.Any(char.IsControl)
            || !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || uri.UserInfo.Length != 0)
            throw Invalid();

        if (uri.Host is "www.bilibili.com" or "bilibili.com" or "m.bilibili.com")
        {
            var match = VideoPath().Match(uri.AbsolutePath);
            if (match.Success)
            {
                var part = System.Web.HttpUtility.ParseQueryString(uri.Query)["p"];
                if (part is not null && (!int.TryParse(part, out var number) || number < 1 || number > 10000))
                    throw Invalid();
                return $"https://www.bilibili.com/video/{match.Groups[1].Value}" + (part is null ? "" : $"?p={int.Parse(part)}");
            }
        }
        if (uri.Host == "space.bilibili.com")
        {
            var match = ChannelPath().Match(uri.AbsolutePath);
            if (match.Success) return $"https://space.bilibili.com/{match.Groups[1].Value}/video";
            if (allowCollection && CollectionPath().IsMatch(uri.AbsolutePath))
                return $"https://space.bilibili.com{uri.AbsolutePath.TrimEnd('/')}?type=season";
        }
        if (uri.Host == "b23.tv" && ShortPath().IsMatch(uri.AbsolutePath))
            return "https://b23.tv" + uri.AbsolutePath.TrimEnd('/');
        throw Invalid();
    }

    public static bool IsVideo(string url) => url.StartsWith("https://www.bilibili.com/video/", StringComparison.Ordinal);
    public static bool IsShort(string url) => url.StartsWith("https://b23.tv/", StringComparison.Ordinal);

    public static string? Thumbnail(string? input)
    {
        if (input?.StartsWith("//", StringComparison.Ordinal) == true) input = "https:" + input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0
            || (uri.Scheme != "https" && uri.Scheme != "http")
            || !uri.IsDefaultPort || uri.Host is not ("i0.hdslb.com" or "i1.hdslb.com" or "i2.hdslb.com")) return null;
        return new UriBuilder(uri) { Scheme = "https", Port = -1, Query = "", Fragment = "" }.Uri.AbsoluteUri;
    }

    private static BilibiliException Invalid() => new("bilibili_invalid_url",
        "Hãy nhập link HTTPS video bilibili.com/video/…, kênh space.bilibili.com/… hoặc link b23.tv.");

    [GeneratedRegex(@"^/video/(BV[A-Za-z0-9]{10}|av[0-9]{1,20})/?$", RegexOptions.CultureInvariant)]
    private static partial Regex VideoPath();
    [GeneratedRegex(@"^/([0-9]{1,20})(?:(?:/upload)?/video)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex ChannelPath();
    [GeneratedRegex(@"^/[0-9]{1,20}/lists/[0-9]{1,20}/?$", RegexOptions.CultureInvariant)]
    private static partial Regex CollectionPath();
    [GeneratedRegex(@"^/[A-Za-z0-9]{1,32}/?$", RegexOptions.CultureInvariant)]
    private static partial Regex ShortPath();
}

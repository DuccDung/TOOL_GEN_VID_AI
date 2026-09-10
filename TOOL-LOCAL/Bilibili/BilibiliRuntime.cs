using System.Net;

namespace TOOL_LOCAL.Bilibili;

internal interface IBilibiliRuntime
{
    string Version { get; }
    Task<bool> IsReadyAsync(CancellationToken token);
    Task InstallAsync(CancellationToken token);
    Task<string> RequireAsync(CancellationToken token);
}

internal sealed class BilibiliRuntime : IBilibiliRuntime
{
    public const string PinnedVersion = "2026.08.19";
    public const long Size = 17840399;
    public const string Sha256 = "66674953fe251b89f4d08c5f0e35e0728679bd67ab3d7d05c0562af101dd3e7a";
    private const string DownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/yt-dlp.exe";
    private readonly string _directory;
    private string Executable => Path.Combine(_directory, "yt-dlp.exe");
    public string Version => PinnedVersion;

    public BilibiliRuntime(string? root = null)
    {
        _directory = BilibiliFiles.RequireLocalDirectory(Path.Combine(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoMaker", "Bilibili", "runtime"), PinnedVersion));
    }

    public async Task<bool> IsReadyAsync(CancellationToken token)
    {
        if (!File.Exists(Executable) || new FileInfo(Executable).Length != Size) return false;
        return (await BilibiliFiles.HashAsync(Executable, token)).Equals(Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> RequireAsync(CancellationToken token) => await IsReadyAsync(token) ? Executable
        : throw new BilibiliException("bilibili_tool_missing", "Hãy bấm Chuẩn bị công cụ tải trước khi quét Bilibili.");

    public async Task InstallAsync(CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        token = linked.Token;
        if (await IsReadyAsync(token)) return;
        BilibiliFiles.RequireLocalDirectory(_directory);
        BilibiliFiles.RequireDiskSpace(_directory, Size * 2 + 20 * 1024 * 1024);
        Directory.CreateDirectory(_directory);
        var part = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".part");
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false })
            { Timeout = TimeSpan.FromMinutes(5) };
            var uri = new Uri(DownloadUrl);
            for (var redirect = 0; redirect <= 4; redirect++)
            {
                ValidateDownloadUri(uri);
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                if ((int)response.StatusCode is >= 300 and <= 399)
                {
                    uri = new Uri(uri, response.Headers.Location ?? throw new HttpRequestException());
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } length && length != Size)
                    throw new BilibiliException("bilibili_tool_checksum", "Kích thước công cụ tải không khớp bản đã xác minh.");
                await using (var input = await response.Content.ReadAsStreamAsync(token))
                await using (var output = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    var buffer = new byte[65536];
                    long count = 0;
                    while (true)
                    {
                        var read = await input.ReadAsync(buffer, token);
                        if (read == 0) break;
                        count += read;
                        if (count > Size) throw new BilibiliException("bilibili_tool_checksum", "Công cụ tải vượt kích thước đã xác minh.");
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                    }
                    if (count != Size) throw new BilibiliException("bilibili_tool_checksum", "Công cụ tải chưa được tải đầy đủ.");
                }
                if (!(await BilibiliFiles.HashAsync(part, token)).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new BilibiliException("bilibili_tool_checksum", "Checksum công cụ tải không khớp. Tệp chưa được cài.");
                // Notices are embedded from pinned upstream source; never infer readiness from a marker.
                foreach (var name in new[] { "LICENSE", "THIRD_PARTY_LICENSES.txt", "PROVENANCE.md" })
                {
                    if (File.Exists(Path.Combine(_directory, name))) BilibiliFiles.RequireRegularFile(Path.Combine(_directory, name));
                    await using var source = typeof(BilibiliRuntime).Assembly.GetManifestResourceStream("Bilibili." + name)
                        ?? throw new InvalidOperationException("Missing Bilibili component notice.");
                    await using var destination = new FileStream(Path.Combine(_directory, name), FileMode.Create, FileAccess.Write, FileShare.None);
                    await source.CopyToAsync(destination, token);
                }
                File.Move(part, Executable, overwrite: true);
                return;
            }
            throw new HttpRequestException();
        }
        finally { if (File.Exists(part)) File.Delete(part); }
    }

    internal static void ValidateDownloadUri(Uri uri)
    {
        if (uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0
            || uri.Host is not ("github.com" or "release-assets.githubusercontent.com"))
            throw new BilibiliException("bilibili_tool_source_invalid", "Nguồn tải công cụ không hợp lệ.");
    }

    public static async Task<string> ResolveShortUrlAsync(string url, CancellationToken token)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(20) };
        for (var i = 0; i < 4; i++)
        {
            url = BilibiliUrl.Normalize(url);
            if (!BilibiliUrl.IsShort(url)) return url;
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is not (>= 300 and <= 399) || response.Headers.Location is null) break;
            url = new Uri(new Uri(url), response.Headers.Location).AbsoluteUri;
        }
        throw new BilibiliException("bilibili_short_url_failed", "Không mở được link rút gọn. Hãy dùng link video hoặc kênh đầy đủ.");
    }
}

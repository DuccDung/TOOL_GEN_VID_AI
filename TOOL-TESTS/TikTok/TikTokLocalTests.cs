using System.Net;
using TOOL_LOCAL.TikTok;

namespace TOOL_TESTS.TikTok;

public sealed class TikTokLocalTests
{
    [Theory]
    [InlineData(null, 100, 0, 99)]
    [InlineData("bytes=10-19", 100, 10, 19)]
    [InlineData("bytes=90-", 100, 90, 99)]
    [InlineData("bytes=-10", 100, 90, 99)]
    public void PreviewRange_ParsesSingleByteRanges(string? header, long length, long start, long end)
    {
        Assert.True(TikTokMediaPreviewService.TryParseRange(header, length, out var range));
        Assert.Equal(start, range.Start);
        Assert.Equal(end, range.End);
    }

    [Fact]
    public void PreviewUrl_DoesNotExposeAbsolutePathOrAcceptTraversal()
    {
        var id = Guid.NewGuid();
        var url = TikTokMediaPreviewService.CreatePreviewUrl(id);

        Assert.Equal($"https://tiktok-media.app.local/video/{id:N}", url);
        Assert.True(TikTokMediaPreviewService.TryParse(new Uri(url), out var parsed));
        Assert.Equal(id, parsed);
        Assert.False(TikTokMediaPreviewService.TryParse(new Uri("https://tiktok-media.app.local/video/../secret"), out _));
    }

    [Theory]
    [InlineData("open-upload.tiktokapis.com")]
    [InlineData("open-upload-sg.tiktokapis.com")]
    [InlineData("upload.us.tiktokapis.com")]
    public async Task Upload_SendsExactContentRangeWithoutAuthorizationHeader(string uploadHost)
    {
        var root = Path.Combine(Path.GetTempPath(), $"videomaker-tiktok-upload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "video.mp4");
        var bytes = Enumerable.Range(0, 128).Select(x => (byte)x).ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            var handler = new CaptureUploadHandler();
            using var httpClient = new HttpClient(handler);
            var service = new TikTokUploadService(httpClient);
            var info = new FileInfo(path);
            var media = new TikTokMediaSelection(
                Guid.NewGuid(), path, info.Name, "video/mp4", info.Length, info.LastWriteTimeUtc,
                1, 360, 640, 30, "h264", "https://tiktok-media.app.local/video/test");

            var uploadUrl = $"https://{uploadHost}/video/?upload_id=test&upload_token=fake%2Bvalue%3D";
            await service.UploadAsync(
                media,
                uploadUrl,
                info.Length,
                1,
                null,
                CancellationToken.None);

            Assert.Equal(bytes, handler.Body);
            Assert.Equal("bytes 0-127/128", handler.ContentRange);
            Assert.Null(handler.Authorization);
            Assert.Equal(uploadUrl, handler.UploadUrl);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(TikTokUploadUrlCases.RejectedUrls), MemberType = typeof(TikTokUploadUrlCases))]
    public async Task Upload_RejectsUnsafeUrlBeforeReadingFileOrSendingBytes(string uploadUrl)
    {
        var handler = new CaptureUploadHandler();
        using var httpClient = new HttpClient(handler);
        var service = new TikTokUploadService(httpClient);
        // Intentionally absent: URL validation must happen before opening any media file.
        var path = Path.Combine(Path.GetTempPath(), $"absent-tiktok-{Guid.NewGuid():N}.mp4");
        var media = new TikTokMediaSelection(Guid.NewGuid(), path, "video.mp4", "video/mp4", 128,
            DateTime.UtcNow, 1, 360, 640, 30, "h264", "https://tiktok-media.app.local/video/test");

        var exception = await Assert.ThrowsAsync<TikTokDesktopException>(() =>
            service.UploadAsync(media, uploadUrl, 128, 1, null, CancellationToken.None));

        Assert.Equal("tiktok_upload_url_invalid", exception.Code);
        Assert.DoesNotContain(uploadUrl, exception.Message, StringComparison.Ordinal);
        Assert.Null(handler.UploadUrl);
        Assert.Null(handler.Body);
    }

    [Theory]
    [InlineData(65L * 1024 * 1024, 64L * 1024 * 1024, 1)]
    [InlineData(65L * 1024 * 1024, 4L * 1024 * 1024, 16)]
    [InlineData(4L * 1024 * 1024, 2L * 1024 * 1024, 1)]
    public void UploadPlan_RejectsPlansOutsideTikTokChunkRules(long totalBytes, long chunkSize, int totalChunks)
    {
        Assert.Throws<TikTokDesktopException>(() =>
            TikTokUploadService.ValidatePlan(totalBytes, chunkSize, totalChunks));
    }

    [Fact]
    public async Task Upload_ReconcilesAChunkAlreadyAcceptedByTikTok()
    {
        var root = Path.Combine(Path.GetTempPath(), $"videomaker-tiktok-reconcile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "video.mp4");
        await File.WriteAllBytesAsync(path, new byte[128]);
        try
        {
            using var httpClient = new HttpClient(new AlreadyUploadedHandler());
            var service = new TikTokUploadService(httpClient);
            var info = new FileInfo(path);
            var media = new TikTokMediaSelection(
                Guid.NewGuid(), path, info.Name, "video/mp4", info.Length, info.LastWriteTimeUtc,
                1, 360, 640, 30, "h264", "https://tiktok-media.app.local/video/test");

            await service.UploadAsync(
                media,
                "https://open-upload.tiktokapis.com/video/?upload_id=test&upload_token=secret",
                info.Length,
                1,
                null,
                CancellationToken.None);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UiAndHost_KeepTikTokIndependentAndDoNotExposeUploadUrlToReact()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var hook = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "features", "tiktok", "useTikTokModule.ts");
        var page = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "features", "tiktok", "TikTokPage.tsx");
        var form = ReadRepositoryFile("TOOL-LOCAL", "Form1.cs");
        var bridge = ReadRepositoryFile("TOOL-LOCAL", "TikTok", "TikTokWebBridge.cs");
        var desktopSettings = ReadRepositoryFile("TOOL-LOCAL", "appsettings.json");

        Assert.Contains("feature: 'tikTokEnabled'", app, StringComparison.Ordinal);
        Assert.Contains("page: 'tiktok'", app, StringComparison.Ordinal);
        Assert.Contains("postToHost('tiktok.state.get')", hook, StringComparison.Ordinal);
        Assert.Contains("_tiktokBridge.TryHandleAsync", form, StringComparison.Ordinal);
        Assert.Contains("MessagePrefix = \"tiktok.\"", bridge, StringComparison.Ordinal);
        Assert.Contains("File được đọc tại máy và tải trực tiếp lên TikTok", page, StringComparison.Ordinal);
        Assert.Contains("commercialContent", page, StringComparison.Ordinal);
        Assert.Contains("Xác nhận sử dụng âm nhạc", page, StringComparison.Ordinal);
        Assert.Contains("tiktok.policy.open", bridge, StringComparison.Ordinal);
        Assert.Contains("\"TikTokEnabled\": true", desktopSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("uploadUrl", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", page, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate)) return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Cannot locate repository file: {Path.Combine(relativeParts)}");
    }

    private sealed class CaptureUploadHandler : HttpMessageHandler
    {
        public string? UploadUrl { get; private set; }
        public byte[]? Body { get; private set; }
        public string? ContentRange { get; private set; }
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UploadUrl = request.RequestUri!.AbsoluteUri;
            Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            ContentRange = request.Content.Headers.ContentRange?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.Created);
        }
    }

    private sealed class AlreadyUploadedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                Content = new ByteArrayContent([])
            };
            response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, 127, 128);
            return Task.FromResult(response);
        }
    }
}

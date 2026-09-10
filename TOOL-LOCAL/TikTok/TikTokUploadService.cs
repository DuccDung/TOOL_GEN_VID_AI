using System.Net;
using System.Net.Http.Headers;

namespace TOOL_LOCAL.TikTok;

internal sealed record TikTokUploadProgress(long UploadedBytes, long TotalBytes, int Percent, int CompletedChunks, int TotalChunks);

internal sealed class TikTokUploadService(HttpClient httpClient)
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "open-upload.tiktokapis.com",
        "open-upload-sg.tiktokapis.com",
        "upload.us.tiktokapis.com"
    };

    public async Task UploadAsync(
        TikTokMediaSelection media,
        string uploadUrl,
        long chunkSize,
        int totalChunks,
        IProgress<TikTokUploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var uri = ValidateUploadUri(uploadUrl);
        ValidatePlan(media.SizeBytes, chunkSize, totalChunks);
        using var source = new FileStream(
            media.AbsolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (source.Length != media.SizeBytes)
            throw new TikTokDesktopException("tiktok_video_changed", "Video đã thay đổi sau khi được chọn. Hãy chọn lại video.");
        long offset = 0;
        for (var index = 0; index < totalChunks; index++)
        {
            var isLast = index == totalChunks - 1;
            var length = isLast ? media.SizeBytes - offset : chunkSize;
            await UploadChunkAsync(uri, source, media, offset, length, isLast, cancellationToken);
            offset += length;
            progress?.Report(new TikTokUploadProgress(
                offset,
                media.SizeBytes,
                (int)Math.Round(offset * 100d / media.SizeBytes),
                index + 1,
                totalChunks));
        }
        if (offset != media.SizeBytes)
            throw new TikTokDesktopException("tiktok_upload_incomplete", "Dung lượng video đã truyền không khớp với file đã chọn.");
    }

    private async Task UploadChunkAsync(
        Uri uploadUri,
        FileStream source,
        TikTokMediaSelection media,
        long offset,
        long length,
        bool isLast,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var segment = new TikTokBoundedReadStream(source, offset, length, leaveOpen: true);
            using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri);
            request.Content = new StreamContent(segment, 1024 * 1024);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(media.MimeType);
            request.Content.Headers.ContentLength = length;
            request.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + length - 1, media.SizeBytes);
            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                continue;
            }
            using (response)
            {
                var expected = isLast ? HttpStatusCode.Created : HttpStatusCode.PartialContent;
                if (response.StatusCode == expected) return;
                if ((response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable ||
                     (int)response.StatusCode >= 500) &&
                    TryGetUploadedBytes(response, out var uploadedBytes) &&
                    uploadedBytes >= offset + length)
                {
                    return;
                }
                if ((int)response.StatusCode >= 500 && attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                    continue;
                }
                throw new TikTokDesktopException(
                    response.StatusCode == HttpStatusCode.Forbidden ? "tiktok_upload_url_expired" : "tiktok_upload_failed",
                    response.StatusCode == HttpStatusCode.Forbidden
                        ? "Phiên upload TikTok đã hết hạn. Hãy đăng lại từ đầu."
                        : $"TikTok từ chối phần video đang tải lên (HTTP {(int)response.StatusCode}).");
            }
        }
        throw new TikTokDesktopException("tiktok_upload_failed", "Không thể tải video lên TikTok sau nhiều lần thử.");
    }

    internal static void ValidatePlan(long totalBytes, long chunkSize, int totalChunks)
    {
        const long maximumVideoBytes = 4L * 1024 * 1024 * 1024;
        const long minimumChunkBytes = 5L * 1024 * 1024;
        const long maximumChunkBytes = 64L * 1024 * 1024;
        const long maximumFinalChunkBytes = 128L * 1024 * 1024;
        if (totalBytes <= 0 || totalBytes > maximumVideoBytes || chunkSize <= 0 || totalChunks is < 1 or > 1000)
            throw new TikTokDesktopException("tiktok_upload_plan_invalid", "Kế hoạch upload TikTok không hợp lệ.");
        if (totalBytes <= maximumChunkBytes)
        {
            if (totalChunks != 1 || chunkSize != totalBytes)
                throw new TikTokDesktopException("tiktok_upload_plan_invalid", "Kế hoạch upload TikTok không hợp lệ.");
            return;
        }
        if (chunkSize < minimumChunkBytes || chunkSize > maximumChunkBytes || totalChunks < 2)
            throw new TikTokDesktopException("tiktok_upload_plan_invalid", "Kích thước phần upload TikTok không hợp lệ.");
        var expectedCount = checked((int)(totalBytes / chunkSize));
        var finalChunkSize = totalBytes - chunkSize * (totalChunks - 1L);
        if (totalChunks != expectedCount ||
            finalChunkSize < chunkSize || finalChunkSize > maximumFinalChunkBytes)
            throw new TikTokDesktopException("tiktok_upload_plan_invalid", "Số phần upload TikTok không khớp với dung lượng video.");
    }

    private static Uri ValidateUploadUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !AllowedHosts.Contains(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) || value.Length > 2048)
            throw new TikTokDesktopException("tiktok_upload_url_invalid", "Địa chỉ upload TikTok không an toàn.");
        return uri;
    }

    private static bool TryGetUploadedBytes(HttpResponseMessage response, out long uploadedBytes)
    {
        uploadedBytes = 0;
        if (!response.Headers.TryGetValues("Content-Range", out var values) &&
            !response.Content.Headers.TryGetValues("Content-Range", out values)) return false;
        var value = values.FirstOrDefault();
        if (!ContentRangeHeaderValue.TryParse(value, out var range) || range.To is null) return false;
        uploadedBytes = range.To.Value + 1;
        return uploadedBytes > 0;
    }
}

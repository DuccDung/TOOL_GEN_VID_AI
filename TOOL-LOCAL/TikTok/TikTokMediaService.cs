using System.Globalization;
using TOOL_LOCAL.Media;

namespace TOOL_LOCAL.TikTok;

internal sealed record TikTokDesktopAvatar(byte[] Bytes, string MimeType);

internal sealed record TikTokMediaSelection(
    Guid MediaId,
    string AbsolutePath,
    string FileName,
    string MimeType,
    long SizeBytes,
    DateTime LastWriteTimeUtc,
    decimal DurationSeconds,
    int Width,
    int Height,
    decimal FramesPerSecond,
    string VideoCodec,
    string PreviewUrl);

internal sealed class TikTokMediaService(
    FfprobeService probeService,
    IMediaToolPreflightService mediaToolPreflight)
{
    private const long MaximumVideoBytes = 4L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> VideoCodecs = new(StringComparer.OrdinalIgnoreCase)
        { "h264", "hevc", "vp8", "vp9" };
    private readonly object _sync = new();
    private TikTokMediaSelection? _current;
    private readonly Dictionary<Guid, (TikTokDesktopAvatar Content, DateTime Expires, string Url)> _avatars = new();

    public string SetAvatar(Guid id, TikTokDesktopAvatar avatar)
    {
        var url = $"https://{TikTokMediaPreviewService.HostName}/avatar/{id:N}/{Guid.NewGuid():N}";
        lock (_sync)
        {
            if (_avatars.Count >= 16) _avatars.Remove(_avatars.OrderBy(x => x.Value.Expires).First().Key);
            _avatars[id] = (avatar, DateTime.UtcNow.AddMinutes(15), url);
        }
        return url;
    }

    public string? GetAvatarUrl(Guid id)
    {
        lock (_sync) return _avatars.TryGetValue(id, out var value) && value.Expires > DateTime.UtcNow ? value.Url : null;
    }

    public void RetainAvatars(IEnumerable<Guid> ids)
    {
        var allowed = ids.ToHashSet();
        lock (_sync)
            foreach (var id in _avatars.Keys.Where(id => !allowed.Contains(id)).ToArray()) _avatars.Remove(id);
    }

    public TikTokDesktopAvatar? ResolveAvatar(Uri uri)
    {
        lock (_sync) return _avatars.Values.FirstOrDefault(x => x.Url == uri.AbsoluteUri && x.Expires > DateTime.UtcNow).Content;
    }

    public async Task<TikTokMediaSelection> SelectAsync(string path, CancellationToken cancellationToken)
    {
        await mediaToolPreflight.RequireReadyAsync(cancellationToken);
        var absolutePath = Path.GetFullPath(path);
        var extension = Path.GetExtension(absolutePath).ToLowerInvariant();
        var mimeType = extension switch
        {
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            _ => throw new TikTokDesktopException("tiktok_video_format_invalid", "Chỉ hỗ trợ video MP4, MOV hoặc WebM.")
        };
        var file = new FileInfo(absolutePath);
        if (!file.Exists)
            throw new TikTokDesktopException("tiktok_video_not_found", "Không tìm thấy video đã chọn.");
        if (file.Length <= 0 || file.Length > MaximumVideoBytes)
            throw new TikTokDesktopException("tiktok_video_size_invalid", "Video phải có dung lượng lớn hơn 0 và không vượt quá 4 GB.");
        var probe = await probeService.ProbeAsync(absolutePath, cancellationToken);
        ValidateProbe(probe);
        var mediaId = Guid.NewGuid();
        var selection = new TikTokMediaSelection(
            mediaId,
            absolutePath,
            file.Name,
            mimeType,
            file.Length,
            file.LastWriteTimeUtc,
            probe.DurationSeconds,
            probe.Width!.Value,
            probe.Height!.Value,
            probe.FramesPerSecond!.Value,
            probe.VideoCodec!,
            TikTokMediaPreviewService.CreatePreviewUrl(mediaId));
        lock (_sync) _current = selection;
        return selection;
    }

    public TikTokMediaSelection RequireCurrent()
    {
        TikTokMediaSelection? current;
        lock (_sync) current = _current;
        if (current is null)
            throw new TikTokDesktopException("tiktok_video_required", "Hãy chọn video cần đăng.");
        var file = new FileInfo(current.AbsolutePath);
        if (!file.Exists)
            throw new TikTokDesktopException("tiktok_video_not_found", "Video đã chọn không còn tồn tại.");
        if (file.Length != current.SizeBytes || file.LastWriteTimeUtc != current.LastWriteTimeUtc)
            throw new TikTokDesktopException("tiktok_video_changed", "Video đã thay đổi sau khi được chọn. Hãy chọn lại video.");
        return current;
    }

    public bool TryResolve(Guid mediaId, out TikTokMediaSelection selection)
    {
        lock (_sync)
        {
            if (_current is { } current && current.MediaId == mediaId)
            {
                selection = current;
                return true;
            }
        }
        selection = null!;
        return false;
    }

    public void Clear()
    {
        lock (_sync) _current = null;
    }

    private static void ValidateProbe(MediaProbeResult probe)
    {
        if (!probe.HasVideo || probe.DurationSeconds <= 0)
            throw new TikTokDesktopException("tiktok_video_invalid", "File đã chọn không chứa luồng video hợp lệ.");
        if (probe.DurationSeconds > 600)
            throw new TikTokDesktopException("tiktok_video_too_long", "Content Posting API chỉ hỗ trợ video tối đa 10 phút.");
        if (probe.Width is null || probe.Height is null ||
            probe.Width is < 360 or > 4096 || probe.Height is < 360 or > 4096)
            throw new TikTokDesktopException("tiktok_video_dimensions_invalid", "Kích thước video phải nằm trong khoảng 360–4096 pixel.");
        if (probe.FramesPerSecond is null || probe.FramesPerSecond is < 23 or > 60)
            throw new TikTokDesktopException("tiktok_video_fps_invalid", "Tốc độ khung hình TikTok hỗ trợ nằm trong khoảng 23–60 FPS.");
        if (string.IsNullOrWhiteSpace(probe.VideoCodec) || !VideoCodecs.Contains(probe.VideoCodec))
            throw new TikTokDesktopException("tiktok_video_codec_invalid", "Codec video phải là H.264, H.265, VP8 hoặc VP9.");
    }
}

internal readonly record struct TikTokMediaByteRange(long Start, long End)
{
    public long Length => End - Start + 1;
}

internal sealed record TikTokPreviewResponse(int StatusCode, string ReasonPhrase, string Headers, Stream Content);

internal sealed class TikTokMediaPreviewService(TikTokMediaService mediaService)
{
    public const string HostName = "tiktok-media.app.local";

    public static string CreatePreviewUrl(Guid mediaId) => $"https://{HostName}/video/{mediaId:N}";

    public TikTokPreviewResponse Open(Uri uri, string method, string? rangeHeader)
    {
        if (uri.IsAbsoluteUri && uri.Scheme == "https" && uri.Port == 443 && uri.Host == HostName &&
            uri.AbsolutePath.StartsWith("/avatar/", StringComparison.Ordinal) && method is "GET" or "HEAD")
        {
            var avatar = mediaService.ResolveAvatar(uri);
            return avatar is null ? Error(404, "Not Found", "tiktok_avatar_not_found") :
                new(200, "OK", $"Content-Type: {avatar.MimeType}\r\nContent-Length: {avatar.Bytes.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n",
                    method == "HEAD" ? Stream.Null : new MemoryStream(avatar.Bytes, writable: false));
        }
        if (method is not ("GET" or "HEAD") || !TryParse(uri, out var mediaId))
            return Error(400, "Bad Request", "tiktok_preview_request_invalid");
        if (!mediaService.TryResolve(mediaId, out var media))
            return Error(404, "Not Found", "tiktok_preview_not_found");
        FileStream source;
        try
        {
            mediaService.RequireCurrent();
            source = new FileStream(
                media.AbsolutePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TikTokDesktopException)
        {
            return Error(409, "Conflict", "tiktok_preview_unavailable");
        }
        if (!TryParseRange(rangeHeader, source.Length, out var range))
        {
            source.Dispose();
            return Error(416, "Range Not Satisfiable", "tiktok_preview_range_invalid", $"Content-Range: bytes */{media.SizeBytes}\r\n");
        }
        var partial = !string.IsNullOrWhiteSpace(rangeHeader);
        Stream content = method == "HEAD"
            ? DisposeAndEmpty(source)
            : partial ? new TikTokBoundedReadStream(source, range.Start, range.Length) : source;
        var headers =
            $"Content-Type: {media.MimeType}\r\n" +
            "Accept-Ranges: bytes\r\n" +
            "Cache-Control: private, no-store\r\n" +
            "Access-Control-Allow-Origin: https://app.local\r\n" +
            "Cross-Origin-Resource-Policy: same-site\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            $"Content-Length: {range.Length}\r\n" +
            (partial ? $"Content-Range: bytes {range.Start}-{range.End}/{media.SizeBytes}\r\n" : string.Empty);
        return new TikTokPreviewResponse(partial ? 206 : 200, partial ? "Partial Content" : "OK", headers, content);
    }

    internal static bool TryParse(Uri uri, out Guid mediaId)
    {
        mediaId = Guid.Empty;
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals(HostName, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        var path = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped);
        if (path.Contains("..", StringComparison.Ordinal) || path.Contains('\\')) return false;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0] == "video" && Guid.TryParseExact(parts[1], "N", out mediaId);
    }

    internal static bool TryParseRange(string? value, long length, out TikTokMediaByteRange range)
    {
        range = default;
        if (length <= 0) return false;
        if (string.IsNullOrWhiteSpace(value))
        {
            range = new TikTokMediaByteRange(0, length - 1);
            return true;
        }
        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || value.Contains(',')) return false;
        var bounds = value[6..].Split('-', 2);
        if (bounds.Length != 2) return false;
        if (bounds[0].Length == 0)
        {
            if (!long.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) || suffix <= 0) return false;
            suffix = Math.Min(suffix, length);
            range = new TikTokMediaByteRange(length - suffix, length - 1);
            return true;
        }
        if (!long.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out var start) || start < 0 || start >= length) return false;
        var end = length - 1;
        if (bounds[1].Length > 0 &&
            (!long.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture, out end) || end < start)) return false;
        range = new TikTokMediaByteRange(start, Math.Min(end, length - 1));
        return true;
    }

    private static TikTokPreviewResponse Error(int status, string reason, string code, string extra = "") =>
        new(status, reason, "Content-Length: 0\r\nCache-Control: no-store\r\n" + $"X-TikTok-Error-Code: {code}\r\n" + extra, Stream.Null);

    private static Stream DisposeAndEmpty(Stream stream)
    {
        stream.Dispose();
        return Stream.Null;
    }
}

internal sealed class TikTokBoundedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _start;
    private readonly long _length;
    private readonly bool _leaveOpen;

    public TikTokBoundedReadStream(Stream inner, long start, long length, bool leaveOpen = false)
    {
        _inner = inner;
        _start = start;
        _length = length;
        _leaveOpen = leaveOpen;
        _inner.Position = start;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _inner.Position - _start; set => Seek(value, SeekOrigin.Begin); }
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Allowed(count));
    public override int Read(Span<byte> buffer) => _inner.Read(buffer[..Allowed(buffer.Length)]);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer[..Allowed(buffer.Length)], cancellationToken);
    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (target < 0 || target > _length) throw new IOException("Vị trí đọc nằm ngoài media range.");
        _inner.Position = _start + target;
        return target;
    }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing && !_leaveOpen) _inner.Dispose(); base.Dispose(disposing); }
    private int Allowed(int requested) => (int)Math.Min(Math.Max(0, _length - Position), requested);
}

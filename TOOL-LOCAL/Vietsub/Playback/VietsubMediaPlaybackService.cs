using System.Globalization;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Media;

namespace TOOL_LOCAL.Vietsub.Playback;

internal readonly record struct VietsubMediaByteRange(long Start, long End)
{
    public long Length => End - Start + 1;
}

internal sealed record VietsubPlaybackResponse(
    int StatusCode,
    string ReasonPhrase,
    string Headers,
    Stream Content);

internal static class VietsubLocalMediaRange
{
    public static bool TryParse(
        string? rangeHeader,
        long resourceLength,
        out VietsubMediaByteRange range)
    {
        range = default;
        if (resourceLength <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(rangeHeader))
        {
            range = new(0, resourceLength - 1);
            return true;
        }

        const string prefix = "bytes=";
        if (!rangeHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = rangeHeader[prefix.Length..].Trim();
        if (value.Contains(','))
        {
            return false;
        }

        var separator = value.IndexOf('-');
        if (separator < 0)
        {
            return false;
        }

        var startText = value[..separator].Trim();
        var endText = value[(separator + 1)..].Trim();
        if (startText.Length == 0)
        {
            if (!long.TryParse(endText, NumberStyles.None, CultureInfo.InvariantCulture, out var suffixLength)
                || suffixLength <= 0)
            {
                return false;
            }

            suffixLength = Math.Min(suffixLength, resourceLength);
            range = new(resourceLength - suffixLength, resourceLength - 1);
            return true;
        }

        if (!long.TryParse(startText, NumberStyles.None, CultureInfo.InvariantCulture, out var start)
            || start < 0
            || start >= resourceLength)
        {
            return false;
        }

        var end = resourceLength - 1;
        if (endText.Length > 0
            && (!long.TryParse(endText, NumberStyles.None, CultureInfo.InvariantCulture, out end)
                || end < start))
        {
            return false;
        }

        range = new(start, Math.Min(end, resourceLength - 1));
        return true;
    }
}

internal sealed class VietsubMediaPlaybackService(
    VietsubMediaImportService mediaImportService,
    VietsubTimelineThumbnailService? thumbnailService = null)
{
    public const string HostName = "vietsub-media.app.local";

    public static string CreatePlaybackUrl(Guid projectId, Guid mediaId) =>
        $"https://{HostName}/projects/{projectId:N}/media/{mediaId:N}";

    public static string CreateThumbnailUrl(Guid projectId, Guid mediaId, int index) =>
        $"https://{HostName}/projects/{projectId:N}/media/{mediaId:N}/thumbnails/{index:D3}.jpg";

    public VietsubPlaybackResponse? Open(
        Uri requestUri,
        string method,
        string? rangeHeader,
        VietsubProjectManifest activeProject)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(activeProject);
        if (TryParseThumbnailUrl(requestUri, out var thumbnailProjectId, out var thumbnailMediaId, out var index))
        {
            return OpenThumbnail(
                method,
                activeProject,
                thumbnailProjectId,
                thumbnailMediaId,
                index);
        }
        if (!TryParseUrl(requestUri, out var projectId, out var mediaId)
            || projectId != activeProject.ProjectId
            || activeProject.SourceVideo is not { } media
            || media.MediaId != mediaId)
        {
            return null;
        }

        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return Error(405, "Method Not Allowed", "Allow: GET, HEAD\r\n");
        }

        var status = mediaImportService.GetSourceStatus(projectId, media);
        if (!status.Available || status.Changed || string.IsNullOrWhiteSpace(status.EffectivePath))
        {
            return null;
        }

        FileStream source;
        try
        {
            source = new FileStream(
                status.EffectivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var totalLength = source.Length;
        if (!VietsubLocalMediaRange.TryParse(rangeHeader, totalLength, out var range))
        {
            source.Dispose();
            return Error(
                416,
                "Range Not Satisfiable",
                $"Accept-Ranges: bytes\r\nContent-Range: bytes */{totalLength}\r\n");
        }

        var partial = !string.IsNullOrWhiteSpace(rangeHeader);
        var content = string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
            ? DisposeAndReturnEmpty(source)
            : partial
                ? new VietsubBoundedReadStream(source, range.Start, range.Length)
                : source;
        var headers =
            $"Content-Type: {GetMimeType(media.FileName)}\r\n" +
            "Accept-Ranges: bytes\r\n" +
            "Cache-Control: private, no-store\r\n" +
            "Access-Control-Allow-Origin: https://app.local\r\n" +
            "Cross-Origin-Resource-Policy: same-site\r\n" +
            $"Content-Length: {range.Length}\r\n" +
            (partial ? $"Content-Range: bytes {range.Start}-{range.End}/{totalLength}\r\n" : string.Empty);
        return new(
            partial ? 206 : 200,
            partial ? "Partial Content" : "OK",
            headers,
            content);
    }

    internal static bool TryParseUrl(Uri uri, out Guid projectId, out Guid mediaId)
    {
        projectId = Guid.Empty;
        mediaId = Guid.Empty;
        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, HostName, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var unescapedPath = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped);
        if (unescapedPath.Contains('\\') || unescapedPath.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }
        var parts = unescapedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4
            && string.Equals(parts[0], "projects", StringComparison.Ordinal)
            && Guid.TryParseExact(parts[1], "N", out projectId)
            && string.Equals(parts[2], "media", StringComparison.Ordinal)
            && Guid.TryParseExact(parts[3], "N", out mediaId);
    }

    internal static bool TryParseThumbnailUrl(
        Uri uri,
        out Guid projectId,
        out Guid mediaId,
        out int index)
    {
        projectId = Guid.Empty;
        mediaId = Guid.Empty;
        index = -1;
        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, HostName, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var unescapedPath = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped);
        if (unescapedPath.Contains('\\') || unescapedPath.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }
        var parts = unescapedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 6
            && string.Equals(parts[0], "projects", StringComparison.Ordinal)
            && Guid.TryParseExact(parts[1], "N", out projectId)
            && string.Equals(parts[2], "media", StringComparison.Ordinal)
            && Guid.TryParseExact(parts[3], "N", out mediaId)
            && string.Equals(parts[4], "thumbnails", StringComparison.Ordinal)
            && parts[5].EndsWith(".jpg", StringComparison.Ordinal)
            && int.TryParse(
                parts[5].AsSpan(0, parts[5].Length - 4),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out index)
            && index is >= 0 and < VietsubTimelineThumbnailService.ThumbnailCount;
    }

    private VietsubPlaybackResponse? OpenThumbnail(
        string method,
        VietsubProjectManifest activeProject,
        Guid projectId,
        Guid mediaId,
        int index)
    {
        if (thumbnailService is null
            || projectId != activeProject.ProjectId
            || activeProject.SourceVideo is not { } media
            || media.MediaId != mediaId)
        {
            return null;
        }
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return Error(405, "Method Not Allowed", "Allow: GET, HEAD\r\n");
        }

        var path = thumbnailService.ResolveExistingPath(projectId, media.Sha256, index);
        if (path is null)
        {
            return null;
        }
        try
        {
            var source = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var length = source.Length;
            var content = string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
                ? DisposeAndReturnEmpty(source)
                : source;
            return new(
                200,
                "OK",
                "Content-Type: image/jpeg\r\n" +
                $"Content-Length: {length}\r\n" +
                "Cache-Control: private, max-age=31536000, immutable\r\n" +
                "Access-Control-Allow-Origin: https://app.local\r\n" +
                "Cross-Origin-Resource-Policy: same-site\r\n",
                content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static VietsubPlaybackResponse Error(int status, string reason, string headers) =>
        new(status, reason, headers, Stream.Null);

    private static Stream DisposeAndReturnEmpty(Stream stream)
    {
        stream.Dispose();
        return Stream.Null;
    }

    private static string GetMimeType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4",
            ".mkv" => "video/x-matroska",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };
}

internal sealed class VietsubBoundedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _start;
    private readonly long _length;

    public VietsubBoundedReadStream(Stream inner, long start, long length)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (!inner.CanRead || !inner.CanSeek)
        {
            throw new ArgumentException("Stream phải đọc và seek được.", nameof(inner));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        if (start + length > inner.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        _inner = inner;
        _start = start;
        _length = length;
        _inner.Position = start;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _inner.Position - _start;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var allowed = GetAllowedCount(count);
        return allowed == 0 ? 0 : _inner.Read(buffer, offset, allowed);
    }

    public override int Read(Span<byte> buffer)
    {
        var allowed = GetAllowedCount(buffer.Length);
        return allowed == 0 ? 0 : _inner.Read(buffer[..allowed]);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var allowed = GetAllowedCount(buffer.Length);
        return allowed == 0
            ? ValueTask.FromResult(0)
            : _inner.ReadAsync(buffer[..allowed], cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (target < 0 || target > _length)
        {
            throw new IOException("Vị trí seek nằm ngoài media range.");
        }
        _inner.Position = _start + target;
        return target;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }

    private int GetAllowedCount(int requested) =>
        (int)Math.Min(Math.Max(0, _length - Position), requested);
}

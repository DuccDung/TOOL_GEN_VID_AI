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
    Stream Content,
    string ResourceType = VietsubPlaybackResourceTypes.Unknown,
    string? ErrorCode = null);

internal static class VietsubPlaybackResourceTypes
{
    public const string Unknown = "unknown";
    public const string Video = "video";
    public const string Thumbnail = "thumbnail";
    public const string Waveform = "waveform";
}

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

internal sealed class VietsubMediaPlaybackService
{
    private static readonly byte[] JpegMagic = [0xff, 0xd8, 0xff];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    internal const int MaximumThumbnailBytes = 4 * 1024 * 1024;
    internal const int MaximumWaveformBytes = 8 * 1024 * 1024;
    private readonly VietsubMediaImportService _mediaImportService;
    private readonly VietsubTimelineThumbnailService? _thumbnailService;
    private readonly VietsubTimelineWaveformService? _waveformService;

    public VietsubMediaPlaybackService(
        VietsubMediaImportService mediaImportService,
        VietsubTimelineThumbnailService? thumbnailService = null,
        VietsubTimelineWaveformService? waveformService = null)
    {
        _mediaImportService = mediaImportService;
        _thumbnailService = thumbnailService;
        _waveformService = waveformService;
    }

    public const string HostName = "vietsub-media.app.local";

    public static string CreatePlaybackUrl(Guid projectId, Guid mediaId) =>
        $"https://{HostName}/projects/{projectId:N}/media/{mediaId:N}";

    public static string CreateThumbnailUrl(
        Guid projectId,
        Guid mediaId,
        string sourceSha256,
        int index) =>
        $"https://{HostName}/projects/{projectId:N}/media/{mediaId:N}/thumbnails/" +
        $"v{VietsubTimelineThumbnailService.ProfileVersion}/{NormalizeSha256(sourceSha256)}/{index:D3}.jpg";

    public static string CreateWaveformUrl(Guid projectId, Guid mediaId, string sourceSha256) =>
        $"https://{HostName}/projects/{projectId:N}/media/{mediaId:N}/waveform/" +
        $"v{VietsubTimelineWaveformService.ProfileVersion}/{NormalizeSha256(sourceSha256)}/source.png";

    internal static string ClassifyResource(Uri requestUri) =>
        TryParseThumbnailUrl(requestUri, out _, out _, out _, out _, out _)
            ? VietsubPlaybackResourceTypes.Thumbnail
            : TryParseWaveformUrl(requestUri, out _, out _, out _, out _)
                ? VietsubPlaybackResourceTypes.Waveform
                : TryParseUrl(requestUri, out _, out _)
                    ? VietsubPlaybackResourceTypes.Video
                    : VietsubPlaybackResourceTypes.Unknown;

    public VietsubPlaybackResponse Open(
        Uri requestUri,
        string method,
        string? rangeHeader,
        VietsubProjectManifest activeProject)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(activeProject);
        if (!IsSupportedMethod(method))
        {
            return Error(
                400,
                "Bad Request",
                "vietsub_media_method_invalid",
                VietsubPlaybackResourceTypes.Unknown,
                "Allow: GET, HEAD\r\n");
        }

        if (TryParseWaveformUrl(
            requestUri,
            out var waveformProjectId,
            out var waveformMediaId,
            out var waveformProfileVersion,
            out var waveformSourceSha256))
        {
            return OpenWaveform(
                method,
                activeProject,
                waveformProjectId,
                waveformMediaId,
                waveformProfileVersion,
                waveformSourceSha256);
        }
        if (TryParseThumbnailUrl(
            requestUri,
            out var thumbnailProjectId,
            out var thumbnailMediaId,
            out var thumbnailProfileVersion,
            out var thumbnailSourceSha256,
            out var index))
        {
            return OpenThumbnail(
                method,
                activeProject,
                thumbnailProjectId,
                thumbnailMediaId,
                thumbnailProfileVersion,
                thumbnailSourceSha256,
                index);
        }
        if (!TryParseUrl(requestUri, out var projectId, out var mediaId))
        {
            return Error(
                400,
                "Bad Request",
                "vietsub_media_route_invalid",
                VietsubPlaybackResourceTypes.Unknown);
        }

        if (!TryGetScopedMedia(activeProject, projectId, mediaId, out var media))
        {
            return Error(
                403,
                "Forbidden",
                "vietsub_media_context_mismatch",
                VietsubPlaybackResourceTypes.Video);
        }

        var status = _mediaImportService.GetSourceStatus(projectId, media);
        if (!status.Available || status.Changed || string.IsNullOrWhiteSpace(status.EffectivePath))
        {
            var issueCode = status.Changed
                ? "vietsub_media_source_changed"
                : status.IssueCode switch
                {
                    "vietsub_media_reference_invalid" => "vietsub_media_reference_invalid",
                    "vietsub_media_source_missing" => "vietsub_media_source_missing",
                    _ => "vietsub_media_source_unavailable"
                };
            return Error(
                409,
                "Media Source Unavailable",
                issueCode,
                VietsubPlaybackResourceTypes.Video,
                "X-Vietsub-Recovery-Action: relink-or-copy-source\r\n");
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
            return Error(
                500,
                "Internal Server Error",
                "vietsub_media_stream_unreadable",
                VietsubPlaybackResourceTypes.Video);
        }

        var totalLength = source.Length;
        if (!VietsubLocalMediaRange.TryParse(rangeHeader, totalLength, out var range))
        {
            source.Dispose();
            return Error(
                416,
                "Range Not Satisfiable",
                "vietsub_media_range_invalid",
                VietsubPlaybackResourceTypes.Video,
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
            content,
            VietsubPlaybackResourceTypes.Video);
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
        out int profileVersion,
        out string sourceSha256,
        out int index)
    {
        projectId = Guid.Empty;
        mediaId = Guid.Empty;
        profileVersion = -1;
        sourceSha256 = string.Empty;
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
        return parts.Length == 8
            && string.Equals(parts[0], "projects", StringComparison.Ordinal)
            && Guid.TryParseExact(parts[1], "N", out projectId)
            && string.Equals(parts[2], "media", StringComparison.Ordinal)
            && Guid.TryParseExact(parts[3], "N", out mediaId)
            && string.Equals(parts[4], "thumbnails", StringComparison.Ordinal)
            && TryParseProfileVersion(parts[5], out profileVersion)
            && IsSha256(parts[6])
            && AssignNormalizedSha256(parts[6], out sourceSha256)
            && parts[7].EndsWith(".jpg", StringComparison.Ordinal)
            && int.TryParse(
                parts[7].AsSpan(0, parts[7].Length - 4),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out index)
            && index is >= 0 and < VietsubTimelineThumbnailService.ThumbnailCount;
    }

    internal static bool TryParseWaveformUrl(
        Uri uri,
        out Guid projectId,
        out Guid mediaId,
        out int profileVersion,
        out string sourceSha256)
    {
        projectId = Guid.Empty;
        mediaId = Guid.Empty;
        profileVersion = -1;
        sourceSha256 = string.Empty;
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
        return parts.Length == 8
            && string.Equals(parts[0], "projects", StringComparison.Ordinal)
            && Guid.TryParseExact(parts[1], "N", out projectId)
            && string.Equals(parts[2], "media", StringComparison.Ordinal)
            && Guid.TryParseExact(parts[3], "N", out mediaId)
            && string.Equals(parts[4], "waveform", StringComparison.Ordinal)
            && TryParseProfileVersion(parts[5], out profileVersion)
            && IsSha256(parts[6])
            && AssignNormalizedSha256(parts[6], out sourceSha256)
            && string.Equals(parts[7], "source.png", StringComparison.Ordinal);
    }

    private VietsubPlaybackResponse OpenThumbnail(
        string method,
        VietsubProjectManifest activeProject,
        Guid projectId,
        Guid mediaId,
        int profileVersion,
        string sourceSha256,
        int index)
    {
        if (!TryGetScopedMedia(activeProject, projectId, mediaId, out var media))
        {
            return Error(
                403,
                "Forbidden",
                "vietsub_media_context_mismatch",
                VietsubPlaybackResourceTypes.Thumbnail);
        }
        if (_thumbnailService is null)
        {
            return Error(
                500,
                "Internal Server Error",
                "vietsub_thumbnail_service_unavailable",
                VietsubPlaybackResourceTypes.Thumbnail);
        }
        if (profileVersion != VietsubTimelineThumbnailService.ProfileVersion
            || !string.Equals(sourceSha256, media.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return Error(
                409,
                "Conflict",
                "vietsub_media_artifact_stale",
                VietsubPlaybackResourceTypes.Thumbnail,
                "X-Vietsub-Recovery-Action: refresh-artifact-state\r\n");
        }

        var sourceConflict = GetArtifactSourceConflict(projectId, media, VietsubPlaybackResourceTypes.Thumbnail);
        if (sourceConflict is not null)
        {
            return sourceConflict;
        }

        var path = _thumbnailService.ResolveArtifactPath(projectId, media.Sha256, index);
        if (path is not null
            && !File.Exists(path)
            && _thumbnailService.HasStaleArtifacts(projectId, media.Sha256))
        {
            return Error(
                409,
                "Conflict",
                "vietsub_media_artifact_stale",
                VietsubPlaybackResourceTypes.Thumbnail,
                "X-Vietsub-Recovery-Action: regenerate-artifact\r\n");
        }

        return OpenImageArtifact(
            method,
            path,
            "image/jpeg",
            JpegMagic,
            MaximumThumbnailBytes,
            VietsubPlaybackResourceTypes.Thumbnail,
            "vietsub_thumbnail_artifact_missing",
            "vietsub_thumbnail_artifact_invalid");
    }

    private VietsubPlaybackResponse OpenWaveform(
        string method,
        VietsubProjectManifest activeProject,
        Guid projectId,
        Guid mediaId,
        int profileVersion,
        string sourceSha256)
    {
        if (!TryGetScopedMedia(activeProject, projectId, mediaId, out var media))
        {
            return Error(
                403,
                "Forbidden",
                "vietsub_media_context_mismatch",
                VietsubPlaybackResourceTypes.Waveform);
        }
        if (_waveformService is null)
        {
            return Error(
                500,
                "Internal Server Error",
                "vietsub_waveform_service_unavailable",
                VietsubPlaybackResourceTypes.Waveform);
        }
        if (profileVersion != VietsubTimelineWaveformService.ProfileVersion
            || !string.Equals(sourceSha256, media.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return Error(
                409,
                "Conflict",
                "vietsub_media_artifact_stale",
                VietsubPlaybackResourceTypes.Waveform,
                "X-Vietsub-Recovery-Action: refresh-artifact-state\r\n");
        }

        var sourceConflict = GetArtifactSourceConflict(projectId, media, VietsubPlaybackResourceTypes.Waveform);
        if (sourceConflict is not null)
        {
            return sourceConflict;
        }

        var path = _waveformService.ResolveArtifactPath(projectId, media.Sha256);
        if (path is not null
            && !File.Exists(path)
            && _waveformService.HasStaleArtifacts(projectId, media.Sha256))
        {
            return Error(
                409,
                "Conflict",
                "vietsub_media_artifact_stale",
                VietsubPlaybackResourceTypes.Waveform,
                "X-Vietsub-Recovery-Action: regenerate-artifact\r\n");
        }

        return OpenImageArtifact(
            method,
            path,
            "image/png",
            PngMagic,
            MaximumWaveformBytes,
            VietsubPlaybackResourceTypes.Waveform,
            "vietsub_waveform_artifact_missing",
            "vietsub_waveform_artifact_invalid");
    }

    private VietsubPlaybackResponse OpenImageArtifact(
        string method,
        string? path,
        string contentType,
        ReadOnlySpan<byte> magic,
        int maximumBytes,
        string resourceType,
        string missingErrorCode,
        string invalidErrorCode)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Error(
                409,
                "Conflict",
                "vietsub_media_artifact_stale",
                resourceType,
                "X-Vietsub-Recovery-Action: regenerate-artifact\r\n");
        }

        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return Error(
                    404,
                    "Not Found",
                    missingErrorCode,
                    resourceType,
                    "X-Vietsub-Recovery-Action: regenerate-artifact\r\n");
            }
            if (info.Length < magic.Length || info.Length > maximumBytes)
            {
                return Error(
                    500,
                    "Internal Server Error",
                    invalidErrorCode,
                    resourceType,
                    "X-Vietsub-Recovery-Action: regenerate-artifact\r\n");
            }
            bytes = File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            return Error(
                404,
                "Not Found",
                missingErrorCode,
                resourceType,
                "X-Vietsub-Recovery-Action: regenerate-artifact\r\n");
        }
        catch (DirectoryNotFoundException)
        {
            return Error(
                404,
                "Not Found",
                missingErrorCode,
                resourceType,
                "X-Vietsub-Recovery-Action: regenerate-artifact\r\n");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Error(500, "Internal Server Error", "vietsub_media_artifact_unreadable", resourceType);
        }

        if (bytes.Length < magic.Length
            || bytes.Length > maximumBytes
            || !bytes.AsSpan(0, magic.Length).SequenceEqual(magic))
        {
            return Error(
                500,
                "Internal Server Error",
                invalidErrorCode,
                resourceType,
                "X-Vietsub-Recovery-Action: regenerate-artifact\r\n");
        }

        var content = string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
            ? Stream.Null
            : new MemoryStream(bytes, writable: false);
        return new(
            200,
            "OK",
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {bytes.Length}\r\n" +
            "Cache-Control: private, max-age=31536000, immutable\r\n" +
            "Access-Control-Allow-Origin: https://app.local\r\n" +
            "Cross-Origin-Resource-Policy: same-site\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Vary: Origin\r\n",
            content,
            resourceType);
    }

    internal static VietsubPlaybackResponse Error(
        int status,
        string reason,
        string errorCode,
        string resourceType = VietsubPlaybackResourceTypes.Unknown,
        string additionalHeaders = "") =>
        new(
            status,
            reason,
            "Content-Length: 0\r\n" +
            "Cache-Control: no-store\r\n" +
            $"X-Vietsub-Error-Code: {errorCode}\r\n" +
            additionalHeaders,
            Stream.Null,
            resourceType,
            errorCode);

    private VietsubPlaybackResponse? GetArtifactSourceConflict(
        Guid projectId,
        VietsubMediaReference media,
        string resourceType)
    {
        var status = _mediaImportService.GetSourceStatus(projectId, media);
        if (status.Available && !status.Changed && !string.IsNullOrWhiteSpace(status.EffectivePath))
        {
            return null;
        }

        return Error(
            409,
            "Conflict",
            status.Changed ? "vietsub_media_source_changed" : "vietsub_media_artifact_stale",
            resourceType,
            "X-Vietsub-Recovery-Action: regenerate-artifact\r\n");
    }

    private static bool TryGetScopedMedia(
        VietsubProjectManifest activeProject,
        Guid projectId,
        Guid mediaId,
        out VietsubMediaReference media)
    {
        if (projectId != activeProject.ProjectId
            || activeProject.SourceVideo is not { } sourceVideo
            || sourceVideo.MediaId != mediaId)
        {
            media = null!;
            return false;
        }

        media = sourceVideo;
        return true;
    }

    private static bool IsSupportedMethod(string method) =>
        string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSha256(string value) =>
        IsSha256(value)
            ? value.ToLowerInvariant()
            : throw new ArgumentException("SHA-256 của media không hợp lệ.", nameof(value));

    private static bool AssignNormalizedSha256(string value, out string normalized)
    {
        normalized = value.ToLowerInvariant();
        return true;
    }

    private static bool TryParseProfileVersion(string value, out int profileVersion)
    {
        profileVersion = -1;
        return value.Length > 1
            && value[0] == 'v'
            && int.TryParse(
                value.AsSpan(1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out profileVersion)
            && profileVersion > 0;
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

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

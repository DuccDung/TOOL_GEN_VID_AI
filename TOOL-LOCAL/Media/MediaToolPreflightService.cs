using TOOL_SHARED.Distribution;

namespace TOOL_LOCAL.Media;

public sealed record MediaToolPaths(
    string FfmpegPath,
    string FfprobePath,
    string? BundleDirectory = null);

public sealed record MediaToolStatusSummary(
    bool Ready,
    string? ErrorCode,
    string Message,
    string? FfmpegVersion,
    string? FfprobeVersion,
    DateTime CheckedAtUtc);

public sealed class MediaToolUnavailableException : Exception
{
    public MediaToolUnavailableException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

internal interface IMediaToolPreflightService
{
    Task<MediaToolStatusSummary> GetStatusAsync(bool force, CancellationToken cancellationToken);

    Task<MediaToolStatusSummary> RequireReadyAsync(CancellationToken cancellationToken);
}

internal sealed class MediaToolPathResolver(
    Configuration.MediaToolOptions options,
    string? applicationDirectory = null)
{
    private readonly string _applicationDirectory = Path.GetFullPath(applicationDirectory ?? AppContext.BaseDirectory);

    public MediaToolPaths Resolve()
    {
        var ffmpegPath = ResolveExecutable(options.FfmpegPath, "ffmpeg.exe");
        var ffprobePath = ResolveExecutable(options.FfprobePath, "ffprobe.exe");
        var bundledDirectory = Path.Combine(_applicationDirectory, "tools", "ffmpeg");
        var isBundled = PathEquals(ffmpegPath, Path.Combine(bundledDirectory, "ffmpeg.exe")) &&
                        PathEquals(ffprobePath, Path.Combine(bundledDirectory, "ffprobe.exe"));
        return new MediaToolPaths(
            ffmpegPath,
            ffprobePath,
            isBundled ? bundledDirectory : null);
    }

    private string ResolveExecutable(string? configuredPath, string executableName)
    {
        var expanded = Environment.ExpandEnvironmentVariables(configuredPath?.Trim() ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(expanded) && Path.IsPathFullyQualified(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        if (!string.IsNullOrWhiteSpace(expanded) &&
            (expanded.Contains(Path.DirectorySeparatorChar) || expanded.Contains(Path.AltDirectorySeparatorChar)))
        {
            var configuredRelative = Path.GetFullPath(Path.Combine(_applicationDirectory, expanded));
            if (File.Exists(configuredRelative))
            {
                return configuredRelative;
            }
        }

        var bundled = Path.Combine(_applicationDirectory, "tools", "ffmpeg", executableName);
        if (File.Exists(bundled))
        {
            return bundled;
        }

        // Development fallback: let ProcessStartInfo resolve the executable from PATH.
        return string.IsNullOrWhiteSpace(expanded) ||
               expanded.Contains(Path.DirectorySeparatorChar) ||
               expanded.Contains(Path.AltDirectorySeparatorChar)
            ? Path.GetFileNameWithoutExtension(executableName)
            : expanded;
    }

    private static bool PathEquals(string left, string right) =>
        Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}

internal sealed class MediaToolPreflightService(
    MediaToolPaths paths,
    IExternalProcessRunner processRunner,
    TimeProvider timeProvider) : IMediaToolPreflightService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _statusLock = new(1, 1);
    private MediaToolStatusSummary? _cachedStatus;

    public async Task<MediaToolStatusSummary> GetStatusAsync(
        bool force,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!force && _cachedStatus is { } cached &&
            now - new DateTimeOffset(cached.CheckedAtUtc, TimeSpan.Zero) < CacheLifetime)
        {
            return cached;
        }

        await _statusLock.WaitAsync(cancellationToken);
        try
        {
            now = timeProvider.GetUtcNow();
            if (!force && _cachedStatus is { } current &&
                now - new DateTimeOffset(current.CheckedAtUtc, TimeSpan.Zero) < CacheLifetime)
            {
                return current;
            }

            if (!string.IsNullOrWhiteSpace(paths.BundleDirectory))
            {
                try
                {
                    DesktopMediaBundleIntegrity.ValidateBundleDirectory(
                        paths.BundleDirectory,
                        requireReleaseApproval: false);
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    return _cachedStatus = Failure(
                        "media_tool_bundle_invalid",
                        $"Bộ FFmpeg đi kèm bị thiếu hoặc không còn nguyên vẹn. Hãy cài lại bộ xử lý video. Chi tiết: {exception.Message}",
                        now.UtcDateTime);
                }
            }

            var ffmpeg = await CheckExecutableAsync(
                paths.FfmpegPath,
                "ffmpeg_not_found",
                "Chưa tìm thấy FFmpeg. Hãy cài hoặc cấu hình bộ FFmpeg rồi kiểm tra lại.",
                cancellationToken);
            if (!ffmpeg.Success)
            {
                return _cachedStatus = Failure(ffmpeg.ErrorCode!, ffmpeg.Message!, now.UtcDateTime);
            }

            var ffprobe = await CheckExecutableAsync(
                paths.FfprobePath,
                "ffprobe_not_found",
                "Chưa tìm thấy FFprobe. Hãy cài hoặc cấu hình bộ FFmpeg rồi kiểm tra lại.",
                cancellationToken);
            if (!ffprobe.Success)
            {
                return _cachedStatus = Failure(ffprobe.ErrorCode!, ffprobe.Message!, now.UtcDateTime, ffmpeg.Version);
            }

            var ffmpegVersionId = ExtractVersionIdentifier(ffmpeg.Version, "ffmpeg");
            var ffprobeVersionId = ExtractVersionIdentifier(ffprobe.Version, "ffprobe");
            if (ffmpegVersionId is not null && ffprobeVersionId is not null &&
                !ffmpegVersionId.Equals(ffprobeVersionId, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedStatus = Failure(
                    "media_tool_version_mismatch",
                    "FFmpeg và FFprobe không cùng phiên bản. Hãy cài lại trọn bộ media tool.",
                    now.UtcDateTime,
                    ffmpeg.Version);
            }

            return _cachedStatus = new MediaToolStatusSummary(
                true,
                null,
                "FFmpeg và FFprobe đã sẵn sàng.",
                ffmpeg.Version,
                ffprobe.Version,
                now.UtcDateTime);
        }
        finally
        {
            _statusLock.Release();
        }
    }

    public async Task<MediaToolStatusSummary> RequireReadyAsync(CancellationToken cancellationToken)
    {
        var status = await GetStatusAsync(force: true, cancellationToken);
        if (!status.Ready)
        {
            throw new MediaToolUnavailableException(
                status.ErrorCode ?? "media_tool_unavailable",
                status.Message);
        }

        return status;
    }

    private async Task<ToolCheckResult> CheckExecutableAsync(
        string path,
        string missingCode,
        string missingMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await processRunner.RunAsync(
                path,
                ["-version"],
                TimeSpan.FromSeconds(10),
                cancellationToken);
            if (result.ExitCode != 0)
            {
                return ToolCheckResult.Fail(
                    "media_tool_version_check_failed",
                    "FFmpeg/FFprobe có tồn tại nhưng không thể kiểm tra phiên bản. Hãy cài lại bộ media tool.");
            }

            var version = result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim();
            return ToolCheckResult.Ok(
                string.IsNullOrWhiteSpace(version)
                    ? "Không rõ phiên bản"
                    : version.Length <= 200 ? version : version[..200]);
        }
        catch (FileNotFoundException)
        {
            if (Path.IsPathFullyQualified(path) && File.Exists(path))
            {
                return ToolCheckResult.Fail(
                    "media_tool_not_executable",
                    "Windows không thể chạy FFmpeg/FFprobe. Hãy kiểm tra đúng kiến trúc win-x64, quyền file hoặc cài lại bộ media tool.");
            }

            return ToolCheckResult.Fail(missingCode, missingMessage);
        }
        catch (UnauthorizedAccessException)
        {
            return ToolCheckResult.Fail(
                "media_tool_not_executable",
                "Windows không cho phép chạy FFmpeg/FFprobe. Hãy kiểm tra quyền file hoặc cài lại bộ media tool.");
        }
        catch (TimeoutException)
        {
            return ToolCheckResult.Fail(
                "media_tool_version_check_failed",
                "FFmpeg/FFprobe không phản hồi khi kiểm tra phiên bản.");
        }
    }

    private static MediaToolStatusSummary Failure(
        string code,
        string message,
        DateTime checkedAtUtc,
        string? ffmpegVersion = null) =>
        new(false, code, message, ffmpegVersion, null, checkedAtUtc);

    private static string? ExtractVersionIdentifier(string? versionLine, string toolName)
    {
        if (string.IsNullOrWhiteSpace(versionLine))
        {
            return null;
        }

        var parts = versionLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 &&
               parts[0].Equals(toolName, StringComparison.OrdinalIgnoreCase) &&
               parts[1].Equals("version", StringComparison.OrdinalIgnoreCase)
            ? parts[2]
            : null;
    }

    private sealed record ToolCheckResult(
        bool Success,
        string? Version,
        string? ErrorCode,
        string? Message)
    {
        public static ToolCheckResult Ok(string version) => new(true, version, null, null);

        public static ToolCheckResult Fail(string code, string message) => new(false, null, code, message);
    }
}

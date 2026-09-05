using System.Globalization;
using System.Text;

namespace TOOL_LOCAL.Vietsub.Playback;

internal sealed record VietsubMediaLoadFailure(
    string ResourceType,
    string CorrelationId,
    string ErrorCode)
{
    public static VietsubMediaLoadFailure Create(
        string resourceType,
        string correlationId,
        string? errorCode)
    {
        var safeCorrelation = Guid.TryParseExact(correlationId, "N", out var parsedCorrelation)
            ? parsedCorrelation.ToString("N")
            : Guid.NewGuid().ToString("N");
        return new(
            VietsubMediaRuntimeLog.NormalizeResourceType(resourceType),
            safeCorrelation,
            VietsubMediaRuntimeLog.NormalizeErrorCode(errorCode));
    }
}

internal sealed class VietsubMediaRuntimeLog
{
    internal const long DefaultMaximumBytes = 2 * 1024 * 1024;
    private const int DefaultRetainedFiles = 2;
    private static readonly object Sync = new();
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly string _logPath;
    private readonly long _maximumBytes;
    private readonly int _retainedFiles;

    internal VietsubMediaRuntimeLog(
        string logPath,
        long maximumBytes = DefaultMaximumBytes,
        int retainedFiles = DefaultRetainedFiles)
    {
        if (string.IsNullOrWhiteSpace(logPath))
        {
            throw new ArgumentException("Đường dẫn log media không được để trống.", nameof(logPath));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(retainedFiles);
        _logPath = Path.GetFullPath(logPath);
        _maximumBytes = maximumBytes;
        _retainedFiles = retainedFiles;
    }

    public static VietsubMediaRuntimeLog CreateDefault()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolGenPostVideo",
            "Logs");
        return new VietsubMediaRuntimeLog(Path.Combine(directory, "vietsub-media-runtime.log"));
    }

    public void Write(
        string correlationId,
        string resourceType,
        string method,
        int? statusCode,
        string? errorCode,
        string stage,
        string? exceptionType = null)
    {
        try
        {
            var line = Format(
                correlationId,
                resourceType,
                method,
                statusCode,
                errorCode,
                stage,
                exceptionType) + Environment.NewLine;
            var bytes = Utf8WithoutBom.GetByteCount(line);
            lock (Sync)
            {
                var directory = Path.GetDirectoryName(_logPath)
                    ?? throw new InvalidOperationException("Thư mục log media không hợp lệ.");
                Directory.CreateDirectory(directory);
                RotateIfNeeded(bytes);
                File.AppendAllText(_logPath, line, Utf8WithoutBom);
            }
        }
        catch
        {
            // Diagnostic logging must never break local media playback.
        }
    }

    internal static string Format(
        string correlationId,
        string resourceType,
        string method,
        int? statusCode,
        string? errorCode,
        string stage,
        string? exceptionType = null)
    {
        var correlation = Guid.TryParseExact(correlationId, "N", out var parsedCorrelation)
            ? parsedCorrelation.ToString("N")
            : "invalid";
        var status = statusCode?.ToString(CultureInfo.InvariantCulture) ?? "pending";
        var exception = string.IsNullOrWhiteSpace(exceptionType)
            ? "none"
            : NormalizeToken(exceptionType, 96);
        return $"{DateTime.UtcNow:O}\tCorrelation={correlation}" +
            $"\tResource={NormalizeResourceType(resourceType)}" +
            $"\tMethod={NormalizeMethod(method)}" +
            $"\tStatus={status}" +
            $"\tCode={NormalizeErrorCode(errorCode)}" +
            $"\tStage={NormalizeStage(stage)}" +
            $"\tExceptionType={exception}";
    }

    private void RotateIfNeeded(int pendingBytes)
    {
        var currentLength = File.Exists(_logPath) ? new FileInfo(_logPath).Length : 0;
        if (currentLength + pendingBytes <= _maximumBytes)
        {
            return;
        }

        if (_retainedFiles == 0)
        {
            File.Delete(_logPath);
            return;
        }

        for (var index = _retainedFiles; index >= 2; index--)
        {
            var previous = $"{_logPath}.{index - 1}";
            if (File.Exists(previous))
            {
                File.Move(previous, $"{_logPath}.{index}", overwrite: true);
            }
        }
        if (File.Exists(_logPath))
        {
            File.Move(_logPath, $"{_logPath}.1", overwrite: true);
        }
    }

    internal static string NormalizeResourceType(string? value) => value switch
    {
        VietsubPlaybackResourceTypes.Video => VietsubPlaybackResourceTypes.Video,
        VietsubPlaybackResourceTypes.Thumbnail => VietsubPlaybackResourceTypes.Thumbnail,
        VietsubPlaybackResourceTypes.Waveform => VietsubPlaybackResourceTypes.Waveform,
        _ => VietsubPlaybackResourceTypes.Unknown
    };

    private static string NormalizeMethod(string? value) => value?.ToUpperInvariant() switch
    {
        "GET" => "GET",
        "HEAD" => "HEAD",
        _ => "OTHER"
    };

    internal static string NormalizeErrorCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        return value.Length <= 96
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
                ? value
                : "vietsub_media_unknown_error";
    }

    private static string NormalizeStage(string? value) => value switch
    {
        "filter" => "filter",
        "bridge" => "bridge",
        "request_headers" => "request_headers",
        "playback" => "playback",
        "response_creation" => "response_creation",
        "response_received" => "response_received",
        _ => "unknown"
    };

    private static string NormalizeToken(string value, int maximumLength) => new(
        value.Take(maximumLength)
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'
                ? character
                : '_')
            .ToArray());
}

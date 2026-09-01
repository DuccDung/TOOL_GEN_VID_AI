using System.Security.Cryptography;
using System.Text.Json;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_LOCAL.Vietsub.Media;

internal enum VietsubMediaImportMode
{
    Link,
    Copy
}

internal sealed record VietsubMediaImportProgress(
    long BytesProcessed,
    long TotalBytes,
    double Percent,
    double MegabytesPerSecond);

internal sealed record VietsubMediaSourceStatus(
    bool Available,
    bool Changed,
    string? IssueCode,
    string? EffectivePath);

internal sealed class VietsubMediaException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

internal sealed class VietsubMediaImportService
{
    internal const long DefaultMaximumFileSizeBytes = 50L * 1024 * 1024 * 1024;
    private const long CopySafetyMarginBytes = 512L * 1024 * 1024;
    private const int BufferSize = 1024 * 1024;
    private static readonly HashSet<string> SupportedExtensions =
        new([".mp4", ".mkv", ".mov", ".webm"], StringComparer.OrdinalIgnoreCase);

    private readonly VietsubAppPaths _paths;
    private readonly IMediaToolPreflightService _preflight;
    private readonly FfprobeService _probe;
    private readonly long _maximumFileSizeBytes;

    public VietsubMediaImportService(
        VietsubAppPaths paths,
        IMediaToolPreflightService preflight,
        FfprobeService probe,
        long maximumFileSizeBytes = DefaultMaximumFileSizeBytes)
    {
        _paths = paths;
        _preflight = preflight;
        _probe = probe;
        if (maximumFileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileSizeBytes));
        }

        _maximumFileSizeBytes = maximumFileSizeBytes;
    }

    public async Task<VietsubMediaReference> ImportAsync(
        VietsubProjectManifest project,
        string sourcePath,
        VietsubMediaImportMode mode,
        decimal? maximumDurationMinutes = null,
        IProgress<VietsubMediaImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.SourceVideo is not null)
        {
            throw new VietsubMediaException(
                "vietsub_media_source_exists",
                "Dự án đã có video nguồn. Hãy tạo dự án mới nếu muốn dùng video khác.");
        }

        var fullSourcePath = GetExistingSourcePath(sourcePath);
        var sourceInfo = new FileInfo(fullSourcePath);
        ValidateFile(sourceInfo);
        var initialLength = sourceInfo.Length;
        var initialLastWriteUtc = sourceInfo.LastWriteTimeUtc;

        try
        {
            await _preflight.RequireReadyAsync(cancellationToken);
        }
        catch (MediaToolUnavailableException exception)
        {
            throw new VietsubMediaException(exception.Code, exception.Message, exception);
        }

        MediaProbeResult probe;
        try
        {
            probe = await _probe.ProbeAsync(fullSourcePath, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw new VietsubMediaException(
                "vietsub_media_probe_failed",
                "Video bị hỏng hoặc định dạng không được FFprobe hỗ trợ.",
                exception);
        }

        if (!probe.HasVideo || probe.DurationSeconds <= 0)
        {
            throw new VietsubMediaException(
                "vietsub_media_video_stream_missing",
                "Tệp đã chọn không chứa luồng video hợp lệ.");
        }

        if (maximumDurationMinutes is not null
            && probe.DurationSeconds > maximumDurationMinutes.Value * 60 + 0.5m)
        {
            throw new VietsubMediaException(
                "vietsub_media_duration_limit_exceeded",
                $"Video dài hơn giới hạn {maximumDurationMinutes:0.##} phút hiện tại.");
        }

        string? relativePath = null;
        string hash;
        if (mode == VietsubMediaImportMode.Copy)
        {
            relativePath = Path.Combine("source", "original" + sourceInfo.Extension.ToLowerInvariant());
            var destinationPath = _paths.GetProjectPath(project.ProjectId, relativePath);
            EnsureFreeDiskSpace(destinationPath, initialLength);
            hash = await CopyAndHashAsync(
                fullSourcePath,
                destinationPath,
                initialLength,
                progress,
                cancellationToken);
        }
        else
        {
            hash = await HashAsync(
                fullSourcePath,
                initialLength,
                progress,
                cancellationToken);
        }

        sourceInfo.Refresh();
        if (!sourceInfo.Exists
            || sourceInfo.Length != initialLength
            || sourceInfo.LastWriteTimeUtc != initialLastWriteUtc)
        {
            if (relativePath is not null)
            {
                TryDelete(_paths.GetProjectPath(project.ProjectId, relativePath));
            }
            throw new VietsubMediaException(
                "vietsub_media_source_changed_during_import",
                "Video nguồn đã thay đổi trong lúc nhập. Hãy chọn lại tệp ổn định.");
        }

        return new VietsubMediaReference
        {
            MediaId = Guid.NewGuid(),
            ImportMode = mode == VietsubMediaImportMode.Copy
                ? VietsubMediaImportModes.Copy
                : VietsubMediaImportModes.Link,
            OriginalPath = fullSourcePath,
            WorkspaceRelativePath = relativePath,
            FileName = sourceInfo.Name,
            SizeBytes = initialLength,
            Sha256 = hash,
            SourceLastWriteAtUtc = initialLastWriteUtc,
            Metadata = new VietsubMediaMetadata
            {
                DurationSeconds = probe.DurationSeconds,
                Width = probe.Width ?? 0,
                Height = probe.Height ?? 0,
                FramesPerSecond = probe.FramesPerSecond,
                VideoCodec = probe.VideoCodec,
                AudioCodec = probe.AudioCodec,
                AudioSampleRate = probe.AudioSampleRate,
                HasVideo = probe.HasVideo,
                HasAudio = probe.HasAudio
            }
        };
    }

    public VietsubMediaSourceStatus GetSourceStatus(
        Guid projectId,
        VietsubMediaReference media)
    {
        ArgumentNullException.ThrowIfNull(media);
        string effectivePath;
        try
        {
            effectivePath = ResolveEffectivePath(projectId, media);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new(false, true, "vietsub_media_reference_invalid", null);
        }

        var info = new FileInfo(effectivePath);
        if (!info.Exists)
        {
            return new(false, false, "vietsub_media_source_missing", null);
        }

        var expectedLastWriteUtc = string.Equals(
            media.ImportMode,
            VietsubMediaImportModes.Link,
            StringComparison.Ordinal)
                ? media.SourceLastWriteAtUtc
                : info.LastWriteTimeUtc;
        var changed = info.Length != media.SizeBytes
            || (string.Equals(media.ImportMode, VietsubMediaImportModes.Link, StringComparison.Ordinal)
                && info.LastWriteTimeUtc != expectedLastWriteUtc);
        return changed
            ? new(true, true, "vietsub_media_source_changed", null)
            : new(true, false, null, effectivePath);
    }

    public string ResolveEffectivePath(Guid projectId, VietsubMediaReference media)
    {
        ArgumentNullException.ThrowIfNull(media);
        if (string.Equals(media.ImportMode, VietsubMediaImportModes.Copy, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(media.WorkspaceRelativePath))
            {
                throw new InvalidDataException("Video COPY thiếu đường dẫn workspace.");
            }

            var path = _paths.GetProjectPath(projectId, media.WorkspaceRelativePath);
            var sourceDirectory = Path.GetFullPath(_paths.GetProjectPath(projectId, "source"));
            var sourcePrefix = sourceDirectory + Path.DirectorySeparatorChar;
            if (!path.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Video COPY nằm ngoài thư mục source của dự án.");
            }
            return path;
        }

        if (!string.Equals(media.ImportMode, VietsubMediaImportModes.Link, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(media.OriginalPath)
            || !Path.IsPathFullyQualified(media.OriginalPath))
        {
            throw new InvalidDataException("Tham chiếu video LINK không hợp lệ.");
        }

        return Path.GetFullPath(media.OriginalPath);
    }

    private string GetExistingSourcePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new VietsubMediaException("vietsub_media_file_required", "Hãy chọn video nguồn.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(sourcePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new VietsubMediaException("vietsub_media_path_invalid", "Đường dẫn video không hợp lệ.", exception);
        }

        if (!File.Exists(fullPath))
        {
            throw new VietsubMediaException("vietsub_media_file_not_found", "Không tìm thấy video đã chọn.");
        }
        return fullPath;
    }

    private void ValidateFile(FileInfo sourceInfo)
    {
        if (!SupportedExtensions.Contains(sourceInfo.Extension))
        {
            throw new VietsubMediaException(
                "vietsub_media_extension_unsupported",
                "Ứng dụng chỉ hỗ trợ video MP4, MKV, MOV và WEBM.");
        }
        if (sourceInfo.Length <= 0)
        {
            throw new VietsubMediaException("vietsub_media_file_empty", "Video đã chọn không có dữ liệu.");
        }
        if (sourceInfo.Length > _maximumFileSizeBytes)
        {
            throw new VietsubMediaException(
                "vietsub_media_file_too_large",
                $"Video vượt giới hạn {_maximumFileSizeBytes / 1024d / 1024d / 1024d:0.##} GB của ứng dụng.");
        }
    }

    private static async Task<string> CopyAndHashAsync(
        string sourcePath,
        string destinationPath,
        long totalBytes,
        IProgress<VietsubMediaImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var partialPath = destinationPath + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        TryDelete(partialPath);
        try
        {
            await using var source = OpenSource(sourcePath);
            await using var destination = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferSize];
            var processed = 0L;
            var startedAt = DateTime.UtcNow;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                hash.AppendData(buffer, 0, bytesRead);
                processed += bytesRead;
                Report(progress, processed, totalBytes, startedAt);
            }

            await destination.FlushAsync(cancellationToken);
            destination.Flush(flushToDisk: true);
            if (processed != totalBytes)
            {
                throw new VietsubMediaException(
                    "vietsub_media_source_changed_during_import",
                    "Kích thước video thay đổi trong lúc sao chép.");
            }

            destination.Close();
            File.Move(partialPath, destinationPath, overwrite: true);
            Report(progress, processed, totalBytes, startedAt);
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    private static async Task<string> HashAsync(
        string sourcePath,
        long totalBytes,
        IProgress<VietsubMediaImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var source = OpenSource(sourcePath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        var processed = 0L;
        var startedAt = DateTime.UtcNow;
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, bytesRead);
            processed += bytesRead;
            Report(progress, processed, totalBytes, startedAt);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static FileStream OpenSource(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        BufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static void Report(
        IProgress<VietsubMediaImportProgress>? progress,
        long processed,
        long total,
        DateTime startedAt)
    {
        if (progress is null)
        {
            return;
        }

        var elapsed = Math.Max(0.001, (DateTime.UtcNow - startedAt).TotalSeconds);
        progress.Report(new(
            processed,
            total,
            total <= 0 ? 0 : Math.Clamp(processed * 100d / total, 0, 100),
            processed / 1024d / 1024d / elapsed));
    }

    private static void EnsureFreeDiskSpace(string destinationPath, long requiredBytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(destinationPath));
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var drive = new DriveInfo(root);
        if (drive.IsReady && drive.AvailableFreeSpace < requiredBytes + CopySafetyMarginBytes)
        {
            throw new VietsubMediaException(
                "vietsub_media_disk_space_insufficient",
                "Ổ đĩa không đủ dung lượng để sao chép video và tạo file tạm.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

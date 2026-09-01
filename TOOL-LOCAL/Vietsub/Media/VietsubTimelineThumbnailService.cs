using System.Globalization;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_LOCAL.Vietsub.Media;

internal sealed class VietsubTimelineThumbnailService
{
    internal const int ThumbnailCount = 12;
    private const int ProfileVersion = 1;
    private readonly VietsubAppPaths _paths;
    private readonly VietsubMediaImportService _mediaImportService;
    private readonly IMediaToolPreflightService _preflight;
    private readonly string _ffmpegPath;
    private readonly IExternalProcessRunner _processRunner;

    public VietsubTimelineThumbnailService(
        VietsubAppPaths paths,
        VietsubMediaImportService mediaImportService,
        IMediaToolPreflightService preflight,
        string ffmpegPath,
        IExternalProcessRunner processRunner)
    {
        _paths = paths;
        _mediaImportService = mediaImportService;
        _preflight = preflight;
        _ffmpegPath = ffmpegPath;
        _processRunner = processRunner;
    }

    public async Task<IReadOnlyList<string>> EnsureAsync(
        VietsubProjectManifest project,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var media = project.SourceVideo
            ?? throw new VietsubMediaException("vietsub_media_source_required", "Dự án chưa có video nguồn.");
        var status = _mediaImportService.GetSourceStatus(project.ProjectId, media);
        if (!status.Available || status.Changed || string.IsNullOrWhiteSpace(status.EffectivePath))
        {
            throw new VietsubMediaException(
                status.IssueCode ?? "vietsub_media_source_unavailable",
                "Video nguồn không còn sẵn sàng để tạo ảnh timeline.");
        }

        await _preflight.RequireReadyAsync(cancellationToken);
        var generated = new List<string>(ThumbnailCount);
        for (var index = 0; index < ThumbnailCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outputPath = GetThumbnailPath(project.ProjectId, media.Sha256, index);
            if (!IsUsable(outputPath))
            {
                await GenerateAsync(
                    status.EffectivePath,
                    media.Metadata.DurationSeconds,
                    index,
                    outputPath,
                    cancellationToken);
            }
            if (IsUsable(outputPath))
            {
                generated.Add(Playback.VietsubMediaPlaybackService.CreateThumbnailUrl(
                    project.ProjectId,
                    media.MediaId,
                    index));
            }
            progress?.Report((index + 1d) * 100d / ThumbnailCount);
        }
        return generated;
    }

    public IReadOnlyList<string> GetExistingUrls(VietsubProjectManifest project)
    {
        if (project.SourceVideo is not { } media)
        {
            return [];
        }

        return Enumerable.Range(0, ThumbnailCount)
            .Where(index => IsUsable(GetThumbnailPath(project.ProjectId, media.Sha256, index)))
            .Select(index => Playback.VietsubMediaPlaybackService.CreateThumbnailUrl(
                project.ProjectId,
                media.MediaId,
                index))
            .ToArray();
    }

    public string? ResolveExistingPath(Guid projectId, string sha256, int index)
    {
        if (!IsSha256(sha256) || index is < 0 or >= ThumbnailCount)
        {
            return null;
        }
        var path = GetThumbnailPath(projectId, sha256, index);
        return IsUsable(path) ? path : null;
    }

    private async Task GenerateAsync(
        string sourcePath,
        decimal durationSeconds,
        int index,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(outputDirectory);
        var partialPath = Path.Combine(
            outputDirectory,
            $"{index:D3}.{Guid.NewGuid():N}.partial.jpg");
        try
        {
            var timestamp = GetTimestamp(durationSeconds, index)
                .ToString("0.###", CultureInfo.InvariantCulture);
            var result = await _processRunner.RunAsync(
                _ffmpegPath,
                [
                    "-hide_banner", "-loglevel", "error",
                    "-ss", timestamp,
                    "-i", sourcePath,
                    "-map", "0:v:0",
                    "-frames:v", "1",
                    "-vf", "scale=240:135:force_original_aspect_ratio=increase,crop=240:135",
                    "-an", "-sn", "-dn",
                    "-threads", "1",
                    "-q:v", "5",
                    "-update", "1",
                    "-y", partialPath
                ],
                TimeSpan.FromMinutes(2),
                cancellationToken);
            if (result.ExitCode != 0 || !IsUsable(partialPath))
            {
                throw new VietsubMediaException(
                    "vietsub_thumbnail_generation_failed",
                    "FFmpeg không thể tạo ảnh timeline cho video.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partialPath, outputPath, overwrite: true);
        }
        finally
        {
            TryDelete(partialPath);
        }
    }

    private string GetThumbnailPath(Guid projectId, string sha256, int index)
    {
        if (!IsSha256(sha256) || index is < 0 or >= ThumbnailCount)
        {
            throw new ArgumentException("Định danh thumbnail không hợp lệ.");
        }
        return _paths.GetProjectPath(
            projectId,
            "thumbnails",
            $"v{ProfileVersion}",
            sha256.ToLowerInvariant(),
            $"{index:D3}.jpg");
    }

    private static decimal GetTimestamp(decimal durationSeconds, int index)
    {
        if (durationSeconds <= 0)
        {
            return 0;
        }
        var position = durationSeconds * (index + 0.5m) / ThumbnailCount;
        return Math.Clamp(position, 0, Math.Max(0, durationSeconds - 0.05m));
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsUsable(string path)
    {
        try
        {
            return new FileInfo(path) is { Exists: true, Length: >= 128 };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
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

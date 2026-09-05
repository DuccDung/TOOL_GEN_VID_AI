using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_LOCAL.Vietsub.Media;

internal sealed record VietsubTimelineWaveformArtifact(
    string Status,
    string? Url,
    long Revision = 0);

internal sealed class VietsubTimelineWaveformService
{
    internal const int ProfileVersion = 1;
    private const int MinimumArtifactBytes = 128;
    private readonly VietsubAppPaths _paths;
    private readonly VietsubMediaImportService _mediaImportService;
    private readonly IMediaToolPreflightService _preflight;
    private readonly string _ffmpegPath;
    private readonly IExternalProcessRunner _processRunner;

    public VietsubTimelineWaveformService(
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

    public async Task<VietsubTimelineWaveformArtifact> EnsureAsync(
        VietsubProjectManifest project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var media = project.SourceVideo
            ?? throw new VietsubMediaException(
                "vietsub_media_source_required",
                "Dự án chưa có video nguồn.");
        if (!media.Metadata.HasAudio)
        {
            return new(VietsubWaveformStatuses.NoAudio, null);
        }

        var status = _mediaImportService.GetSourceStatus(project.ProjectId, media);
        if (!status.Available || status.Changed || string.IsNullOrWhiteSpace(status.EffectivePath))
        {
            throw new VietsubMediaException(
                status.IssueCode ?? "vietsub_media_source_unavailable",
                "Video nguồn không còn sẵn sàng để tạo waveform.");
        }

        var outputPath = GetWaveformPath(project.ProjectId, media.Sha256);
        if (!IsUsable(outputPath))
        {
            await _preflight.RequireReadyAsync(cancellationToken);
            await GenerateAsync(status.EffectivePath, outputPath, cancellationToken);
        }

        return IsUsable(outputPath)
            ? new(
                VietsubWaveformStatuses.Ready,
                Playback.VietsubMediaPlaybackService.CreateWaveformUrl(
                    project.ProjectId,
                    media.MediaId,
                    media.Sha256),
                File.GetLastWriteTimeUtc(outputPath).Ticks)
            : new(VietsubWaveformStatuses.Failed, null);
    }

    public VietsubTimelineWaveformArtifact GetExistingArtifact(VietsubProjectManifest project)
    {
        if (project.SourceVideo is not { } media)
        {
            return new(VietsubWaveformStatuses.Pending, null);
        }
        if (!media.Metadata.HasAudio)
        {
            return new(VietsubWaveformStatuses.NoAudio, null);
        }

        var path = GetWaveformPath(project.ProjectId, media.Sha256);
        return IsUsable(path)
            ? new(
                VietsubWaveformStatuses.Ready,
                Playback.VietsubMediaPlaybackService.CreateWaveformUrl(
                    project.ProjectId,
                    media.MediaId,
                    media.Sha256),
                File.GetLastWriteTimeUtc(path).Ticks)
            : new(VietsubWaveformStatuses.Pending, null);
    }

    public string? ResolveExistingPath(Guid projectId, string sha256)
    {
        var path = ResolveArtifactPath(projectId, sha256);
        return path is not null && IsUsable(path) ? path : null;
    }

    internal string? ResolveArtifactPath(Guid projectId, string sha256) =>
        !IsSha256(sha256) ? null : GetWaveformPath(projectId, sha256);

    internal bool HasStaleArtifacts(Guid projectId, string currentSha256)
    {
        if (!IsSha256(currentSha256))
        {
            return false;
        }

        try
        {
            var root = _paths.GetProjectPath(projectId, "waveforms", $"v{ProfileVersion}");
            return Directory.Exists(root)
                && Directory.EnumerateDirectories(root)
                    .Where(path => !string.Equals(
                        Path.GetFileName(path),
                        currentSha256,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(path => Path.Combine(path, "source.png"))
                    .Any(IsUsable);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task GenerateAsync(
        string sourcePath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(outputDirectory);
        var partialPath = Path.Combine(
            outputDirectory,
            $"{Guid.NewGuid():N}.partial.png");
        try
        {
            var result = await _processRunner.RunAsync(
                _ffmpegPath,
                [
                    "-hide_banner", "-loglevel", "error",
                    "-i", sourcePath,
                    "-filter_complex",
                    "[0:a:0]aformat=channel_layouts=mono,showwavespic=s=2048x64:colors=0x4f86cc:scale=sqrt[waveform]",
                    "-map", "[waveform]",
                    "-frames:v", "1",
                    "-an", "-sn", "-dn",
                    "-threads", "1",
                    "-y", partialPath
                ],
                TimeSpan.FromMinutes(3),
                cancellationToken);
            if (result.ExitCode != 0 || !IsUsable(partialPath))
            {
                throw new VietsubMediaException(
                    "vietsub_waveform_generation_failed",
                    "FFmpeg không thể phân tích âm thanh gốc cho timeline.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partialPath, outputPath, overwrite: true);
        }
        finally
        {
            TryDelete(partialPath);
        }
    }

    private string GetWaveformPath(Guid projectId, string sha256)
    {
        if (!IsSha256(sha256))
        {
            throw new ArgumentException("Định danh waveform không hợp lệ.", nameof(sha256));
        }

        return _paths.GetProjectPath(
            projectId,
            "waveforms",
            $"v{ProfileVersion}",
            sha256.ToLowerInvariant(),
            "source.png");
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsUsable(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MinimumArtifactBytes)
            {
                return false;
            }

            Span<byte> magic = stackalloc byte[8];
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            return stream.Read(magic) == magic.Length
                && magic[0] == 0x89
                && magic[1] == 0x50
                && magic[2] == 0x4e
                && magic[3] == 0x47
                && magic[4] == 0x0d
                && magic[5] == 0x0a
                && magic[6] == 0x1a
                && magic[7] == 0x0a;
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

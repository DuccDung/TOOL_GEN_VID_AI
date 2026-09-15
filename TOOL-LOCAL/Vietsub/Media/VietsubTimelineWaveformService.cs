using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Storage;
using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Globalization;

namespace TOOL_LOCAL.Vietsub.Media;

internal sealed record VietsubTimelineWaveformArtifact(
    string Status,
    string? Url,
    long Revision = 0);

internal sealed class VietsubTimelineWaveformService
{
    internal const int ProfileVersion = 2;
    private const int MinimumArtifactBytes = 128;
    internal const int OverviewSampleRate = 2000;
    internal const int WaveformWidth = 8192;
    private const int MaximumDurationSeconds = 4 * 60 * 60;
    private readonly SemaphoreSlim _generationGate = new(1, 1);
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
            await _generationGate.WaitAsync(cancellationToken);
            try
            {
                if (!IsUsable(outputPath))
                {
                    await _preflight.RequireReadyAsync(cancellationToken);
                    await GenerateAsync(status.EffectivePath, outputPath, media.Metadata.DurationSeconds, cancellationToken);
                }
            }
            finally { _generationGate.Release(); }
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
        decimal durationSeconds,
        CancellationToken cancellationToken)
    {
        if (durationSeconds is <= 0 or > MaximumDurationSeconds)
            throw new VietsubMediaException("vietsub_waveform_duration_unsupported", "Thời lượng video vượt giới hạn tạo waveform.");
        var outputDirectory = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(outputDirectory);
        var partialPath = Path.Combine(
            outputDirectory,
            $"{Guid.NewGuid():N}.partial.png");
        var pcmPath = partialPath + ".pcm";
        var maximumBytes = (long)Math.Ceiling(durationSeconds + 1) * OverviewSampleRate * 2;
        var drive = new DriveInfo(Path.GetPathRoot(outputPath)!);
        if (drive.AvailableFreeSpace < maximumBytes + 32L * 1024 * 1024)
            throw new VietsubMediaException("vietsub_waveform_disk_full", "Không đủ dung lượng tạm để tạo waveform.");
        try
        {
            using var decodeLease = await VietsubBackgroundMediaGate.EnterAsync(cancellationToken);
            var result = await _processRunner.RunAsync(
                _ffmpegPath,
                [
                    "-hide_banner", "-loglevel", "error",
                    "-threads", "1", "-i", sourcePath,
                    "-map", "0:a:0", "-vn", "-sn", "-dn",
                    "-ac", "1", "-ar", OverviewSampleRate.ToString(CultureInfo.InvariantCulture),
                    "-c:a", "pcm_s16le", "-f", "s16le",
                    "-t", durationSeconds.ToString(CultureInfo.InvariantCulture),
                    "-fs", maximumBytes.ToString(CultureInfo.InvariantCulture),
                    "-threads", "1",
                    "-y", pcmPath
                ],
                TimeSpan.FromSeconds(Math.Clamp((double)durationSeconds / 10 + 120, 180, 1800)),
                cancellationToken);
            if (result.ExitCode != 0 || !File.Exists(pcmPath) || new FileInfo(pcmPath).Length > maximumBytes)
            {
                throw new VietsubMediaException(
                    "vietsub_waveform_generation_failed",
                    "FFmpeg không thể phân tích âm thanh gốc cho timeline.");
            }
            // The PCM overview stays on disk. Only one read block and fixed peak bins are in RAM.
            await Task.Run(() => RenderOverview(pcmPath, partialPath, cancellationToken), cancellationToken);
            if (!IsUsable(partialPath)) throw new VietsubMediaException("vietsub_waveform_generation_failed", "Waveform không hợp lệ.");
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partialPath, outputPath, overwrite: true);
        }
        finally
        {
            TryDelete(partialPath);
            TryDelete(pcmPath);
        }
    }

    internal static void RenderOverview(string pcmPath, string pngPath, CancellationToken token)
    {
        using var source = new FileStream(pcmPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            32768, FileOptions.SequentialScan);
        if (source.Length < 2 || source.Length % 2 != 0 || source.Length > (MaximumDurationSeconds + 1L) * OverviewSampleRate * 2)
            throw new VietsubMediaException("vietsub_waveform_generation_failed", "Dữ liệu waveform không hợp lệ.");
        var totalSamples = source.Length / 2;
        var peaks = new int[WaveformWidth];
        var buffer = new byte[32768];
        long sample = 0;
        while (source.Position < source.Length)
        {
            token.ThrowIfCancellationRequested();
            var count = (int)Math.Min(buffer.Length, source.Length - source.Position);
            source.ReadExactly(buffer.AsSpan(0, count));
            for (var offset = 0; offset < count; offset += 2, sample++)
            {
                var bin = (int)(sample * WaveformWidth / totalSamples);
                peaks[bin] = Math.Max(peaks[bin], Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset, 2))));
            }
        }
        using var bitmap = new Bitmap(WaveformWidth, 64, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        using var pen = new Pen(Color.FromArgb(0x4f, 0x86, 0xcc));
        for (var x = 0; x < peaks.Length; x++)
        {
            var height = Math.Max(1, (int)Math.Round(Math.Sqrt(peaks[x] / 32768d) * 31));
            graphics.DrawLine(pen, x, 32 - height, x, 32 + height);
        }
        token.ThrowIfCancellationRequested();
        // Pass a managed stream so deeply nested workspace paths also work with GDI+.
        using var destination = new FileStream(pngPath, FileMode.Create, FileAccess.Write, FileShare.None);
        bitmap.Save(destination, ImageFormat.Png);
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

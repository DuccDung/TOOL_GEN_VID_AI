using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Media;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_LOCAL.Vietsub.Subtitles;

internal static class VietsubVideoExportErrorCodes
{
    public const string VideoRequired = "vietsub_export_video_required";
    public const string SubtitleRequired = "vietsub_export_subtitle_required";
    public const string TranslationRequired = "vietsub_export_translation_required";
    public const string DestinationInvalid = "vietsub_export_destination_invalid";
    public const string SourceChanged = "vietsub_export_source_changed";
    public const string TrackChanged = "vietsub_export_track_changed";
    public const string VoiceChanged = "vietsub_export_voice_changed";
    public const string VoiceInvalid = "vietsub_export_voice_invalid";
    public const string RenderFailed = "vietsub_export_render_failed";
    public const string OutputInvalid = "vietsub_export_output_invalid";
}

internal sealed class VietsubVideoExportException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

internal sealed record VietsubVideoExportResult(string FileName, long SizeBytes, decimal DurationSeconds);

internal sealed class VietsubVideoExportService(
    IVietsubLocalJobAuthorizer authorizer,
    VietsubProjectStore projectStore,
    VietsubMediaImportService mediaImportService,
    VietsubSubtitleStore subtitleStore,
    VietsubVoiceStore voiceStore,
    VietsubAppPaths paths,
    IMediaToolPreflightService mediaToolPreflight,
    FfprobeService mediaProbe,
    string ffmpegPath,
    IExternalProcessRunner processRunner)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<VietsubVideoExportResult> ExportAsync(
        VietsubProjectSession session,
        string userId,
        Guid organizationId,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var project = session.Manifest;
        try
        {
            await authorizer.AuthorizeAsync(userId, organizationId, project, cancellationToken);
        }
        catch (VietsubLocalJobAuthorizationException exception)
        {
            throw new VietsubVideoExportException(exception.Code, exception.Message, exception);
        }

        var media = project.SourceVideo
            ?? throw new VietsubVideoExportException(
                VietsubVideoExportErrorCodes.VideoRequired,
                "Hãy nhập video nguồn trước khi xuất thành phẩm.");
        if (project.ActiveSubtitleTrackId is not { } activeTrackId)
        {
            throw new VietsubVideoExportException(
                VietsubVideoExportErrorCodes.SubtitleRequired,
                "Hãy chọn track phụ đề trước khi xuất video.");
        }

        var fullDestinationPath = NormalizeDestination(destinationPath);
        string sourcePath;
        try
        {
            sourcePath = await mediaImportService.ResolveVerifiedSourcePathAsync(
                project.ProjectId,
                media,
                cancellationToken);
        }
        catch (VietsubOcrException exception)
        {
            throw new VietsubVideoExportException(
                VietsubVideoExportErrorCodes.SourceChanged,
                "Video nguồn không còn khớp với dữ liệu đã nhập. Hãy nhập lại video trước khi xuất.",
                exception);
        }
        if (Path.GetFullPath(sourcePath).Equals(fullDestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new VietsubVideoExportException(
                VietsubVideoExportErrorCodes.DestinationInvalid,
                "Không thể ghi đè trực tiếp lên video nguồn.");
        }

        await session.FlushAsync(cancellationToken);
        var tracks = await subtitleStore.LoadTracksAsync(project.ProjectId, cancellationToken);
        var track = tracks.SingleOrDefault(item => item.TrackId == activeTrackId)
            ?? throw new VietsubVideoExportException(
                VietsubVideoExportErrorCodes.SubtitleRequired,
                "Track phụ đề đang chọn không còn tồn tại.");
        var translatedCues = track.Cues
            .Where(cue => !string.IsNullOrWhiteSpace(cue.TranslatedText))
            .ToArray();
        if (translatedCues.Length == 0)
        {
            throw new VietsubVideoExportException(
                VietsubVideoExportErrorCodes.TranslationRequired,
                "Chưa có câu dịch tiếng Việt để chèn vào video.");
        }

        var style = project.SubtitleStyle.Copy();
        style.Normalize();
        var styleFingerprint = JsonSerializer.Serialize(style, JsonOptions);
        var audioMixSettings = project.AudioMixSettings.Copy();
        audioMixSettings.Normalize();
        var audioMixFingerprint = JsonSerializer.Serialize(audioMixSettings, JsonOptions);
        var voiceWorkspace = await voiceStore.LoadWorkspaceAsync(
            project.ProjectId,
            activeTrackId,
            track.Revision,
            project.VoiceSettings,
            cancellationToken);
        var monitorVoiceTimeline = !audioMixSettings.TranslatedVoiceMuted
            && audioMixSettings.TranslatedVoiceVolume > 0 && track.Cues.Any(cue => cue.VoiceEnabled);
        if (monitorVoiceTimeline && voiceWorkspace.Timeline is null
            && await voiceStore.HasTimelineHistoryAsync(project.ProjectId, activeTrackId, cancellationToken))
            throw new VietsubVideoExportException(VietsubVideoExportErrorCodes.VoiceChanged,
                "Giọng Việt cần cập nhật theo lựa chọn câu hiện tại. Hãy cập nhật giọng trước khi xuất MP4.");
        var voiceTimeline = monitorVoiceTimeline ? voiceWorkspace.Timeline : null;
        string? voiceTimelinePath = null;
        if (voiceTimeline is not null)
        {
            voiceTimelinePath = ResolveVoiceTimelinePath(project.ProjectId, voiceTimeline.RelativePath);
            if (!await IsVoiceArtifactUsableAsync(voiceTimelinePath, voiceTimeline, cancellationToken))
            {
                throw new VietsubVideoExportException(
                    VietsubVideoExportErrorCodes.VoiceInvalid,
                    "Timeline giọng Việt không còn hợp lệ. Hãy tạo lại giọng trước khi xuất video.");
            }
        }
        var normalizedRotation = ((media.Metadata.RotationDegrees % 360) + 360) % 360;
        var displayWidth = normalizedRotation is 90 or 270
            ? media.Metadata.Height
            : media.Metadata.Width;
        var displayHeight = normalizedRotation is 90 or 270
            ? media.Metadata.Width
            : media.Metadata.Height;
        var ass = VietsubAssSubtitleBuilder.BuildTranslated(
            translatedCues,
            style,
            displayWidth,
            displayHeight);

        var tempDirectory = paths.GetProjectPath(project.ProjectId, "temp", $"export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var assPath = Path.Combine(tempDirectory, "subtitles.ass");
        var destinationDirectory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new VietsubVideoExportException(
                VietsubVideoExportErrorCodes.DestinationInvalid,
                "Thư mục xuất video không hợp lệ.");
        Directory.CreateDirectory(destinationDirectory);
        var partialPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileNameWithoutExtension(fullDestinationPath)}.{Guid.NewGuid():N}.partial.mp4");

        try
        {
            await mediaToolPreflight.RequireReadyAsync(cancellationToken);
            await File.WriteAllTextAsync(assPath, ass, new UTF8Encoding(false), cancellationToken);
            var subtitleFilter = $"subtitles=filename='{EscapeSubtitleFilterPath(assPath)}'";
            var timeoutMinutes = Math.Clamp((double)media.Metadata.DurationSeconds / 60 * 3 + 5, 10, 720);
            var renderArguments = BuildRenderArguments(
                sourcePath,
                voiceTimelinePath,
                partialPath,
                subtitleFilter,
                media.Metadata.DurationSeconds,
                media.Metadata.HasAudio,
                audioMixSettings);
            var result = await processRunner.RunAsync(
                ffmpegPath,
                renderArguments,
                TimeSpan.FromMinutes(timeoutMinutes),
                cancellationToken);
            if (result.ExitCode != 0 || !File.Exists(partialPath))
            {
                throw new VietsubVideoExportException(
                    VietsubVideoExportErrorCodes.RenderFailed,
                    "Không thể kết xuất video có phụ đề. Hãy kiểm tra bộ FFmpeg và thử lại.");
            }

            var output = await mediaProbe.ProbeAsync(partialPath, cancellationToken);
            var expectsAudio = ShouldIncludeOriginalAudio(media.Metadata.HasAudio, audioMixSettings)
                || ShouldIncludeTranslatedVoice(voiceTimelinePath, audioMixSettings);
            if (!output.HasVideo
                || output.Width is null
                || output.Height is null
                || output.HasAudio != expectsAudio
                || output.DurationSeconds <= 0
                || Math.Abs(output.DurationSeconds - media.Metadata.DurationSeconds) > 1.5m)
            {
                throw new VietsubVideoExportException(
                    VietsubVideoExportErrorCodes.OutputInvalid,
                    "Video kết xuất không vượt qua bước kiểm tra tính toàn vẹn.");
            }

            await EnsureSnapshotStillCurrentAsync(
                project.ProjectId,
                activeTrackId,
                track.Revision,
                media.MediaId,
                media.Sha256,
                styleFingerprint,
                audioMixFingerprint,
                monitorVoiceTimeline,
                voiceTimeline?.ArtifactId,
                voiceTimeline?.Sha256,
                cancellationToken);
            File.Move(partialPath, fullDestinationPath, overwrite: true);
            var info = new FileInfo(fullDestinationPath);
            return new(info.Name, info.Length, output.DurationSeconds);
        }
        catch (VietsubVideoExportException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MediaToolUnavailableException exception)
        {
            throw new VietsubVideoExportException(exception.Code, exception.Message, exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or TimeoutException)
        {
            throw new VietsubVideoExportException(
                VietsubVideoExportErrorCodes.RenderFailed,
                "Không thể ghi video thành phẩm. Hãy kiểm tra dung lượng và quyền ghi của thư mục đích.",
                exception);
        }
        finally
        {
            TryDelete(partialPath);
            TryDeleteDirectory(tempDirectory);
        }
    }

    private async Task EnsureSnapshotStillCurrentAsync(
        Guid projectId,
        Guid trackId,
        int trackRevision,
        Guid mediaId,
        string mediaSha256,
        string styleFingerprint,
        string audioMixFingerprint,
        bool monitorVoiceTimeline,
        Guid? voiceArtifactId,
        string? voiceSha256,
        CancellationToken cancellationToken)
    {
        var currentProject = await projectStore.LoadForBackgroundJobAsync(projectId, cancellationToken);
        var currentMedia = currentProject.SourceVideo;
        if (currentMedia is null
            || currentMedia.MediaId != mediaId
            || !string.Equals(currentMedia.Sha256, mediaSha256, StringComparison.OrdinalIgnoreCase)
            || currentProject.ActiveSubtitleTrackId != trackId
            || !string.Equals(
                JsonSerializer.Serialize(currentProject.SubtitleStyle, JsonOptions),
                styleFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                JsonSerializer.Serialize(currentProject.AudioMixSettings, JsonOptions),
                audioMixFingerprint,
                StringComparison.Ordinal))
        {
            throw new VietsubVideoExportException(
                VietsubVideoExportErrorCodes.TrackChanged,
                "Video, track, phụ đề hoặc thiết lập âm thanh đã thay đổi trong lúc xuất. Hãy xuất lại để tránh dùng dữ liệu cũ.");
        }
        var currentTracks = await subtitleStore.LoadTracksAsync(projectId, cancellationToken);
        if (currentTracks.SingleOrDefault(item => item.TrackId == trackId)?.Revision != trackRevision)
        {
            throw new VietsubVideoExportException(
                VietsubVideoExportErrorCodes.TrackChanged,
                "Nội dung phụ đề đã thay đổi trong lúc xuất. Hãy xuất lại từ revision mới nhất.");
        }

        if (!monitorVoiceTimeline) return;

        var currentVoiceWorkspace = await voiceStore.LoadWorkspaceAsync(
            projectId,
            trackId,
            trackRevision,
            currentProject.VoiceSettings,
            cancellationToken);
        var currentVoice = currentVoiceWorkspace.Timeline;
        if (currentVoice?.ArtifactId != voiceArtifactId
            || !string.Equals(currentVoice?.Sha256, voiceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new VietsubVideoExportException(
                VietsubVideoExportErrorCodes.VoiceChanged,
                "Timeline giọng Việt đã thay đổi trong lúc xuất. Hãy xuất lại để dùng đúng bản giọng mới nhất.");
        }
        if (currentVoice is not null)
        {
            var currentVoicePath = ResolveVoiceTimelinePath(projectId, currentVoice.RelativePath);
            if (!await IsVoiceArtifactUsableAsync(currentVoicePath, currentVoice, cancellationToken))
            {
                throw new VietsubVideoExportException(
                    VietsubVideoExportErrorCodes.VoiceInvalid,
                    "Timeline giọng Việt không còn vượt qua kiểm tra toàn vẹn.");
            }
        }
    }

    internal static IReadOnlyList<string> BuildRenderArguments(
        string sourcePath,
        string? voiceTimelinePath,
        string partialPath,
        string subtitleFilter,
        decimal durationSeconds,
        bool sourceHasAudio,
        VietsubAudioMixSettings settings)
    {
        var includeOriginal = ShouldIncludeOriginalAudio(sourceHasAudio, settings);
        var includeVoice = ShouldIncludeTranslatedVoice(voiceTimelinePath, settings);
        var targetDuration = durationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var arguments = new List<string>
        {
            "-y",
            "-v", "error",
            "-i", sourcePath
        };
        if (includeVoice)
        {
            arguments.AddRange(["-i", voiceTimelinePath!]);
        }

        arguments.AddRange([
            "-map", "0:v:0",
            "-vf", subtitleFilter
        ]);

        if (includeOriginal && includeVoice)
        {
            var originalVolume = Invariant(settings.OriginalVolume);
            var voiceVolume = Invariant(settings.TranslatedVoiceVolume);
            var originalChain = $"[0:a:0]aformat=sample_rates=48000:channel_layouts=stereo,volume={originalVolume},apad,atrim=duration={targetDuration}[original]";
            var voiceChain = $"[1:a:0]aformat=sample_rates=48000:channel_layouts=stereo,volume={voiceVolume},apad,atrim=duration={targetDuration}";
            var mix = settings.AutoDuckOriginal
                ? $"{originalChain};{voiceChain},asplit=2[voice_mix][voice_sidechain];[original][voice_sidechain]sidechaincompress=threshold=0.015:ratio=10:attack=15:release=280[ducked];[ducked][voice_mix]amix=inputs=2:duration=first:normalize=0:dropout_transition=0,alimiter=limit=0.95,atrim=duration={targetDuration}[aout]"
                : $"{originalChain};{voiceChain}[voice];[original][voice]amix=inputs=2:duration=first:normalize=0:dropout_transition=0,alimiter=limit=0.95,atrim=duration={targetDuration}[aout]";
            arguments.AddRange(["-filter_complex", mix, "-map", "[aout]"]);
        }
        else if (includeOriginal)
        {
            arguments.AddRange([
                "-map", "0:a:0",
                "-af", $"aformat=sample_rates=48000:channel_layouts=stereo,volume={Invariant(settings.OriginalVolume)},apad,atrim=duration={targetDuration},alimiter=limit=0.95"
            ]);
        }
        else if (includeVoice)
        {
            arguments.AddRange([
                "-map", "1:a:0",
                "-af", $"aformat=sample_rates=48000:channel_layouts=stereo,volume={Invariant(settings.TranslatedVoiceVolume)},apad,atrim=duration={targetDuration},alimiter=limit=0.95"
            ]);
        }
        else
        {
            arguments.Add("-an");
        }

        arguments.AddRange([
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-crf", "20",
            "-pix_fmt", "yuv420p"
        ]);
        if (includeOriginal || includeVoice)
        {
            arguments.AddRange(["-c:a", "aac", "-b:a", "192k"]);
        }
        arguments.AddRange([
            "-movflags", "+faststart",
            "-metadata:s:v:0", "rotate=0",
            partialPath
        ]);
        return arguments;
    }

    private static bool ShouldIncludeOriginalAudio(bool sourceHasAudio, VietsubAudioMixSettings settings) =>
        sourceHasAudio && !settings.OriginalMuted && settings.OriginalVolume > 0;

    private static bool ShouldIncludeTranslatedVoice(string? voiceTimelinePath, VietsubAudioMixSettings settings) =>
        !string.IsNullOrWhiteSpace(voiceTimelinePath)
        && !settings.TranslatedVoiceMuted
        && settings.TranslatedVoiceVolume > 0;

    private string ResolveVoiceTimelinePath(Guid projectId, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathFullyQualified(relativePath))
        {
            throw new VietsubVideoExportException(
                VietsubVideoExportErrorCodes.VoiceInvalid,
                "Đường dẫn timeline giọng Việt không hợp lệ.");
        }
        try
        {
            return paths.GetProjectPath(
                projectId,
                relativePath.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries));
        }
        catch (InvalidOperationException exception)
        {
            throw new VietsubVideoExportException(
                VietsubVideoExportErrorCodes.VoiceInvalid,
                "Đường dẫn timeline giọng Việt nằm ngoài workspace dự án.",
                exception);
        }
    }

    private static async Task<bool> IsVoiceArtifactUsableAsync(
        string absolutePath,
        VietsubVoiceArtifact artifact,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(absolutePath);
            if (!info.Exists || info.Length != artifact.SizeBytes || info.Length <= 44) return false;
            await using var stream = new FileStream(
                absolutePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var actualHash = await SHA256.HashDataAsync(stream, cancellationToken);
            return CryptographicOperations.FixedTimeEquals(actualHash, Convert.FromHexString(artifact.Sha256));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            return false;
        }
    }

    private static string Invariant(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string NormalizeDestination(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath)
            || !Path.IsPathFullyQualified(destinationPath)
            || !string.Equals(Path.GetExtension(destinationPath), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            throw new VietsubVideoExportException(
                VietsubVideoExportErrorCodes.DestinationInvalid,
                "Tệp xuất phải là đường dẫn MP4 hợp lệ.");
        }
        try
        {
            return Path.GetFullPath(destinationPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new VietsubVideoExportException(
                VietsubVideoExportErrorCodes.DestinationInvalid,
                "Đường dẫn xuất video không hợp lệ.",
                exception);
        }
    }

    private static string EscapeSubtitleFilterPath(string path) =>
        Path.GetFullPath(path)
            .Replace("\\", "/", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort cleanup for a unique temporary output.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception)
        {
            // Best effort cleanup for a project-scoped temporary directory.
        }
    }
}

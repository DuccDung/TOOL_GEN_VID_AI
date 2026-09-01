using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_LOCAL.Vietsub.Subtitles;

internal sealed class VietsubSubtitleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

internal sealed record VietsubSubtitleTrackSummary(
    Guid TrackId,
    string DisplayName,
    string LanguageCode,
    string Source,
    int Revision,
    int CueCount,
    int TranslatedCueCount,
    int WarningCueCount,
    DateTime UpdatedAtUtc);

internal sealed record VietsubSubtitleWorkspaceSummary(
    Guid? ActiveTrackId,
    IReadOnlyList<VietsubSubtitleTrackSummary> Tracks);

internal sealed record VietsubSubtitleCueSummary(
    Guid CueId,
    int CueIndex,
    long StartMilliseconds,
    long EndMilliseconds,
    string Speaker,
    string OriginalText,
    string TranslatedText,
    bool OriginalLocked,
    bool TranslationLocked,
    string? QualityStatus,
    IReadOnlyList<string> Warnings,
    DateTime UpdatedAtUtc);

internal sealed record VietsubSubtitlePage(
    Guid TrackId,
    int TrackRevision,
    int Offset,
    int PageSize,
    int TotalCount,
    string Search,
    string Status,
    string Speaker,
    IReadOnlyList<string> Speakers,
    IReadOnlyList<VietsubSubtitleCueSummary> Cues);

internal sealed record VietsubSubtitlePageQuery(
    Guid? TrackId,
    int Offset,
    int PageSize,
    string? Search,
    string? Status,
    string? Speaker);

internal sealed partial class VietsubSubtitleService(
    VietsubAppPaths paths,
    VietsubSubtitleStore store)
{
    internal const int MaximumCueCount = 20_000;
    internal const int MaximumPageSize = 200;
    private const int MaximumPageTextCharacters = 120_000;
    private const int MaximumTextLength = 10_000;
    private const long MaximumSrtSizeBytes = 10L * 1024 * 1024;

    public async Task<VietsubSubtitleTrack> ImportSrtAsync(
        VietsubProjectManifest project,
        string sourcePath,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var file = new FileInfo(sourcePath);
        if (!file.Exists)
        {
            throw new VietsubSubtitleException("vietsub_srt_file_not_found", "Không tìm thấy tệp phụ đề.");
        }
        if (!string.Equals(file.Extension, ".srt", StringComparison.OrdinalIgnoreCase)
            || file.Length <= 0
            || file.Length > MaximumSrtSizeBytes)
        {
            throw new VietsubSubtitleException(
                "vietsub_srt_file_invalid",
                "Tệp SRT trống, quá lớn hoặc không đúng định dạng.");
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(
                file.FullName,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            throw new VietsubSubtitleException(
                "vietsub_srt_encoding_unsupported",
                "Tệp SRT phải sử dụng mã hóa UTF-8.");
        }

        var now = DateTime.UtcNow;
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "SRT đã nhập",
            LanguageCode = NormalizeLanguage(languageCode),
            Source = "IMPORTED_SRT",
            Revision = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Cues = Parse(text)
        };
        var serialized = Serialize(track.Cues, preferTranslation: false);
        var relativePath = Path.Combine("subtitles", $"imported-{track.TrackId:N}.srt");
        var artifactPath = paths.GetProjectPath(project.ProjectId, relativePath);
        var bytes = new UTF8Encoding(false).GetBytes(serialized);
        await WriteAtomicAsync(artifactPath, bytes, cancellationToken);
        track.Artifacts.Add(new VietsubSubtitleArtifact
        {
            ArtifactType = "SRT_ORIGINAL",
            TrackRevision = track.Revision,
            WorkspaceRelativePath = relativePath,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Status = VietsubSubtitleArtifactStatuses.Ready,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await store.SaveTrackAsync(project.ProjectId, track, cancellationToken);
        return track;
    }

    public async Task<VietsubSubtitleWorkspaceSummary> GetWorkspaceAsync(
        VietsubProjectManifest project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var tracks = await store.LoadTracksAsync(project.ProjectId, cancellationToken);
        var activeTrackId = project.ActiveSubtitleTrackId is Guid selected
            && tracks.Any(track => track.TrackId == selected)
                ? selected
                : tracks.FirstOrDefault()?.TrackId;
        return new(
            activeTrackId,
            tracks.Select(ToTrackSummary).ToArray());
    }

    public async Task ActivateTrackAsync(
        VietsubProjectManifest project,
        Guid trackId,
        CancellationToken cancellationToken = default)
    {
        var tracks = await store.LoadTracksAsync(project.ProjectId, cancellationToken);
        if (!tracks.Any(track => track.TrackId == trackId))
        {
            throw TrackNotFound();
        }
        project.ActiveSubtitleTrackId = trackId;
    }

    public async Task<VietsubSubtitlePage> GetPageAsync(
        VietsubProjectManifest project,
        VietsubSubtitlePageQuery query,
        CancellationToken cancellationToken = default)
    {
        var tracks = await store.LoadTracksAsync(project.ProjectId, cancellationToken);
        var track = ResolveTrack(project, tracks, query.TrackId);
        var search = NormalizeSearch(query.Search);
        var status = NormalizeStatus(query.Status);
        var speaker = NormalizeSpeakerFilter(query.Speaker);
        IEnumerable<VietsubSubtitleCue> filtered = track.Cues;
        if (search.Length > 0)
        {
            filtered = filtered.Where(cue =>
                cue.OriginalText.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || cue.TranslatedText.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || cue.Speaker.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }
        if (speaker.Length > 0)
        {
            filtered = filtered.Where(cue => string.Equals(
                cue.Speaker,
                speaker,
                StringComparison.CurrentCultureIgnoreCase));
        }
        filtered = status switch
        {
            "PENDING" => filtered.Where(cue => string.IsNullOrWhiteSpace(cue.TranslatedText)),
            "TRANSLATED" => filtered.Where(cue => !string.IsNullOrWhiteSpace(cue.TranslatedText)),
            "LOCKED" => filtered.Where(cue => cue.OriginalLocked || cue.TranslationLocked),
            "WARNING" => filtered.Where(cue => cue.Warnings.Count > 0
                || string.Equals(cue.QualityStatus, "WARNING", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cue.QualityStatus, "INVALID", StringComparison.OrdinalIgnoreCase)),
            _ => filtered
        };

        var materialized = filtered.ToArray();
        var requestedPageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, MaximumPageSize);
        var offset = Math.Clamp(query.Offset, 0, Math.Max(0, materialized.Length - 1));
        var cueIndexes = track.Cues
            .Select((cue, index) => new { cue.CueId, Index = index })
            .ToDictionary(item => item.CueId, item => item.Index);
        var page = new List<VietsubSubtitleCueSummary>(requestedPageSize);
        var textCharacters = 0;
        foreach (var cue in materialized.Skip(offset).Take(requestedPageSize))
        {
            var cueCharacters = cue.OriginalText.Length
                + cue.TranslatedText.Length
                + cue.Speaker.Length
                + cue.Warnings.Sum(warning => warning.Length);
            if (page.Count > 0 && textCharacters + cueCharacters > MaximumPageTextCharacters)
            {
                break;
            }
            page.Add(ToCueSummary(cue, cueIndexes[cue.CueId]));
            textCharacters += cueCharacters;
        }
        var effectivePageSize = page.Count > 0 ? page.Count : requestedPageSize;
        var speakers = track.Cues
            .Select(cue => cue.Speaker)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return new(
            track.TrackId,
            track.Revision,
            offset,
            effectivePageSize,
            materialized.Length,
            search,
            status,
            speaker,
            speakers,
            page);
    }

    public async Task UpdateCueAsync(
        VietsubProjectManifest project,
        Guid cueId,
        string originalText,
        string translatedText,
        string speaker,
        CancellationToken cancellationToken = default)
    {
        var track = await LoadActiveTrackAsync(project, cancellationToken);
        var cue = FindCue(track, cueId, out _);
        var original = NormalizeText(originalText);
        var translated = NormalizeText(translatedText, allowEmpty: true);
        var normalizedSpeaker = NormalizeSpeaker(speaker);
        var originalChanged = !string.Equals(cue.OriginalText, original, StringComparison.Ordinal);
        var translationChanged = !string.Equals(cue.TranslatedText, translated, StringComparison.Ordinal);
        var speakerChanged = !string.Equals(cue.Speaker, normalizedSpeaker, StringComparison.Ordinal);
        if (!originalChanged && !translationChanged && !speakerChanged)
        {
            return;
        }

        cue.OriginalText = original;
        cue.TranslatedText = translated;
        cue.Speaker = normalizedSpeaker;
        cue.OriginalLocked |= originalChanged;
        if (translationChanged)
        {
            cue.TranslationLocked = translated.Length > 0;
            cue.QualityStatus = translated.Length > 0 ? "MANUAL_REVIEWED" : null;
            cue.Warnings.Clear();
        }
        cue.UpdatedAtUtc = DateTime.UtcNow;
        await SaveMutationAsync(project.ProjectId, track, cancellationToken);
    }

    public async Task SplitCueAsync(
        VietsubProjectManifest project,
        Guid cueId,
        long positionMilliseconds,
        CancellationToken cancellationToken = default)
    {
        var track = await LoadActiveTrackAsync(project, cancellationToken);
        var cue = FindCue(track, cueId, out var index);
        if (positionMilliseconds <= cue.StartMilliseconds + 100
            || positionMilliseconds >= cue.EndMilliseconds - 100)
        {
            throw new VietsubSubtitleException(
                "vietsub_subtitle_split_position_invalid",
                "Playhead phải nằm trong cue và cách mỗi mép ít nhất 100 ms.");
        }

        var (originalLeft, originalRight) = SplitText(cue.OriginalText);
        var (translatedLeft, translatedRight) = SplitText(cue.TranslatedText);
        var now = DateTime.UtcNow;
        var right = new VietsubSubtitleCue
        {
            StartMilliseconds = positionMilliseconds,
            EndMilliseconds = cue.EndMilliseconds,
            Speaker = cue.Speaker,
            OriginalText = originalRight,
            TranslatedText = translatedRight,
            OriginalLocked = true,
            TranslationLocked = translatedRight.Length > 0,
            UpdatedAtUtc = now
        };
        cue.EndMilliseconds = positionMilliseconds;
        cue.OriginalText = originalLeft;
        cue.TranslatedText = translatedLeft;
        cue.OriginalLocked = true;
        cue.TranslationLocked = translatedLeft.Length > 0;
        cue.QualityStatus = null;
        cue.Warnings.Clear();
        cue.UpdatedAtUtc = now;
        track.Cues.Insert(index + 1, right);
        await SaveMutationAsync(project.ProjectId, track, cancellationToken);
    }

    public async Task AlignCueStartAsync(
        VietsubProjectManifest project,
        Guid cueId,
        long positionMilliseconds,
        CancellationToken cancellationToken = default)
    {
        var track = await LoadActiveTrackAsync(project, cancellationToken);
        var cue = FindCue(track, cueId, out _);
        var mediaDuration = project.SourceVideo?.Metadata.DurationSeconds * 1000m;
        if (positionMilliseconds < 0
            || positionMilliseconds >= cue.EndMilliseconds - 100
            || (mediaDuration.HasValue && positionMilliseconds >= mediaDuration.Value))
        {
            throw new VietsubSubtitleException(
                "vietsub_subtitle_align_position_invalid",
                "Vị trí căn cue không hợp lệ hoặc làm cue ngắn hơn 100 ms.");
        }
        if (cue.StartMilliseconds == positionMilliseconds)
        {
            return;
        }
        cue.StartMilliseconds = positionMilliseconds;
        cue.UpdatedAtUtc = DateTime.UtcNow;
        await SaveMutationAsync(project.ProjectId, track, cancellationToken);
    }

    public async Task<Guid> DuplicateCueAsync(
        VietsubProjectManifest project,
        Guid cueId,
        CancellationToken cancellationToken = default)
    {
        var track = await LoadActiveTrackAsync(project, cancellationToken);
        var cue = FindCue(track, cueId, out var index);
        var duration = cue.EndMilliseconds - cue.StartMilliseconds;
        var start = cue.EndMilliseconds;
        var end = start + duration;
        var mediaDuration = project.SourceVideo?.Metadata.DurationSeconds * 1000m;
        if (mediaDuration.HasValue && end > mediaDuration.Value)
        {
            end = (long)Math.Floor(mediaDuration.Value);
        }
        if (end <= start + 100)
        {
            throw new VietsubSubtitleException(
                "vietsub_subtitle_duplicate_outside_media",
                "Không còn đủ thời lượng video để nhân bản cue này.");
        }

        var copy = new VietsubSubtitleCue
        {
            StartMilliseconds = start,
            EndMilliseconds = end,
            Speaker = cue.Speaker,
            OriginalText = cue.OriginalText,
            TranslatedText = cue.TranslatedText,
            OriginalLocked = true,
            TranslationLocked = cue.TranslatedText.Length > 0,
            QualityStatus = cue.QualityStatus,
            Warnings = [.. cue.Warnings],
            UpdatedAtUtc = DateTime.UtcNow
        };
        track.Cues.Insert(index + 1, copy);
        await SaveMutationAsync(project.ProjectId, track, cancellationToken);
        return copy.CueId;
    }

    public async Task DeleteCueAsync(
        VietsubProjectManifest project,
        Guid cueId,
        CancellationToken cancellationToken = default)
    {
        var track = await LoadActiveTrackAsync(project, cancellationToken);
        _ = FindCue(track, cueId, out var index);
        track.Cues.RemoveAt(index);
        await SaveMutationAsync(project.ProjectId, track, cancellationToken);
    }

    public async Task<string> ExportSrtAsync(
        VietsubProjectManifest project,
        string destinationPath,
        bool translated,
        CancellationToken cancellationToken = default)
    {
        var track = await LoadActiveTrackAsync(project, cancellationToken);
        var content = Serialize(track.Cues, preferTranslation: translated);
        var fullPath = Path.GetFullPath(destinationPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".srt", StringComparison.OrdinalIgnoreCase))
        {
            fullPath += ".srt";
        }
        var destinationDirectory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
        {
            throw new VietsubSubtitleException(
                "vietsub_srt_destination_invalid",
                "Thư mục xuất SRT không tồn tại.");
        }

        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(content);
        await WriteAtomicAsync(fullPath, bytes, cancellationToken);

        var artifactType = translated ? "SRT_TRANSLATED" : "SRT_ORIGINAL";
        var relativePath = Path.Combine(
            "subtitles",
            $"export-{track.TrackId:N}-r{track.Revision}-{(translated ? "translated" : "original")}.srt");
        var internalPath = paths.GetProjectPath(project.ProjectId, relativePath);
        var internalBytes = new UTF8Encoding(false).GetBytes(content);
        await WriteAtomicAsync(internalPath, internalBytes, cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var existing in track.Artifacts.Where(item =>
                     string.Equals(item.ArtifactType, artifactType, StringComparison.OrdinalIgnoreCase)
                     && item.Status == VietsubSubtitleArtifactStatuses.Ready))
        {
            existing.Status = VietsubSubtitleArtifactStatuses.Stale;
            existing.UpdatedAtUtc = now;
        }
        track.Artifacts.Add(new VietsubSubtitleArtifact
        {
            ArtifactType = artifactType,
            TrackRevision = track.Revision,
            WorkspaceRelativePath = relativePath,
            Sha256 = Convert.ToHexString(SHA256.HashData(internalBytes)).ToLowerInvariant(),
            Status = VietsubSubtitleArtifactStatuses.Ready,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        track.UpdatedAtUtc = now;
        await store.SaveTrackAsync(project.ProjectId, track, cancellationToken);
        return Path.GetFileName(fullPath);
    }

    internal static List<VietsubSubtitleCue> Parse(string srt)
    {
        if (string.IsNullOrWhiteSpace(srt))
        {
            throw new VietsubSubtitleException("vietsub_srt_empty", "Tệp SRT không có nội dung.");
        }
        var normalized = srt
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        var blocks = BlankLineRegex().Split(normalized);
        if (blocks.Length > MaximumCueCount)
        {
            throw new VietsubSubtitleException(
                "vietsub_srt_too_many_cues",
                "Tệp SRT có quá nhiều phân đoạn.");
        }

        var cues = new List<VietsubSubtitleCue>(blocks.Length);
        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            var timelineIndex = Array.FindIndex(lines, line => line.Contains("-->", StringComparison.Ordinal));
            if (timelineIndex < 0 || timelineIndex >= lines.Length - 1)
            {
                throw TimelineInvalid();
            }
            var timeline = TimelineRegex().Match(lines[timelineIndex].Trim());
            if (!timeline.Success)
            {
                throw TimelineInvalid();
            }
            var start = ParseTimestamp(timeline.Groups[1].Value);
            var end = ParseTimestamp(timeline.Groups[2].Value);
            if (end <= start)
            {
                throw TimelineInvalid();
            }
            cues.Add(new VietsubSubtitleCue
            {
                StartMilliseconds = start,
                EndMilliseconds = end,
                OriginalText = NormalizeText(string.Join('\n', lines.Skip(timelineIndex + 1))),
                Speaker = "speaker_1"
            });
        }
        return cues;
    }

    internal static string Serialize(
        IReadOnlyList<VietsubSubtitleCue> cues,
        bool preferTranslation)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < cues.Count; index++)
        {
            var cue = cues[index];
            if (cue.StartMilliseconds < 0 || cue.EndMilliseconds <= cue.StartMilliseconds)
            {
                throw TimelineInvalid();
            }
            var text = preferTranslation && !string.IsNullOrWhiteSpace(cue.TranslatedText)
                ? cue.TranslatedText
                : cue.OriginalText;
            builder.Append(index + 1).AppendLine();
            builder.Append(FormatTimestamp(cue.StartMilliseconds))
                .Append(" --> ")
                .Append(FormatTimestamp(cue.EndMilliseconds))
                .AppendLine();
            builder.AppendLine(text.Trim());
            if (index < cues.Count - 1)
            {
                builder.AppendLine();
            }
        }
        return builder.ToString();
    }

    private async Task<VietsubSubtitleTrack> LoadActiveTrackAsync(
        VietsubProjectManifest project,
        CancellationToken cancellationToken)
    {
        var tracks = await store.LoadTracksAsync(project.ProjectId, cancellationToken);
        return ResolveTrack(project, tracks, trackId: null);
    }

    private static VietsubSubtitleTrack ResolveTrack(
        VietsubProjectManifest project,
        IReadOnlyList<VietsubSubtitleTrack> tracks,
        Guid? trackId)
    {
        var selectedId = trackId ?? project.ActiveSubtitleTrackId;
        if (selectedId is Guid id)
        {
            var selected = tracks.SingleOrDefault(track => track.TrackId == id);
            if (selected is not null)
            {
                return selected;
            }
        }
        throw TrackNotFound();
    }

    private async Task SaveMutationAsync(
        Guid projectId,
        VietsubSubtitleTrack track,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        track.Revision++;
        track.UpdatedAtUtc = now;
        foreach (var artifact in track.Artifacts.Where(item =>
                     item.Status == VietsubSubtitleArtifactStatuses.Ready))
        {
            artifact.Status = VietsubSubtitleArtifactStatuses.Stale;
            artifact.UpdatedAtUtc = now;
        }
        await store.SaveTrackAsync(projectId, track, cancellationToken);
    }

    private static VietsubSubtitleCue FindCue(
        VietsubSubtitleTrack track,
        Guid cueId,
        out int index)
    {
        index = track.Cues.FindIndex(cue => cue.CueId == cueId);
        if (index < 0)
        {
            throw new VietsubSubtitleException(
                "vietsub_subtitle_cue_not_found",
                "Không tìm thấy phân đoạn phụ đề.");
        }
        return track.Cues[index];
    }

    private static VietsubSubtitleTrackSummary ToTrackSummary(VietsubSubtitleTrack track) =>
        new(
            track.TrackId,
            track.DisplayName,
            track.LanguageCode,
            track.Source,
            track.Revision,
            track.Cues.Count,
            track.Cues.Count(cue => !string.IsNullOrWhiteSpace(cue.TranslatedText)),
            track.Cues.Count(cue => cue.Warnings.Count > 0),
            track.UpdatedAtUtc);

    private static VietsubSubtitleCueSummary ToCueSummary(VietsubSubtitleCue cue, int cueIndex) =>
        new(
            cue.CueId,
            cueIndex,
            cue.StartMilliseconds,
            cue.EndMilliseconds,
            cue.Speaker,
            cue.OriginalText,
            cue.TranslatedText,
            cue.OriginalLocked,
            cue.TranslationLocked,
            cue.QualityStatus,
            cue.Warnings,
            cue.UpdatedAtUtc);

    private static async Task WriteAtomicAsync(
        string destinationPath,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporaryPath = destinationPath + ".partial";
        TryDelete(temporaryPath);
        try
        {
            await using var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
            stream.Close();
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
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

    private static (string Left, string Right) SplitText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (string.Empty, string.Empty);
        }
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2)
        {
            return (text.Trim(), text.Trim());
        }
        var split = (words.Length + 1) / 2;
        return (string.Join(' ', words[..split]), string.Join(' ', words[split..]));
    }

    private static string NormalizeText(string? value, bool allowEmpty = false)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Trim();
        if ((!allowEmpty && normalized.Length == 0) || normalized.Length > MaximumTextLength)
        {
            throw new VietsubSubtitleException(
                "vietsub_subtitle_text_invalid",
                "Nội dung phụ đề trống hoặc vượt giới hạn.");
        }
        return normalized;
    }

    private static string NormalizeLanguage(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length is >= 2 and <= 20 && normalized.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
                ? normalized
                : "und";
    }

    private static string NormalizeSpeaker(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > 80 || normalized.Any(char.IsControl))
        {
            throw new VietsubSubtitleException(
                "vietsub_subtitle_speaker_invalid",
                "Tên người nói phải có từ 1 đến 80 ký tự hợp lệ.");
        }
        return normalized;
    }

    private static string NormalizeSearch(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length > 200 || normalized.Any(character => character == '\0'))
        {
            throw new VietsubSubtitleException(
                "vietsub_subtitle_filter_invalid",
                "Từ khóa tìm kiếm phụ đề không hợp lệ.");
        }
        return normalized;
    }

    private static string NormalizeStatus(string? value)
    {
        var normalized = (value ?? "ALL").Trim().ToUpperInvariant();
        return normalized is "ALL" or "PENDING" or "TRANSLATED" or "LOCKED" or "WARNING"
            ? normalized
            : "ALL";
    }

    private static string NormalizeSpeakerFilter(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= 80 && !normalized.Any(char.IsControl)
            ? normalized
            : throw new VietsubSubtitleException(
                "vietsub_subtitle_filter_invalid",
                "Bộ lọc người nói không hợp lệ.");
    }

    private static long ParseTimestamp(string value)
    {
        var parts = value.Split([':', ',', '.']);
        if (parts.Length != 4
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            || minutes > 59
            || seconds > 59
            || milliseconds > 999)
        {
            throw TimelineInvalid();
        }
        return ((hours * 60L + minutes) * 60L + seconds) * 1000L + milliseconds;
    }

    private static string FormatTimestamp(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(milliseconds);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";
    }

    private static VietsubSubtitleException TimelineInvalid() =>
        new("vietsub_srt_timeline_invalid", "Timestamp SRT không hợp lệ.");

    private static VietsubSubtitleException TrackNotFound() =>
        new("vietsub_subtitle_track_not_found", "Không tìm thấy track phụ đề đang chọn.");

    [GeneratedRegex(@"\n\s*\n+", RegexOptions.Compiled)]
    private static partial Regex BlankLineRegex();

    [GeneratedRegex(@"^(\d{1,3}:\d{2}:\d{2}[,.]\d{3})\s*-->\s*(\d{1,3}:\d{2}:\d{2}[,.]\d{3})(?:\s+.*)?$", RegexOptions.Compiled)]
    private static partial Regex TimelineRegex();
}

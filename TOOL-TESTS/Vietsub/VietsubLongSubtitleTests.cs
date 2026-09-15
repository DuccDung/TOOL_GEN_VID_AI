using System.Diagnostics;
using Microsoft.Data.Sqlite;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;
using TOOL_LOCAL.Vietsub.Translation;
using Xunit.Abstractions;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubLongSubtitleTests(ITestOutputHelper output) : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vs-cues-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TenThousandCues_PageFilterAndContextMatchUnicodeAndStableOrder()
    {
        var paths = new VietsubAppPaths(root);
        var store = new VietsubSubtitleStore(paths);
        var service = new VietsubSubtitleService(paths, store);
        var project = new VietsubProjectManifest { ProjectId = Guid.NewGuid() };
        var track = new VietsubSubtitleTrack { DisplayName = "10,000 cues", Source = "PADDLE_OCR_LOCAL", LanguageCode = "en" };
        for (var i = 0; i < 10_000; i++) track.Cues.Add(new()
        {
            StartMilliseconds = i / 2 * 1400L, EndMilliseconds = i / 2 * 1400L + 600,
            OriginalText = i % 3 == 0 ? "ĐIỆN ẢNH 中文" : "Original subtitle " + i,
            TranslatedText = i % 2 == 0 ? "\u2003" : "Bản dịch " + i,
            Speaker = i % 3 == 0 ? "ÁNH" : "người kể", TranslationLocked = i % 7 == 0,
            Warnings = i % 11 == 0 ? ["Cần soát"] : [], VoiceEnabled = i % 5 != 0
        });
        project.ActiveSubtitleTrackId = track.TrackId;
        await store.SaveTrackAsync(project.ProjectId, track);
        var durations = new List<double>();
        var previousDurations = new List<double>();
        for (var pass = 0; pass < 20; pass++)
        {
            var timer = Stopwatch.StartNew();
            var page = await service.GetPageAsync(project, new(null, 9950, 50, null, null, null));
            durations.Add(timer.Elapsed.TotalMilliseconds);
            Assert.Equal(track.Cues.Skip(9950).Select(c => c.CueId), page.Cues.Select(c => c.CueId));
            Assert.Equal(10_000, page.TotalCount);
            Assert.Equal(9950, page.Cues[0].CueIndex);
            timer.Restart();
            _ = await store.LoadTracksAsync(project.ProjectId);
            previousDurations.Add(timer.Elapsed.TotalMilliseconds);
        }
        foreach (var status in new[] { "ALL", "PENDING", "TRANSLATED", "LOCKED", "WARNING" })
        {
            var expected = track.Cues.Where(c => c.OriginalText.Contains("điện", StringComparison.CurrentCultureIgnoreCase)
                && string.Equals(c.Speaker, "ánh", StringComparison.CurrentCultureIgnoreCase)
                && (status switch
                {
                    "PENDING" => string.IsNullOrWhiteSpace(c.TranslatedText),
                    "TRANSLATED" => !string.IsNullOrWhiteSpace(c.TranslatedText),
                    "LOCKED" => c.OriginalLocked || c.TranslationLocked,
                    "WARNING" => c.Warnings.Count > 0, _ => true
                })).ToArray();
            var page = await service.GetPageAsync(project, new(null, 0, 50, "điện", status, "ánh"));
            Assert.Equal(expected.Length, page.TotalCount);
            Assert.Equal(expected.Take(50).Select(c => c.CueId), page.Cues.Select(c => c.CueId));
        }
        foreach (var index in new[] { 0, 1, 4999, 5000, 9999 })
        {
            var context = (await store.LoadCueContextAsync(project.ProjectId, track.TrackId, track.Cues[index].CueId, 3, default)).ToArray();
            var localIndex = Array.FindIndex(context, c => c.CueId == track.Cues[index].CueId);
            Assert.InRange(context.Length, 1, 7);
            Assert.Equal(VietsubTranslationFingerprintBuilder.BuildCueFingerprint(track.Cues[index], index, track.Cues, 3, "config"),
                VietsubTranslationFingerprintBuilder.BuildCueFingerprint(context[localIndex], localIndex, context, 3, "config"));
        }
        var summary = Assert.Single(await store.LoadSummariesAsync(project.ProjectId, default));
        Assert.Equal(5000, summary.TranslatedCueCount);
        Assert.Equal(8000, summary.VoiceEnabledCueCount);
        Assert.Equal(track.Cues.Count(c => c.Warnings.Count > 0), summary.WarningCueCount);
        var window = await service.GetTimelineWindowAsync(project, new(null, 0, 7_200_000, 500));
        Assert.True(window.Truncated);
        Assert.Equal(500, window.Cues.Count);
        output.WriteLine($"10,000 cues / 20 queries: paged P50={Percentile(durations, .5):F2}ms P95={Percentile(durations, .95):F2}ms; full-load P50={Percentile(previousDurations, .5):F2}ms P95={Percentile(previousDurations, .95):F2}ms.");
    }

    [Fact]
    public async Task CueEditsAndOcrCheckpoints_WriteOnlyDeltaAndRejectStaleRevisionAtomically()
    {
        var paths = new VietsubAppPaths(root);
        var store = new VietsubSubtitleStore(paths);
        var projectId = Guid.NewGuid();
        var track = new VietsubSubtitleTrack { DisplayName = "OCR delta", Source = "PADDLE_OCR_LOCAL", LanguageCode = "en", Cues =
            [new() { OriginalText = "first", StartMilliseconds = 0, EndMilliseconds = 900 },
             new() { OriginalText = "last", StartMilliseconds = 3000, EndMilliseconds = 3900, TranslationLocked = true, TranslatedText = "Giữ nguyên" }] };
        await store.SaveTrackAsync(projectId, track);
        var edit = (await store.LoadCueEditAsync(projectId, track.TrackId, track.Cues[0].CueId, default))!;
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.GetProjectPath(projectId, "project.db"), Pooling = false }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE test_writes (operation TEXT);
            CREATE TRIGGER test_cue_update AFTER UPDATE OF original_text,translated_text ON subtitle_cues BEGIN INSERT INTO test_writes VALUES('UPDATE'); END;
            CREATE TRIGGER test_cue_insert AFTER INSERT ON subtitle_cues BEGIN INSERT INTO test_writes VALUES('INSERT'); END;
            CREATE TRIGGER test_no_delete BEFORE DELETE ON subtitle_cues BEGIN SELECT RAISE(ABORT, 'Unexpected full-track rewrite'); END;
            """;
        await command.ExecuteNonQueryAsync();
        edit.Cue.TranslatedText = "Sửa tay"; edit.Cue.TranslationLocked = true;
        Assert.True(await store.SaveCueEditAsync(projectId, edit, default));
        Assert.False(await store.SaveCueEditAsync(projectId, edit, default));
        Assert.False(await store.SaveCueEditAsync(projectId, edit with { TrackId = Guid.NewGuid() }, default));
        command.CommandText = "SELECT COUNT(*) FROM test_writes;";
        Assert.Equal(1L, await command.ExecuteScalarAsync());

        var jobs = new VietsubJobStore(paths, store);
        var job = await jobs.CreateAsync(projectId, VietsubJobTypes.OcrLocal, ["OCR_RECOGNIZE"]);
        await jobs.BindOutputTrackAsync(projectId, job.Id, track.TrackId);
        await jobs.TransitionAsync(projectId, job.Id, VietsubJobStatus.Running, "STARTED");
        track = Assert.Single(await store.LoadTracksAsync(projectId));
        var revision = track.Revision;
        var newCue = new VietsubSubtitleCue { OriginalText = "middle", StartMilliseconds = 1500, EndMilliseconds = 2400 };
        track.Cues.Insert(1, newCue); track.Revision++;
        // A stale in-memory snapshot must not overwrite manual/locked content in existing rows.
        track.Cues[0].TranslatedText = "stale";
        await store.SaveOcrDeltaCheckpointAsync(projectId, track, job.Id, "{\"frame\":2400}", revision, new HashSet<Guid> { newCue.CueId }, default);
        var saved = Assert.Single(await store.LoadTracksAsync(projectId));
        Assert.Equal(new[] { "Sửa tay", "", "Giữ nguyên" }, saved.Cues.Select(c => c.TranslatedText));
        Assert.Equal("{\"frame\":2400}", (await jobs.GetAsync(projectId, job.Id))!.CheckpointJson);
        Assert.Equal(2L, await command.ExecuteScalarAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveOcrDeltaCheckpointAsync(projectId, track, job.Id, "{}", revision, new HashSet<Guid> { newCue.CueId }, default));
        Assert.Equal(2L, await command.ExecuteScalarAsync());
        Assert.Equal(saved.Revision, Assert.Single(await store.LoadTracksAsync(projectId)).Revision);
    }

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var ordered = values.Order().ToArray();
        return ordered[(int)Math.Ceiling(ordered.Length * percentile) - 1];
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

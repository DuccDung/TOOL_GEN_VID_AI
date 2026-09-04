using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TOOL_LOCAL.Vietsub;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubSubtitleTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "VideoMaker-Vietsub-Subtitle-Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ParseAndSerialize_PreservesUnicodeMultilineAndLongHours()
    {
        const string srt = "1\r\n00:00:01,250 --> 00:00:03,900\r\nXin chào.\r\nDòng thứ hai.\r\n\r\n2\r\n25:01:02.003 --> 25:01:04.010\r\n日本語テスト";

        var cues = VietsubSubtitleService.Parse(srt);
        var serialized = VietsubSubtitleService.Serialize(cues, preferTranslation: false);
        var reparsed = VietsubSubtitleService.Parse(serialized);

        Assert.Equal(2, cues.Count);
        Assert.Equal(1_250, cues[0].StartMilliseconds);
        Assert.Equal("Xin chào.\nDòng thứ hai.", cues[0].OriginalText);
        Assert.Equal(cues[1].StartMilliseconds, reparsed[1].StartMilliseconds);
        Assert.Equal("日本語テスト", reparsed[1].OriginalText);
    }

    [Fact]
    public void Parse_RejectsInvalidTimeline()
    {
        var exception = Assert.Throws<VietsubSubtitleException>(() =>
            VietsubSubtitleService.Parse("1\n00:00:05,000 --> 00:00:04,000\nSai timestamp"));

        Assert.Equal("vietsub_srt_timeline_invalid", exception.Code);
    }

    [Fact]
    public async Task ImportEditAndExport_PersistsArtifactsLocksAndVietnameseText()
    {
        var (paths, subtitleStore, projectStore, service) = CreateServices();
        var project = await projectStore.CreateAsync(Guid.NewGuid(), "owner", "SRT workflow");
        var sourcePath = Path.Combine(_root, "nguồn.srt");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(
            sourcePath,
            "1\n00:00:00,500 --> 00:00:02,000\nWelcome.\n",
            new UTF8Encoding(false));

        var track = await service.ImportSrtAsync(project, sourcePath, "en");
        project.ActiveSubtitleTrackId = track.TrackId;
        await projectStore.SaveAsync(project);
        var importedArtifact = Assert.Single(track.Artifacts);
        Assert.Equal(VietsubSubtitleArtifactStatuses.Ready, importedArtifact.Status);
        Assert.True(File.Exists(paths.GetProjectPath(project.ProjectId, importedArtifact.WorkspaceRelativePath)));

        var cue = Assert.Single(track.Cues);
        await service.UpdateCueAsync(
            project,
            cue.CueId,
            "Welcome.",
            "Chào mừng bạn.",
            "narrator");
        var updated = Assert.Single(await subtitleStore.LoadTracksAsync(project.ProjectId));
        var updatedCue = Assert.Single(updated.Cues);
        Assert.Equal(2, updated.Revision);
        Assert.True(updatedCue.TranslationLocked);
        Assert.Equal("MANUAL_REVIEWED", updatedCue.QualityStatus);
        Assert.Equal(VietsubSubtitleArtifactStatuses.Stale, Assert.Single(updated.Artifacts).Status);

        var destination = Path.Combine(_root, "output.srt");
        var exportedName = await service.ExportSrtAsync(project, destination, translated: true);
        var exported = await File.ReadAllTextAsync(destination, Encoding.UTF8);
        var reopened = Assert.Single(await subtitleStore.LoadTracksAsync(project.ProjectId));

        Assert.Equal("output.srt", exportedName);
        Assert.Contains("Chào mừng bạn.", exported);
        Assert.DoesNotContain("Welcome.", exported);
        Assert.DoesNotContain(reopened.Artifacts, artifact =>
            artifact.Status == VietsubSubtitleArtifactStatuses.Ready
            && artifact.TrackRevision != reopened.Revision);
        Assert.Contains(reopened.Artifacts, artifact =>
            artifact.ArtifactType == "SRT_TRANSLATED"
            && artifact.Status == VietsubSubtitleArtifactStatuses.Ready);
        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public async Task TimelineEdits_AreValidatedPagedAndPersisted()
    {
        var (_, subtitleStore, projectStore, service) = CreateServices();
        var project = await projectStore.CreateAsync(Guid.NewGuid(), "owner", "Timeline edits");
        project.SourceVideo = new VietsubMediaReference
        {
            Metadata = new VietsubMediaMetadata { DurationSeconds = 12 }
        };
        var cue = new VietsubSubtitleCue
        {
            StartMilliseconds = 0,
            EndMilliseconds = 4_000,
            OriginalText = "one two three four",
            TranslatedText = "một hai ba bốn"
        };
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "English",
            LanguageCode = "en",
            Source = "IMPORTED_SRT",
            Cues = [cue]
        };
        await subtitleStore.SaveTrackAsync(project.ProjectId, track);
        project.ActiveSubtitleTrackId = track.TrackId;
        await projectStore.SaveAsync(project);

        await service.SplitCueAsync(project, cue.CueId, 2_000);
        var split = Assert.Single(await subtitleStore.LoadTracksAsync(project.ProjectId));
        Assert.Equal(2, split.Cues.Count);
        Assert.Equal("one two", split.Cues[0].OriginalText);
        Assert.Equal("three four", split.Cues[1].OriginalText);

        var right = split.Cues[1];
        await service.AlignCueStartAsync(project, right.CueId, 2_500);
        var duplicateId = await service.DuplicateCueAsync(project, right.CueId);
        await service.DeleteCueAsync(project, cue.CueId);
        var page = await service.GetPageAsync(
            project,
            new VietsubSubtitlePageQuery(null, 0, 50, "ba", "TRANSLATED", "speaker_1"));

        Assert.Equal(2, page.TotalCount);
        Assert.Contains(page.Cues, item => item.CueId == duplicateId);
        Assert.All(page.Cues, item => Assert.True(item.TranslationLocked));
        Assert.True(page.TrackRevision >= 5);
    }

    [Fact]
    public async Task TimelineWindow_IsBoundedAndTimingUpdateRejectsStaleRevision()
    {
        var (_, subtitleStore, projectStore, service) = CreateServices();
        var project = await projectStore.CreateAsync(Guid.NewGuid(), "owner", "Windowed timeline");
        project.SourceVideo = new VietsubMediaReference
        {
            Metadata = new VietsubMediaMetadata { DurationSeconds = 60 }
        };
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "Window",
            LanguageCode = "en",
            Source = "IMPORTED_SRT",
            Cues = Enumerable.Range(0, 10)
                .Select(index => new VietsubSubtitleCue
                {
                    StartMilliseconds = index * 1_000,
                    EndMilliseconds = index * 1_000 + 900,
                    OriginalText = new string('x', 1_000),
                    TranslatedText = index % 2 == 0 ? $"Bản dịch {index}" : string.Empty,
                    Speaker = "speaker_1"
                })
                .ToList()
        };
        await subtitleStore.SaveTrackAsync(project.ProjectId, track);
        project.ActiveSubtitleTrackId = track.TrackId;
        await projectStore.SaveAsync(project);

        var timeline = await service.GetTimelineWindowAsync(
            project,
            new VietsubTimelineWindowQuery(track.TrackId, 1_500, 7_500, 3));

        Assert.Equal(track.Revision, timeline.TrackRevision);
        Assert.True(timeline.Truncated);
        Assert.Equal(3, timeline.Cues.Count);
        Assert.All(timeline.Cues, cue =>
        {
            Assert.True(cue.StartMilliseconds < 7_500);
            Assert.True(cue.EndMilliseconds > 1_500);
            Assert.InRange(cue.PreviewText.Length, 1, 200);
        });

        var cueToMove = timeline.Cues[0];
        var nextRevision = await service.UpdateCueTimingAsync(
            project,
            track.TrackId,
            cueToMove.CueId,
            timeline.TrackRevision,
            2_050,
            2_950);
        Assert.Equal(timeline.TrackRevision + 1, nextRevision);
        var saved = Assert.Single(await subtitleStore.LoadTracksAsync(project.ProjectId));
        var moved = Assert.Single(saved.Cues, cue => cue.CueId == cueToMove.CueId);
        Assert.Equal(2_050, moved.StartMilliseconds);
        Assert.Equal(2_950, moved.EndMilliseconds);

        var stale = await Assert.ThrowsAsync<VietsubSubtitleException>(() =>
            service.UpdateCueTimingAsync(
                project,
                track.TrackId,
                cueToMove.CueId,
                timeline.TrackRevision,
                2_100,
                3_000));
        Assert.Equal("vietsub_timeline_edit_conflict", stale.Code);
    }

    [Fact]
    public async Task Bridge_TimelineWindowAndCueUpdate_UseRevisionSafeContracts()
    {
        var (_, subtitleStore, projectStore, service) = CreateServices();
        var organizationId = Guid.NewGuid();
        const string owner = "timeline-contract-owner";
        var project = await projectStore.CreateAsync(organizationId, owner, "Timeline contract");
        project.SourceVideo = new VietsubMediaReference
        {
            Metadata = new VietsubMediaMetadata { DurationSeconds = 10 }
        };
        var cue = new VietsubSubtitleCue
        {
            StartMilliseconds = 1_000,
            EndMilliseconds = 2_000,
            OriginalText = "Hello",
            TranslatedText = "Xin chào"
        };
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "English",
            LanguageCode = "en",
            Source = "IMPORTED_SRT",
            Cues = [cue]
        };
        await subtitleStore.SaveTrackAsync(project.ProjectId, track);
        project.ActiveSubtitleTrackId = track.TrackId;
        await projectStore.SaveAsync(project);
        var responses = new List<string>();
        using var bridge = new VietsubWebBridge(
            true,
            responses.Add,
            projectStore,
            () => new VietsubUserContext(owner, organizationId),
            subtitleService: service);
        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.project.open",
            requestId = "open-timeline-contract",
            payload = new { projectId = project.ProjectId }
        }));
        responses.Clear();

        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.timeline.window.get",
            requestId = "timeline-window",
            payload = new
            {
                trackId = track.TrackId,
                windowStartMilliseconds = 0,
                windowEndMilliseconds = 5_000,
                maximumCues = 50
            }
        }));
        var windowResponse = Assert.Single(responses);
        using var windowJson = JsonDocument.Parse(windowResponse);
        var windowPayload = windowJson.RootElement.GetProperty("payload");
        Assert.Equal(track.Revision, windowPayload.GetProperty("trackRevision").GetInt32());
        Assert.Single(windowPayload.GetProperty("cues").EnumerateArray());
        Assert.DoesNotContain(_root, windowResponse, StringComparison.OrdinalIgnoreCase);
        responses.Clear();

        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.timeline.cue.update",
            requestId = "timeline-update",
            payload = new
            {
                trackId = track.TrackId,
                cueId = cue.CueId,
                expectedTrackRevision = track.Revision,
                startMilliseconds = 1_250,
                endMilliseconds = 2_250
            }
        }));

        Assert.Contains(responses, response => response.Contains("vietsub.subtitle.changed", StringComparison.Ordinal));
        Assert.Contains(responses, response => response.Contains("vietsub.operation.completed", StringComparison.Ordinal));
        Assert.DoesNotContain(responses, response => response.Contains("vietsub.error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PageAndBridge_ClampLargeCuePayloadWithoutLeakingLocalPaths()
    {
        var (_, subtitleStore, projectStore, service) = CreateServices();
        var organizationId = Guid.NewGuid();
        const string owner = "large-cue-owner";
        var project = await projectStore.CreateAsync(organizationId, owner, "Large cues");
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "Large",
            LanguageCode = "en",
            Source = "IMPORTED_SRT",
            Cues = Enumerable.Range(0, 1_000)
                .Select(index => new VietsubSubtitleCue
                {
                    StartMilliseconds = index * 1_100L,
                    EndMilliseconds = index * 1_100L + 1_000,
                    OriginalText = index < 30 ? new string('x', 5_000) : $"Source cue {index}",
                    TranslatedText = index % 2 == 0 ? $"Bản dịch {index}" : string.Empty,
                    Speaker = index % 3 == 0 ? "speaker_a" : "speaker_b"
                })
                .ToList()
        };
        await subtitleStore.SaveTrackAsync(project.ProjectId, track);
        project.ActiveSubtitleTrackId = track.TrackId;
        await projectStore.SaveAsync(project);
        var responses = new List<string>();
        using var bridge = new VietsubWebBridge(
            true,
            responses.Add,
            projectStore,
            () => new VietsubUserContext(owner, organizationId),
            subtitleService: service);
        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.project.open",
            requestId = "open-large",
            payload = new { projectId = project.ProjectId }
        }));
        responses.Clear();

        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.subtitle.page.get",
            requestId = "page-large",
            payload = new
            {
                trackId = track.TrackId,
                offset = 0,
                pageSize = 10_000,
                search = "",
                status = "ALL",
                speaker = ""
            }
        }));

        var response = Assert.Single(responses);
        using var json = JsonDocument.Parse(response);
        var payload = json.RootElement.GetProperty("payload");
        Assert.InRange(payload.GetProperty("pageSize").GetInt32(), 1, 200);
        Assert.Equal(1_000, payload.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            payload.GetProperty("pageSize").GetInt32(),
            payload.GetProperty("cues").GetArrayLength());
        Assert.True(response.Length < 256 * 1024);
        Assert.DoesNotContain(_root, response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("project.db", response, StringComparison.OrdinalIgnoreCase);
        responses.Clear();

        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.timeline.window.get",
            requestId = "timeline-large",
            payload = new
            {
                trackId = track.TrackId,
                windowStartMilliseconds = 0,
                windowEndMilliseconds = 2_000_000,
                maximumCues = 10_000
            }
        }));
        var timelineResponse = Assert.Single(responses);
        using var timelineJson = JsonDocument.Parse(timelineResponse);
        var timelinePayload = timelineJson.RootElement.GetProperty("payload");
        Assert.True(timelinePayload.GetProperty("truncated").GetBoolean());
        Assert.Equal(500, timelinePayload.GetProperty("cues").GetArrayLength());
        Assert.True(timelineResponse.Length < 256 * 1024);
        Assert.DoesNotContain(_root, timelineResponse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubtitleStore_MigratesVersion1DatabaseToArtifactAwareSchema()
    {
        var paths = new VietsubAppPaths(_root);
        var projectId = Guid.NewGuid();
        paths.CreateProjectDirectories(projectId);
        var databasePath = paths.GetProjectPath(projectId, "project.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE schema_info (schema_version INTEGER NOT NULL);
                INSERT INTO schema_info(schema_version) VALUES(1);
                CREATE TABLE subtitle_tracks (
                    track_id TEXT NOT NULL PRIMARY KEY,
                    display_name TEXT NOT NULL,
                    language_code TEXT NOT NULL,
                    source TEXT NOT NULL,
                    revision INTEGER NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );
                CREATE TABLE subtitle_cues (
                    cue_id TEXT NOT NULL PRIMARY KEY,
                    track_id TEXT NOT NULL,
                    cue_index INTEGER NOT NULL,
                    start_ms INTEGER NOT NULL,
                    end_ms INTEGER NOT NULL,
                    speaker TEXT NOT NULL,
                    original_text TEXT NOT NULL,
                    translated_text TEXT NOT NULL,
                    original_locked INTEGER NOT NULL,
                    translation_locked INTEGER NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    FOREIGN KEY(track_id) REFERENCES subtitle_tracks(track_id) ON DELETE CASCADE
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var store = new VietsubSubtitleStore(paths);
        await store.InitializeAsync(projectId);
        await using var verify = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await verify.OpenAsync();
        await using var version = verify.CreateCommand();
        version.CommandText = "SELECT schema_version FROM schema_info LIMIT 1;";
        Assert.Equal(2L, Convert.ToInt64(await version.ExecuteScalarAsync()));
        await using var columns = verify.CreateCommand();
        columns.CommandText = """
            SELECT COUNT(*) FROM pragma_table_info('subtitle_cues')
            WHERE name IN ('quality_status', 'warning_json');
            """;
        Assert.Equal(2L, Convert.ToInt64(await columns.ExecuteScalarAsync()));
        await using var artifacts = verify.CreateCommand();
        artifacts.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='subtitle_artifacts';";
        Assert.Equal(1L, Convert.ToInt64(await artifacts.ExecuteScalarAsync()));
    }

    private (VietsubAppPaths Paths, VietsubSubtitleStore Subtitles, VietsubProjectStore Projects, VietsubSubtitleService Service)
        CreateServices()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var projects = new VietsubProjectStore(paths, subtitles);
        return (paths, subtitles, projects, new VietsubSubtitleService(paths, subtitles));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

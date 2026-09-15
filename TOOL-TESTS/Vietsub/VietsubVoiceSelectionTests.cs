using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubVoiceSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vietsub-selection-{Guid.NewGuid():N}");

    [Fact]
    public void VoiceSettings_AllowOnlyPinnedPiperAndKokoroVoices()
    {
        Assert.Equal(15, VietsubVoiceCatalog.Items.Count);
        foreach (var voice in VietsubVoiceModelCatalog.Voices)
        {
            var settings = new VietsubVoiceSettings {
                EngineId = VietsubVoiceEngines.Kokoro,
                ModelId = VietsubVoiceModelCatalog.ModelId,
                VoiceId = voice.VoiceId
            };
            settings.Normalize();
            Assert.Equal(voice.VoiceId, settings.VoiceId);
            Assert.Contains(VietsubVoiceCatalog.Items, item => item.VoiceId == voice.VoiceId);
        }
        Assert.Throws<InvalidDataException>(() => new VietsubVoiceSettings {
            EngineId = VietsubVoiceEngines.Kokoro,
            ModelId = VietsubVoiceModelCatalog.ModelId,
            VoiceId = "kokoro-vi:unknown"
        }.Normalize());
        Assert.Throws<InvalidDataException>(() => new VietsubVoiceSettings {
            EngineId = VietsubVoiceEngines.Piper,
            ModelId = VietsubVoiceCatalog.PiperModelId,
            VoiceId = VietsubVoiceModelCatalog.Voices[0].VoiceId
        }.Normalize());
    }

    [Fact]
    public async Task Workspace_HidesOldTimelineWhenSelectedVoiceChanges()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var projectId = Guid.NewGuid();
        var cue = Cue(0, 800, "Xin chào");
        var track = new VietsubSubtitleTrack { DisplayName = "Fixture", Cues = [cue] };
        await subtitles.SaveTrackAsync(projectId, track);
        var audioPath = paths.GetProjectPath(projectId, "voice", "timeline.wav");
        VietsubVoiceBoundaryTests.WriteWave(audioPath, 16000, 1000, 0, 1000);
        var now = DateTime.UtcNow;
        var artifact = new VietsubVoiceArtifact(
            Guid.NewGuid(), track.TrackId, track.Revision, VietsubVoiceArtifactKinds.Timeline,
            null, Path.GetRelativePath(paths.GetProjectDirectory(projectId), audioPath),
            new FileInfo(audioPath).Length,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(audioPath))).ToLowerInvariant(),
            new string('a', 64), VietsubVoiceEngines.Piper,
            VietsubVoiceCatalog.PiperEngineVersion, VietsubVoiceCatalog.PiperModelId,
            VietsubVoiceCatalog.PiperModelVersion, VietsubVoiceCatalog.PiperVoiceId,
            1000, 16000, 1, VietsubVoiceArtifactStatuses.Ready,
            VietsubVoiceTimingStatuses.Natural, now, now, [cue.CueId]);
        var store = new VietsubVoiceStore(paths, subtitles);
        Assert.True(await store.SaveArtifactAsync(projectId, artifact, track.Revision));
        var piperSettings = new VietsubVoiceSettings();
        Assert.Equal(artifact.ArtifactId, (await store.LoadWorkspaceAsync(projectId,
            track.TrackId, track.Revision, piperSettings)).Timeline?.ArtifactId);
        var kokoroSettings = new VietsubVoiceSettings {
            EngineId = VietsubVoiceEngines.Kokoro,
            ModelId = VietsubVoiceModelCatalog.ModelId,
            VoiceId = VietsubVoiceModelCatalog.Voices[0].VoiceId
        };
        Assert.Null((await store.LoadWorkspaceAsync(projectId,
            track.TrackId, track.Revision, kokoroSettings)).Timeline);
        Assert.Equal(artifact.ArtifactId, (await store.LoadWorkspaceAsync(projectId,
            track.TrackId, track.Revision, piperSettings)).Timeline?.ArtifactId);
    }

    [Fact]
    public async Task JobStore_HasActiveChecksAllJobsWithoutRecentListLimit()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var jobs = new VietsubJobStore(paths, subtitles);
        var projectId = Guid.NewGuid();
        Assert.False(await jobs.HasActiveAsync(projectId));
        var job = await jobs.CreateAsync(projectId, VietsubJobTypes.SynthesizeVoiceLocal,
            ["VOICE_PREPARE"]);
        Assert.True(await jobs.HasActiveAsync(projectId));
        await jobs.TransitionAsync(projectId, job.Id, VietsubJobStatus.Cancelled, "CANCELLED");
        Assert.False(await jobs.HasActiveAsync(projectId));
    }

    [Fact]
    public async Task Bridge_SelectVoicePersistsExactVoiceAndRejectsForeignProjectContext()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var projects = new VietsubProjectStore(paths, subtitles);
        var project = await projects.CreateAsync(Guid.NewGuid(), "owner", "Voice choice");
        var voiceStore = new VietsubVoiceStore(paths, subtitles);
        var jobs = new VietsubJobStore(paths, subtitles);
        await using var manager = new VietsubJobManager(jobs, new VietsubJobExecutorRegistry());
        using var components = new VietsubVoiceComponentStore(paths, featureEnabled: true);
        var service = new VietsubVoiceService(new AllowVoiceAuthorizer(), subtitles,
            voiceStore, paths, components,
            new VietsubVoicePlaybackRegistry(voiceStore.IsTrackRevisionCurrent),
            null!, manager);
        var messages = new List<string>();
        using var bridge = new VietsubWebBridge(true, messages.Add, projects,
            () => new VietsubUserContext("owner", project.OrganizationId),
            jobManager: manager, voiceService: service);
        await bridge.TryHandleAsync(System.Text.Json.JsonSerializer.Serialize(new {
            type = "vietsub.project.open", requestId = "open",
            payload = new { projectId = project.ProjectId }
        }));
        messages.Clear();
        var selectedVoice = VietsubVoiceModelCatalog.Voices[2].VoiceId;
        await bridge.TryHandleAsync(System.Text.Json.JsonSerializer.Serialize(new {
            type = "vietsub.voice.select", requestId = "choose",
            payload = new { expectedProjectId = project.ProjectId, voiceId = selectedVoice }
        }));
        Assert.DoesNotContain(messages, message => message.Contains("vietsub.error", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("vietsub.state", StringComparison.Ordinal)
            && message.Contains(selectedVoice, StringComparison.Ordinal));
        var persisted = await projects.OpenAsync(project.ProjectId, project.OrganizationId, "owner");
        Assert.Equal(VietsubVoiceEngines.Kokoro, persisted.VoiceSettings.EngineId);
        Assert.Equal(selectedVoice, persisted.VoiceSettings.VoiceId);
        Assert.False(await manager.HasActiveAsync(project.ProjectId));

        messages.Clear();
        await bridge.TryHandleAsync(System.Text.Json.JsonSerializer.Serialize(new {
            type = "vietsub.voice.select", requestId = "foreign",
            payload = new { expectedProjectId = Guid.NewGuid(), voiceId = VietsubVoiceCatalog.PiperVoiceId }
        }));
        Assert.Contains(messages, message => message.Contains("vietsub.error", StringComparison.Ordinal));
        Assert.Equal(selectedVoice, (await projects.OpenAsync(project.ProjectId,
            project.OrganizationId, "owner")).VoiceSettings.VoiceId);
    }

    private sealed class AllowVoiceAuthorizer : IVietsubLocalJobAuthorizer
    {
        public Task AuthorizeAsync(string userId, Guid organizationId,
            VietsubProjectManifest project, CancellationToken token) => Task.CompletedTask;
    }

    [Fact]
    public async Task Schema5Migration_DefaultsExistingCuesToEnabled_AndPreservesSelectionOnReopen()
    {
        var paths = new VietsubAppPaths(_root);
        var store = new VietsubSubtitleStore(paths);
        var projectId = Guid.NewGuid();
        var cue = Cue(0, 800, "Xin chào");
        cue.OriginalLocked = true;
        var track = new VietsubSubtitleTrack { DisplayName = "Fixture", Cues = [cue] };
        await store.SaveTrackAsync(projectId, track);
        await using (var connection = new SqliteConnection($"Data Source={paths.GetProjectPath(projectId, "project.db")}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE subtitle_cues DROP COLUMN voice_enabled; UPDATE schema_info SET schema_version = 5;";
            await command.ExecuteNonQueryAsync();
        }
        await store.InitializeAsync(projectId);
        var migrated = Assert.Single(await store.LoadTracksAsync(projectId));
        Assert.True(Assert.Single(migrated.Cues).VoiceEnabled);
        var revision = await store.SetVoiceEnabledAsync(projectId, track.TrackId, track.Revision, [cue.CueId], false);
        var reopened = new VietsubSubtitleStore(paths);
        await reopened.InitializeAsync(projectId);
        var loaded = Assert.Single(await reopened.LoadTracksAsync(projectId));
        var savedCue = Assert.Single(loaded.Cues);
        Assert.False(savedCue.VoiceEnabled);
        Assert.True(savedCue.OriginalLocked);
        Assert.Equal(cue.TranslatedText, savedCue.TranslatedText);
        Assert.Equal(cue.StartMilliseconds, savedCue.StartMilliseconds);
        Assert.Equal(cue.EndMilliseconds, savedCue.EndMilliseconds);
        Assert.Equal(revision, loaded.Revision);
        Assert.Equal(revision, await reopened.SetVoiceEnabledAsync(projectId, track.TrackId, revision, [cue.CueId], false));
        Assert.Equal(revision + 1, await reopened.SetVoiceEnabledAsync(projectId, track.TrackId, revision, [cue.CueId], true));
    }

    [Fact]
    public async Task BatchSelection_IsAtomic_RejectsForeignCuesStaleRevisionAndActiveJobs()
    {
        var paths = new VietsubAppPaths(_root);
        var store = new VietsubSubtitleStore(paths);
        var projectId = Guid.NewGuid();
        var track = new VietsubSubtitleTrack { DisplayName = "Fixture", Cues = [Cue(0, 800, "Một"), Cue(1000, 1800, "Hai")] };
        await store.SaveTrackAsync(projectId, track);
        await Assert.ThrowsAsync<VietsubVoiceException>(() => store.SetVoiceEnabledAsync(
            projectId, track.TrackId, track.Revision, [track.Cues[0].CueId, Guid.NewGuid()], false));
        Assert.All(Assert.Single(await store.LoadTracksAsync(projectId)).Cues, cue => Assert.True(cue.VoiceEnabled));
        var revision = await store.SetVoiceEnabledAsync(projectId, track.TrackId, track.Revision,
            track.Cues.Select(cue => cue.CueId).ToArray(), false);
        Assert.Equal(track.Revision + 1, revision);
        Assert.All(Assert.Single(await store.LoadTracksAsync(projectId)).Cues, cue => Assert.False(cue.VoiceEnabled));
        var stale = await Assert.ThrowsAsync<VietsubVoiceException>(() => store.SetVoiceEnabledAsync(
            projectId, track.TrackId, track.Revision, [track.Cues[0].CueId], true));
        Assert.Equal(VietsubVoiceErrorCodes.TrackChanged, stale.Code);
        var jobs = new VietsubJobStore(paths, store);
        await jobs.CreateAsync(projectId, VietsubJobTypes.TranslateLocal, ["TRANSLATE"]);
        var busy = await Assert.ThrowsAsync<VietsubVoiceException>(() => store.SetVoiceEnabledAsync(
            projectId, track.TrackId, revision, [track.Cues[0].CueId], true));
        Assert.Equal("VOICE_SELECTION_JOB_ACTIVE", busy.Code);
    }

    [Fact]
    public async Task SplitAndDuplicate_InheritSkippedVoiceWithoutDeletingSubtitleText()
    {
        var paths = new VietsubAppPaths(_root);
        var store = new VietsubSubtitleStore(paths);
        var projects = new VietsubProjectStore(paths, store);
        var project = await projects.CreateAsync(Guid.NewGuid(), "owner", "Selection fixture");
        var cue = Cue(0, 1600, "Xin chào các bạn");
        cue.VoiceEnabled = false;
        var track = new VietsubSubtitleTrack { DisplayName = "Fixture", Cues = [cue] };
        await store.SaveTrackAsync(project.ProjectId, track);
        project.ActiveSubtitleTrackId = track.TrackId;
        var service = new VietsubSubtitleService(paths, store);
        var duplicateId = await service.DuplicateCueAsync(project, cue.CueId);
        await service.SplitCueAsync(project, cue.CueId, 800);
        var loaded = Assert.Single(await store.LoadTracksAsync(project.ProjectId));
        Assert.Equal(3, loaded.Cues.Count);
        Assert.All(loaded.Cues, item => Assert.False(item.VoiceEnabled));
        Assert.Equal(cue.TranslatedText, loaded.Cues.Single(item => item.CueId == duplicateId).TranslatedText);
    }

    [Fact]
    public void Planner_SkipsUntranslatedCue_SeparatesPhrases_AndSetsFittingTarget()
    {
        var first = Cue(0, 800, "Xin chào");
        var skipped = Cue(900, 1000, "");
        skipped.VoiceEnabled = false;
        var last = Cue(1100, 1800, "các bạn");
        var track = new VietsubSubtitleTrack { Cues = [first, skipped, last] };
        VietsubVoiceTranslationPolicy.EnsureComplete(track);
        var phrases = VietsubVoicePhrasePlanner.Plan(track.Cues, Settings());
        Assert.Equal(2, phrases.Count);
        Assert.Equal([first.CueId], phrases[0].CueIds);
        Assert.Equal(900, phrases[0].HardEndMilliseconds);
        Assert.Equal([last.CueId], phrases[1].CueIds);
        Assert.Null(phrases[1].HardEndMilliseconds);
        var fingerprint = VietsubVoiceFingerprintBuilder.BuildSelectionFingerprint(track.Cues);
        skipped.VoiceEnabled = true;
        Assert.NotEqual(fingerprint, VietsubVoiceFingerprintBuilder.BuildSelectionFingerprint(track.Cues));
        Assert.Throws<VietsubVoiceException>(() => VietsubVoiceTranslationPolicy.EnsureComplete(track));
        track.Cues.ForEach(cue => cue.VoiceEnabled = false);
        Assert.Equal("VOICE_NO_CUES_SELECTED", Assert.Throws<VietsubVoiceException>(() => VietsubVoiceTranslationPolicy.EnsureComplete(track)).Code);
    }

    [Fact]
    public async Task Renderer_KeepsLongSpeechSpillingIntoSkippedCue()
    {
        var path = Path.Combine(_root, "phrase.wav");
        VietsubVoiceBoundaryTests.WriteWave(path, 16000, 2000, 0, 2000);
        var phrase = new VietsubVoicePhrase("phrase", [Guid.NewGuid()], "speaker_1", "Xin chào", 0, 800, 900);
        var renderer = new VietsubVoiceTimelineRenderer(new VietsubAppPaths(_root),
            new VietsubVoiceBoundaryTests.ReadyPreflight(),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe"), new ExternalProcessRunner());
        var result = await renderer.RenderAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            [new VietsubVoicePhraseAudio(phrase, null!, path, VietsubWavInspector.Inspect(path, true))],
            Settings(), 3000, CancellationToken.None);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(VietsubVoiceTimingStatuses.ReviewRequired, diagnostic.Status);
        Assert.Equal(1.2, diagnostic.Tempo);
        Assert.True(VietsubVoiceBoundaryTests.Peak(VietsubVoiceBoundaryTests.ReadSamples(result.AbsolutePath), 1500, 1600) > 1000);
    }

    private static VietsubVoiceSettingsSnapshot Settings() => new(
        VietsubVoiceEngines.Piper, VietsubVoiceCatalog.PiperEngineVersion,
        VietsubVoiceCatalog.PiperModelId, VietsubVoiceCatalog.PiperModelVersion,
        VietsubVoiceCatalog.PiperVoiceId, 500, 8000, 4500, 600, 1.12, 1.20, true);

    private static VietsubSubtitleCue Cue(long start, long end, string text) => new()
    {
        StartMilliseconds = start, EndMilliseconds = end, OriginalText = "source",
        TranslatedText = text, Speaker = "speaker_1"
    };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

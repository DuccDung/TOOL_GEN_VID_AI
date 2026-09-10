using Microsoft.Data.Sqlite;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubVoiceSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vietsub-selection-{Guid.NewGuid():N}");

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

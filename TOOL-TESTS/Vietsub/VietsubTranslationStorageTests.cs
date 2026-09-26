using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Translation;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubTranslationStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"videomaker-vietsub-translation-storage-{Guid.NewGuid():N}");

    [Fact]
    public async Task ManifestV1_MigratesInMemoryAndPersistsSafeTranslationDefaults()
    {
        var (paths, subtitles, projects) = CreateProjectStores();
        var organizationId = Guid.NewGuid();
        var created = await projects.CreateAsync(organizationId, "owner", "Manifest cũ");
        var manifestPath = paths.GetProjectPath(created.ProjectId, "project.json");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        root["schemaVersion"] = 1;
        root.Remove("translationSettings");
        await File.WriteAllTextAsync(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var migrated = await projects.OpenAsync(created.ProjectId, organizationId, "owner");

        Assert.Equal(VietsubProjectManifest.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.NotNull(migrated.TranslationSettings);
        Assert.Equal(string.Empty, migrated.TranslationSettings.SourceLanguageCode);
        Assert.Equal("vi", migrated.TranslationSettings.TargetLanguageCode);
        Assert.Equal(VietsubTranslationEnginePolicies.NotSelected, migrated.TranslationSettings.EnginePolicy);
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        Assert.Equal(VietsubProjectManifest.CurrentSchemaVersion, saved.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(saved.RootElement.TryGetProperty("translationSettings", out _));
        Assert.True(saved.RootElement.TryGetProperty("voiceSettings", out _));
        Assert.True(saved.RootElement.TryGetProperty("subtitleStyle", out _));
        Assert.True(saved.RootElement.TryGetProperty("audioMixSettings", out _));
    }

    [Fact]
    public void TranslationSettings_RejectsConflictingLanguageAndOversizedGlossary()
    {
        var conflicting = new VietsubTranslationSettings { SourceLanguageCode = "zh" };
        Assert.Throws<InvalidDataException>(() => conflicting.Normalize("en", "vi"));

        var oversized = new VietsubTranslationSettings
        {
            SourceLanguageCode = "en",
            Glossary = Enumerable.Range(0, VietsubTranslationSettings.MaximumGlossaryEntries + 1)
                .Select(index => new VietsubTranslationGlossarySetting
                {
                    SourceText = $"source-{index}",
                    TargetText = $"target-{index}"
                })
                .ToList()
        };
        Assert.Throws<InvalidDataException>(() => oversized.Normalize("en", "vi"));
    }

    [Fact]
    public async Task Schema5_ContainsTranslationAndVoiceObjects_ReopensIdempotently_AndRejectsIncompleteSchema5()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var projectId = Guid.NewGuid();
        await subtitles.InitializeAsync(projectId);
        await subtitles.InitializeAsync(projectId);

        await using (var connection = await OpenAsync(paths, projectId))
        {
            await using var version = connection.CreateCommand();
            version.CommandText = "SELECT schema_version FROM schema_info LIMIT 1;";
            Assert.Equal(6L, Convert.ToInt64(await version.ExecuteScalarAsync()));
            await using var columns = connection.CreateCommand();
            columns.CommandText = """
                SELECT COUNT(*) FROM pragma_table_info('subtitle_cues')
                WHERE name IN (
                    'translation_source', 'translation_engine_id',
                    'translation_engine_version', 'translation_source_fingerprint',
                    'translation_confidence', 'translation_reviewed_at_utc');
                """;
            Assert.Equal(6L, Convert.ToInt64(await columns.ExecuteScalarAsync()));
            await using var tables = connection.CreateCommand();
            tables.CommandText = """
                SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'
                AND name IN ('translation_memory', 'translation_cache', 'translation_job_items');
                """;
            Assert.Equal(3L, Convert.ToInt64(await tables.ExecuteScalarAsync()));
            await using var voiceTables = connection.CreateCommand();
            voiceTables.CommandText = """
                SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'
                AND name IN ('voice_artifacts', 'voice_artifact_cues', 'voice_phrase_boundaries', 'voice_cue_timings');
                """;
            Assert.Equal(4L, Convert.ToInt64(await voiceTables.ExecuteScalarAsync()));
        }

        var incompleteProjectId = Guid.NewGuid();
        paths.CreateProjectDirectories(incompleteProjectId);
        await using (var incomplete = await OpenAsync(paths, incompleteProjectId))
        {
            await using var command = incomplete.CreateCommand();
            command.CommandText = """
                CREATE TABLE schema_info(schema_version INTEGER NOT NULL);
                INSERT INTO schema_info(schema_version) VALUES(5);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            subtitles.InitializeAsync(incompleteProjectId));
        Assert.Contains("schema 5", error.Message, StringComparison.OrdinalIgnoreCase);

        var futureProjectId = Guid.NewGuid();
        paths.CreateProjectDirectories(futureProjectId);
        await using (var future = await OpenAsync(paths, futureProjectId))
        {
            await using var command = future.CreateCommand();
            command.CommandText = """
                CREATE TABLE schema_info(schema_version INTEGER NOT NULL);
                INSERT INTO schema_info(schema_version) VALUES(7);
                """;
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => subtitles.InitializeAsync(futureProjectId));
        await using var futureVerify = await OpenAsync(paths, futureProjectId);
        await using var futureTables = futureVerify.CreateCommand();
        futureTables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table';";
        Assert.Equal(1L, Convert.ToInt64(await futureTables.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Schema3OrInterruptedMigration_PreservesExistingCueArtifactAndJobData()
    {
        var (paths, subtitles, _) = CreateProjectStores();
        var projectId = Guid.NewGuid();
        var cue = new VietsubSubtitleCue
        {
            StartMilliseconds = 100,
            EndMilliseconds = 900,
            OriginalText = "Legacy cue"
        };
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "Legacy",
            LanguageCode = "en",
            Source = "SRT",
            Cues = [cue],
            Artifacts =
            [
                new VietsubSubtitleArtifact
                {
                    WorkspaceRelativePath = "subtitles/legacy.srt",
                    Sha256 = new string('a', 64)
                }
            ]
        };
        await subtitles.SaveTrackAsync(projectId, track);
        var jobs = new VietsubJobStore(paths, subtitles);
        var job = await jobs.CreateAsync(projectId, VietsubJobTypes.TranslateLocal, ["TRANSLATE"]);
        await using (var connection = await OpenAsync(paths, projectId))
        {
            await using var downgradeMarker = connection.CreateCommand();
            downgradeMarker.CommandText = "UPDATE schema_info SET schema_version = 3;";
            await downgradeMarker.ExecuteNonQueryAsync();
        }

        await subtitles.InitializeAsync(projectId);

        var loadedTrack = Assert.Single(await subtitles.LoadTracksAsync(projectId));
        Assert.Equal("Legacy cue", Assert.Single(loadedTrack.Cues).OriginalText);
        Assert.Equal("subtitles/legacy.srt", Assert.Single(loadedTrack.Artifacts).WorkspaceRelativePath);
        Assert.NotNull(await jobs.GetAsync(projectId, job.Id));
        await using var verify = await OpenAsync(paths, projectId);
        await using var version = verify.CreateCommand();
        version.CommandText = "SELECT schema_version FROM schema_info;";
        Assert.Equal(6L, Convert.ToInt64(await version.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Memory_IsProjectLanguageAndContextScoped_AndPrefersLatestManualEntry()
    {
        var (paths, subtitles, _) = CreateProjectStores();
        var store = new VietsubTranslationStore(paths, subtitles);
        var projectId = Guid.NewGuid();
        var contextA = Fingerprint("a");
        var contextB = Fingerprint("b");
        await store.SaveApprovedMemoryAsync(
            projectId, "en", "vi", "  You are here  ", "Bạn ở đây", contextA,
            VietsubTranslationMemorySourceKinds.ManualApproved);
        await store.SaveApprovedMemoryAsync(
            projectId, "en", "vi", "You   are here", "Ngài ở đây", contextB,
            VietsubTranslationMemorySourceKinds.ManualApproved);

        var first = await store.FindApprovedMemoryAsync(projectId, "en", "vi", "you are HERE", contextA);
        var second = await store.FindApprovedMemoryAsync(projectId, "en", "vi", "you are here", contextB);
        var wrongContext = await store.FindApprovedMemoryAsync(
            projectId, "en", "vi", "you are here", Fingerprint("c"));

        Assert.Equal("Bạn ở đây", first?.TranslatedText);
        Assert.Equal("Ngài ở đây", second?.TranslatedText);
        Assert.Null(wrongContext);
    }

    [Fact]
    public async Task Cache_UsesConfigurationAndInputFingerprints_AndIgnoresCorruptJson()
    {
        var (paths, subtitles, _) = CreateProjectStores();
        var store = new VietsubTranslationStore(paths, subtitles);
        var projectId = Guid.NewGuid();
        var configuration = Fingerprint("config");
        var input = Fingerprint("input");
        var key = VietsubTranslationStore.BuildCacheKey("engine", "1", configuration, input);
        Assert.NotEqual(key, VietsubTranslationStore.BuildCacheKey("engine", "2", configuration, input));
        Assert.NotEqual(key, VietsubTranslationStore.BuildCacheKey("engine", "1", Fingerprint("other"), input));
        await store.SaveCacheAsync(projectId, "engine", "1", configuration, input, "{\"items\":[]}");
        Assert.NotNull(await store.TryGetCacheAsync(projectId, key));

        await using (var connection = await OpenAsync(paths, projectId))
        {
            await using var corrupt = connection.CreateCommand();
            corrupt.CommandText = "UPDATE translation_cache SET result_json = '{broken' WHERE cache_key = $key;";
            corrupt.Parameters.AddWithValue("$key", key);
            await corrupt.ExecuteNonQueryAsync();
        }

        Assert.Null(await store.TryGetCacheAsync(projectId, key));
        await using var verify = await OpenAsync(paths, projectId);
        await using var count = verify.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM translation_cache WHERE cache_key = $key;";
        count.Parameters.AddWithValue("$key", key);
        Assert.Equal(0L, Convert.ToInt64(await count.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Cache_AllowsConcurrentProjectScopedWriters()
    {
        var (paths, subtitles, _) = CreateProjectStores();
        var store = new VietsubTranslationStore(paths, subtitles);
        var projectId = Guid.NewGuid();
        await subtitles.InitializeAsync(projectId);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => store.SaveCacheAsync(
            projectId,
            "engine",
            "1",
            Fingerprint($"config-{index}"),
            Fingerprint($"input-{index}"),
            $"{{\"index\":{index}}}")));

        await using var connection = await OpenAsync(paths, projectId);
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM translation_cache WHERE project_id = $projectId;";
        count.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        Assert.Equal(8L, Convert.ToInt64(await count.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task JobItem_ResumeKeepsCompletedFingerprint_AndAtomicCommitRejectsStaleOrLockedCue()
    {
        var (paths, subtitles, _) = CreateProjectStores();
        var translations = new VietsubTranslationStore(paths, subtitles);
        var jobs = new VietsubJobStore(paths, subtitles);
        var projectId = Guid.NewGuid();
        var cue = new VietsubSubtitleCue
        {
            StartMilliseconds = 0,
            EndMilliseconds = 1_000,
            Speaker = "alice",
            OriginalText = "Hello"
        };
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "English",
            LanguageCode = "en",
            Source = "SRT",
            Cues = [cue]
        };
        await subtitles.SaveTrackAsync(projectId, track);
        var job = await jobs.CreateAsync(
            projectId,
            VietsubJobTypes.TranslateLocal,
            ["TRANSLATE"],
            inputTrackId: track.TrackId,
            inputRevision: track.Revision);
        await jobs.TransitionAsync(projectId, job.Id, VietsubJobStatus.Running, "STARTED");
        var fingerprint = Fingerprint("cue-input");
        var seed = new VietsubTranslationJobItemSeed(cue.CueId, 1, 1, fingerprint);
        await translations.UpsertJobItemsAsync(projectId, job.Id, [seed]);

        var committed = await translations.TryCommitCueResultAsync(
            projectId,
            job.Id,
            new VietsubTranslationCueCommit(
                cue.CueId,
                cue.UpdatedAtUtc,
                cue.OriginalText,
                cue.StartMilliseconds,
                cue.EndMilliseconds,
                cue.Speaker,
                fingerprint,
                "Xin chào",
                VietsubTranslationQualityStatuses.Valid,
                0.95,
                [],
                "fixture-engine",
                "1"),
            "{\"completed\":1}");
        Assert.True(committed);

        await translations.UpsertJobItemsAsync(projectId, job.Id, [seed]);
        var item = Assert.Single(await translations.LoadJobItemsAsync(projectId, job.Id));
        Assert.Equal(VietsubTranslationJobItemStatuses.Completed, item.Status);
        Assert.Equal("Xin chào", item.TranslatedText);
        var loadedTrack = Assert.Single(await subtitles.LoadTracksAsync(projectId));
        var loadedCue = Assert.Single(loadedTrack.Cues);
        Assert.Equal("LOCAL_AUTO", loadedCue.TranslationSource);
        Assert.Equal(fingerprint, loadedCue.TranslationSourceFingerprint);
        Assert.Equal(2, loadedTrack.Revision);

        loadedCue.TranslatedText = "Bản sửa tay";
        loadedCue.TranslationLocked = true;
        loadedCue.TranslationSource = VietsubTranslationSources.Manual;
        loadedCue.UpdatedAtUtc = DateTime.UtcNow;
        loadedTrack.Revision++;
        await subtitles.SaveTrackAsync(projectId, loadedTrack);
        var rejected = await translations.TryCommitCueResultAsync(
            projectId,
            job.Id,
            new VietsubTranslationCueCommit(
                loadedCue.CueId,
                cue.UpdatedAtUtc,
                cue.OriginalText,
                cue.StartMilliseconds,
                cue.EndMilliseconds,
                cue.Speaker,
                fingerprint,
                "Không được ghi đè",
                VietsubTranslationQualityStatuses.Valid,
                null,
                [],
                "fixture-engine",
                "1"),
            "{\"completed\":2}");

        Assert.False(rejected);
        Assert.Equal("Bản sửa tay", Assert.Single((await subtitles.LoadTracksAsync(projectId)).Single().Cues).TranslatedText);
    }

    private (VietsubAppPaths Paths, VietsubSubtitleStore Subtitles, VietsubProjectStore Projects)
        CreateProjectStores()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        return (paths, subtitles, new VietsubProjectStore(paths, subtitles));
    }

    private static async Task<SqliteConnection> OpenAsync(VietsubAppPaths paths, Guid projectId)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.GetProjectPath(projectId, "project.db"),
            Pooling = false,
            ForeignKeys = true
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private static string Fingerprint(string seed) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)))
        .ToLowerInvariant();

    public void Dispose()
    {
        VietsubTestStorage.ClearPools(_root);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

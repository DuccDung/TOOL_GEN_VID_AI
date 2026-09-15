using Microsoft.Data.Sqlite;
using System.Text.Json;
using TOOL_LOCAL.Vietsub.Domain;

namespace TOOL_LOCAL.Vietsub.Storage;

internal sealed record VietsubTimelineCueRecord(
    Guid CueId,
    int CueIndex,
    long StartMilliseconds,
    long EndMilliseconds,
    bool OriginalLocked,
    bool TranslationLocked,
    string? QualityStatus,
    bool HasWarnings,
    bool HasTranslation,
    string PreviewText,
    bool VoiceEnabled = true);

internal sealed record VietsubTimelineWindowRecord(
    int TrackRevision,
    bool Truncated,
    IReadOnlyList<VietsubTimelineCueRecord> Cues);

internal sealed partial class VietsubSubtitleStore(VietsubAppPaths paths)
{
    private const int SchemaVersion = 6;

    public async Task InitializeAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        paths.CreateProjectDirectories(projectId);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        int? existingSchemaVersion = null;
        await using (var preflightCommand = connection.CreateCommand())
        {
            preflightCommand.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA foreign_keys=ON;
                CREATE TABLE IF NOT EXISTS schema_info (
                    schema_version INTEGER NOT NULL
                );
                """;
            await preflightCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var preflightVersionCommand = connection.CreateCommand())
        {
            preflightVersionCommand.CommandText = "SELECT schema_version FROM schema_info LIMIT 1;";
            var existingVersion = await preflightVersionCommand.ExecuteScalarAsync(cancellationToken);
            if (existingVersion is not null and not DBNull)
            {
                existingSchemaVersion = Convert.ToInt32(existingVersion);
                if (existingSchemaVersion is < 1 or > SchemaVersion)
                {
                    throw new InvalidDataException("Phiên bản database Vietsub chưa được hỗ trợ.");
                }
            }
        }

        if (existingSchemaVersion == 5)
        {
            await ValidateVersion5SchemaAsync(connection, cancellationToken);
        }
        if (existingSchemaVersion == SchemaVersion)
        {
            await ValidateVersion5SchemaAsync(connection, cancellationToken);
            await ValidateVoiceSelectionSchemaAsync(connection, cancellationToken);
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            BEGIN IMMEDIATE;

            INSERT INTO schema_info(schema_version)
            SELECT 5
            WHERE NOT EXISTS (SELECT 1 FROM schema_info);

            CREATE TABLE IF NOT EXISTS subtitle_tracks (
                track_id TEXT NOT NULL PRIMARY KEY,
                display_name TEXT NOT NULL,
                language_code TEXT NOT NULL,
                source TEXT NOT NULL,
                revision INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS subtitle_cues (
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
                quality_status TEXT NULL,
                warning_json TEXT NOT NULL DEFAULT '[]',
                translation_source TEXT NULL,
                translation_engine_id TEXT NULL,
                translation_engine_version TEXT NULL,
                translation_source_fingerprint TEXT NULL,
                translation_confidence REAL NULL,
                translation_reviewed_at_utc TEXT NULL,
                updated_at_utc TEXT NOT NULL,
                CONSTRAINT fk_subtitle_cues_track
                    FOREIGN KEY(track_id) REFERENCES subtitle_tracks(track_id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_subtitle_cues_track_order
                ON subtitle_cues(track_id, cue_index);
            CREATE INDEX IF NOT EXISTS ix_subtitle_cues_timeline
                ON subtitle_cues(track_id, start_ms, end_ms);

            CREATE TABLE IF NOT EXISTS subtitle_artifacts (
                artifact_id TEXT NOT NULL PRIMARY KEY,
                track_id TEXT NOT NULL,
                artifact_type TEXT NOT NULL,
                track_revision INTEGER NOT NULL,
                relative_path TEXT NOT NULL,
                sha256 TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                CONSTRAINT fk_subtitle_artifacts_track
                    FOREIGN KEY(track_id) REFERENCES subtitle_tracks(track_id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_subtitle_artifacts_track_revision
                ON subtitle_artifacts(track_id, track_revision, status);

            CREATE TABLE IF NOT EXISTS local_jobs (
                id TEXT NOT NULL PRIMARY KEY,
                project_id TEXT NOT NULL,
                type TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                started_at_utc TEXT NULL,
                updated_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NULL,
                progress_percent REAL NOT NULL,
                status_message TEXT NULL,
                input_track_id TEXT NULL,
                output_track_id TEXT NULL,
                input_revision INTEGER NULL,
                parameters_json TEXT NOT NULL,
                checkpoint_json TEXT NULL,
                metrics_json TEXT NULL,
                attempt_count INTEGER NOT NULL,
                max_attempts INTEGER NOT NULL,
                error_code TEXT NULL,
                error_message TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_local_jobs_project_updated
                ON local_jobs(project_id, updated_at_utc DESC);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_local_jobs_project_active
                ON local_jobs(project_id)
                WHERE status IN ('PENDING', 'RUNNING', 'PAUSING', 'PAUSED');

            CREATE TABLE IF NOT EXISTS local_job_steps (
                job_id TEXT NOT NULL,
                step_index INTEGER NOT NULL,
                code TEXT NOT NULL,
                status TEXT NOT NULL,
                progress_percent REAL NOT NULL,
                started_at_utc TEXT NULL,
                updated_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NULL,
                error_code TEXT NULL,
                error_message TEXT NULL,
                PRIMARY KEY(job_id, step_index),
                CONSTRAINT fk_local_job_steps_job
                    FOREIGN KEY(job_id) REFERENCES local_jobs(id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_local_job_steps_code
                ON local_job_steps(job_id, code);

            CREATE TABLE IF NOT EXISTS local_job_events (
                id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                job_id TEXT NOT NULL,
                event_type TEXT NOT NULL,
                message TEXT NULL,
                created_at_utc TEXT NOT NULL,
                CONSTRAINT fk_local_job_events_job
                    FOREIGN KEY(job_id) REFERENCES local_jobs(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_local_job_events_job_created
                ON local_job_events(job_id, created_at_utc DESC);

            CREATE TABLE IF NOT EXISTS translation_memory (
                entry_id TEXT NOT NULL PRIMARY KEY,
                project_id TEXT NOT NULL,
                source_language_code TEXT NOT NULL,
                target_language_code TEXT NOT NULL,
                normalized_source_hash TEXT NOT NULL,
                source_text TEXT NOT NULL,
                translated_text TEXT NOT NULL,
                context_fingerprint TEXT NULL,
                source_kind TEXT NOT NULL,
                use_count INTEGER NOT NULL DEFAULT 0,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_translation_memory_lookup
                ON translation_memory(
                    project_id, source_language_code, target_language_code,
                    normalized_source_hash, updated_at_utc DESC);

            CREATE TABLE IF NOT EXISTS translation_cache (
                cache_key TEXT NOT NULL,
                project_id TEXT NOT NULL,
                engine_id TEXT NOT NULL,
                engine_version TEXT NOT NULL,
                configuration_fingerprint TEXT NOT NULL,
                input_fingerprint TEXT NOT NULL,
                result_json TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                last_used_at_utc TEXT NOT NULL,
                PRIMARY KEY(project_id, cache_key)
            );

            CREATE INDEX IF NOT EXISTS ix_translation_cache_project_used
                ON translation_cache(project_id, last_used_at_utc DESC);

            CREATE TABLE IF NOT EXISTS translation_job_items (
                job_id TEXT NOT NULL,
                cue_id TEXT NOT NULL,
                scene_number INTEGER NOT NULL,
                chapter_number INTEGER NOT NULL,
                input_fingerprint TEXT NOT NULL,
                status TEXT NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                translated_text TEXT NULL,
                confidence REAL NULL,
                warning_json TEXT NOT NULL DEFAULT '[]',
                error_code TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NULL,
                PRIMARY KEY(job_id, cue_id),
                CONSTRAINT fk_translation_job_items_job
                    FOREIGN KEY(job_id) REFERENCES local_jobs(id) ON DELETE CASCADE,
                CONSTRAINT fk_translation_job_items_cue
                    FOREIGN KEY(cue_id) REFERENCES subtitle_cues(cue_id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_translation_job_items_status
                ON translation_job_items(job_id, status, scene_number);

            CREATE TABLE IF NOT EXISTS voice_artifacts (
                artifact_id TEXT NOT NULL PRIMARY KEY,
                track_id TEXT NOT NULL,
                track_revision INTEGER NOT NULL,
                artifact_kind TEXT NOT NULL,
                phrase_id TEXT NULL,
                relative_path TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                sha256 TEXT NOT NULL,
                content_fingerprint TEXT NOT NULL,
                engine_id TEXT NOT NULL,
                engine_version TEXT NOT NULL,
                model_id TEXT NOT NULL,
                model_version TEXT NOT NULL,
                voice_id TEXT NOT NULL,
                duration_ms INTEGER NOT NULL,
                sample_rate INTEGER NOT NULL,
                channels INTEGER NOT NULL,
                status TEXT NOT NULL,
                timing_status TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                CONSTRAINT fk_voice_artifacts_track
                    FOREIGN KEY(track_id) REFERENCES subtitle_tracks(track_id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_voice_artifacts_fingerprint
                ON voice_artifacts(content_fingerprint, artifact_kind, status, updated_at_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_voice_artifacts_track_revision
                ON voice_artifacts(track_id, track_revision, artifact_kind, status);

            CREATE TABLE IF NOT EXISTS voice_artifact_cues (
                artifact_id TEXT NOT NULL,
                cue_id TEXT NOT NULL,
                cue_order INTEGER NOT NULL,
                PRIMARY KEY(artifact_id, cue_id),
                CONSTRAINT fk_voice_artifact_cues_artifact
                    FOREIGN KEY(artifact_id) REFERENCES voice_artifacts(artifact_id) ON DELETE CASCADE,
                CONSTRAINT fk_voice_artifact_cues_cue
                    FOREIGN KEY(cue_id) REFERENCES subtitle_cues(cue_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS voice_phrase_boundaries (
                track_id TEXT NOT NULL,
                cue_id TEXT NOT NULL,
                mode TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY(track_id, cue_id),
                CONSTRAINT fk_voice_phrase_boundaries_track
                    FOREIGN KEY(track_id) REFERENCES subtitle_tracks(track_id) ON DELETE CASCADE,
                CONSTRAINT fk_voice_phrase_boundaries_cue
                    FOREIGN KEY(cue_id) REFERENCES subtitle_cues(cue_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS voice_cue_timings (
                track_id TEXT NOT NULL,
                track_revision INTEGER NOT NULL,
                phrase_id TEXT NOT NULL,
                natural_duration_ms INTEGER NOT NULL,
                target_duration_ms INTEGER NOT NULL,
                borrowed_gap_ms INTEGER NOT NULL,
                tempo REAL NOT NULL,
                status TEXT NOT NULL,
                suggested_max_characters INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY(track_id, track_revision, phrase_id),
                CONSTRAINT fk_voice_cue_timings_track
                    FOREIGN KEY(track_id) REFERENCES subtitle_tracks(track_id) ON DELETE CASCADE
            );
            COMMIT;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT schema_version FROM schema_info LIMIT 1;";
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken));
        if (version == 1)
        {
            await MigrateFromVersion1Async(connection, cancellationToken);
            version = 2;
        }
        if (version == 2)
        {
            await MigrateFromVersion2Async(connection, cancellationToken);
            version = 3;
        }
        if (version == 3)
        {
            await MigrateFromVersion3Async(connection, cancellationToken);
            version = 4;
        }
        if (version == 4)
        {
            await MigrateFromVersion4Async(connection, cancellationToken);
            version = 5;
        }
        if (version == 5)
        {
            await MigrateToVersion6Async(connection, cancellationToken);
            version = 6;
        }
        if (version != SchemaVersion)
        {
            throw new InvalidDataException("Phiên bản database Vietsub chưa được hỗ trợ.");
        }
        await ValidateVersion5SchemaAsync(connection, cancellationToken);
        await ValidateVoiceSelectionSchemaAsync(connection, cancellationToken);
    }

    public Task SaveTrackAsync(
        Guid projectId,
        VietsubSubtitleTrack track,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => SaveTrackInternalAsync(
            projectId,
            track,
            checkpointJobId: null,
            checkpointJson: null,
            cancellationToken), cancellationToken);

    public Task SaveTrackAndJobCheckpointAsync(
        Guid projectId,
        VietsubSubtitleTrack track,
        Guid jobId,
        string checkpointJson,
        CancellationToken cancellationToken = default)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("Mã job Vietsub không hợp lệ.", nameof(jobId));
        }
        try
        {
            using var _ = JsonDocument.Parse(checkpointJson);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Checkpoint job Vietsub không phải JSON hợp lệ.", nameof(checkpointJson), exception);
        }

        return SaveTrackInternalAsync(
            projectId,
            track,
            jobId,
            checkpointJson,
            cancellationToken);
    }

    private async Task SaveTrackInternalAsync(
        Guid projectId,
        VietsubSubtitleTrack track,
        Guid? checkpointJobId,
        string? checkpointJson,
        CancellationToken cancellationToken)
    {
        ValidateTrack(track);
        await InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var trackCommand = connection.CreateCommand())
        {
            trackCommand.Transaction = (SqliteTransaction)transaction;
            trackCommand.CommandText = """
                INSERT INTO subtitle_tracks(
                    track_id, display_name, language_code, source, revision,
                    created_at_utc, updated_at_utc)
                VALUES($trackId, $displayName, $languageCode, $source, $revision,
                    $createdAtUtc, $updatedAtUtc)
                ON CONFLICT(track_id) DO UPDATE SET
                    display_name = excluded.display_name,
                    language_code = excluded.language_code,
                    source = excluded.source,
                    revision = excluded.revision,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            trackCommand.Parameters.AddWithValue("$trackId", track.TrackId.ToString("D"));
            trackCommand.Parameters.AddWithValue("$displayName", track.DisplayName.Trim());
            trackCommand.Parameters.AddWithValue("$languageCode", track.LanguageCode.Trim());
            trackCommand.Parameters.AddWithValue("$source", track.Source.Trim());
            trackCommand.Parameters.AddWithValue("$revision", track.Revision);
            trackCommand.Parameters.AddWithValue("$createdAtUtc", track.CreatedAtUtc.ToString("O"));
            trackCommand.Parameters.AddWithValue("$updatedAtUtc", track.UpdatedAtUtc.ToString("O"));
            await trackCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var tempCommand = connection.CreateCommand())
        {
            tempCommand.Transaction = (SqliteTransaction)transaction;
            tempCommand.CommandText = """
                CREATE TEMP TABLE IF NOT EXISTS incoming_vietsub_cues (
                    cue_id TEXT NOT NULL PRIMARY KEY
                );
                DELETE FROM incoming_vietsub_cues;
                """;
            await tempCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var releaseOrderCommand = connection.CreateCommand())
        {
            releaseOrderCommand.Transaction = (SqliteTransaction)transaction;
            releaseOrderCommand.CommandText = """
                UPDATE subtitle_cues
                SET cue_index = -cue_index - 1
                WHERE track_id = $trackId;
                """;
            releaseOrderCommand.Parameters.AddWithValue("$trackId", track.TrackId.ToString("D"));
            await releaseOrderCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var index = 0; index < track.Cues.Count; index++)
        {
            var cue = track.Cues[index];
            await UpsertCueAsync(connection, (SqliteTransaction)transaction, track.TrackId, cue, index, cancellationToken);
            await using var incomingCommand = connection.CreateCommand();
            incomingCommand.Transaction = (SqliteTransaction)transaction;
            incomingCommand.CommandText = "INSERT INTO incoming_vietsub_cues(cue_id) VALUES($cueId);";
            incomingCommand.Parameters.AddWithValue("$cueId", cue.CueId.ToString("D"));
            await incomingCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var artifactTempCommand = connection.CreateCommand())
        {
            artifactTempCommand.Transaction = (SqliteTransaction)transaction;
            artifactTempCommand.CommandText = """
                CREATE TEMP TABLE IF NOT EXISTS incoming_vietsub_artifacts (
                    artifact_id TEXT NOT NULL PRIMARY KEY
                );
                DELETE FROM incoming_vietsub_artifacts;
                """;
            await artifactTempCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var artifact in track.Artifacts)
        {
            await UpsertArtifactAsync(
                connection,
                (SqliteTransaction)transaction,
                track.TrackId,
                artifact,
                cancellationToken);
            await using var incomingArtifactCommand = connection.CreateCommand();
            incomingArtifactCommand.Transaction = (SqliteTransaction)transaction;
            incomingArtifactCommand.CommandText =
                "INSERT INTO incoming_vietsub_artifacts(artifact_id) VALUES($artifactId);";
            incomingArtifactCommand.Parameters.AddWithValue("$artifactId", artifact.ArtifactId.ToString("D"));
            await incomingArtifactCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = (SqliteTransaction)transaction;
            deleteCommand.CommandText = """
                DELETE FROM subtitle_cues
                WHERE track_id = $trackId
                  AND NOT EXISTS (
                      SELECT 1 FROM incoming_vietsub_cues incoming
                      WHERE incoming.cue_id = subtitle_cues.cue_id
                  );
                """;
            deleteCommand.Parameters.AddWithValue("$trackId", track.TrackId.ToString("D"));
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }


        await using (var deleteArtifactCommand = connection.CreateCommand())
        {
            deleteArtifactCommand.Transaction = (SqliteTransaction)transaction;
            deleteArtifactCommand.CommandText = """
                DELETE FROM subtitle_artifacts
                WHERE track_id = $trackId
                  AND NOT EXISTS (
                      SELECT 1 FROM incoming_vietsub_artifacts incoming
                      WHERE incoming.artifact_id = subtitle_artifacts.artifact_id
                  );
                """;
            deleteArtifactCommand.Parameters.AddWithValue("$trackId", track.TrackId.ToString("D"));
            await deleteArtifactCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (checkpointJobId is Guid jobId)
        {
            await using var checkpointCommand = connection.CreateCommand();
            checkpointCommand.Transaction = (SqliteTransaction)transaction;
            checkpointCommand.CommandText = """
                UPDATE local_jobs
                SET checkpoint_json = $checkpointJson,
                    updated_at_utc = $updatedAtUtc
                WHERE id = $jobId
                  AND project_id = $projectId
                  AND status IN ('RUNNING', 'PAUSING');
                """;
            checkpointCommand.Parameters.AddWithValue("$checkpointJson", checkpointJson!);
            checkpointCommand.Parameters.AddWithValue("$updatedAtUtc", DateTime.UtcNow.ToString("O"));
            checkpointCommand.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            checkpointCommand.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
            if (await checkpointCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Không thể lưu đồng thời track và checkpoint của OCR job.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public Task<IReadOnlyList<VietsubSubtitleTrack>> LoadTracksAsync(
        Guid projectId, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadTracksCoreAsync(projectId, cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<VietsubSubtitleTrack>> LoadTracksCoreAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        var tracks = new List<VietsubSubtitleTrack>();
        await using (var trackCommand = connection.CreateCommand())
        {
            trackCommand.CommandText = """
                SELECT track_id, display_name, language_code, source, revision,
                       created_at_utc, updated_at_utc
                FROM subtitle_tracks
                ORDER BY updated_at_utc DESC;
                """;
            await using var reader = await trackCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tracks.Add(new VietsubSubtitleTrack
                {
                    TrackId = Guid.Parse(reader.GetString(0)),
                    DisplayName = reader.GetString(1),
                    LanguageCode = reader.GetString(2),
                    Source = reader.GetString(3),
                    Revision = reader.GetInt32(4),
                    CreatedAtUtc = DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    UpdatedAtUtc = DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind)
                });
            }
        }

        var tracksById = tracks.ToDictionary(track => track.TrackId);
        await using var cueCommand = connection.CreateCommand();
        cueCommand.CommandText = """
            SELECT cue_id, track_id, start_ms, end_ms, speaker,
                   original_text, translated_text, original_locked,
                   translation_locked, quality_status, warning_json,
                   translation_source, translation_engine_id,
                   translation_engine_version, translation_source_fingerprint,
                   translation_confidence, translation_reviewed_at_utc,
                   updated_at_utc, voice_enabled
            FROM subtitle_cues
            ORDER BY track_id, cue_index;
            """;
        await using var cueReader = await cueCommand.ExecuteReaderAsync(cancellationToken);
        while (await cueReader.ReadAsync(cancellationToken))
        {
            var trackId = Guid.Parse(cueReader.GetString(1));
            if (!tracksById.TryGetValue(trackId, out var track))
            {
                continue;
            }

            track.Cues.Add(new VietsubSubtitleCue
            {
                CueId = Guid.Parse(cueReader.GetString(0)),
                StartMilliseconds = cueReader.GetInt64(2),
                EndMilliseconds = cueReader.GetInt64(3),
                Speaker = cueReader.GetString(4),
                OriginalText = cueReader.GetString(5),
                TranslatedText = cueReader.GetString(6),
                OriginalLocked = cueReader.GetBoolean(7),
                TranslationLocked = cueReader.GetBoolean(8),
                QualityStatus = cueReader.IsDBNull(9) ? null : cueReader.GetString(9),
                Warnings = DeserializeWarnings(cueReader.GetString(10)),
                TranslationSource = cueReader.IsDBNull(11) ? null : cueReader.GetString(11),
                TranslationEngineId = cueReader.IsDBNull(12) ? null : cueReader.GetString(12),
                TranslationEngineVersion = cueReader.IsDBNull(13) ? null : cueReader.GetString(13),
                TranslationSourceFingerprint = cueReader.IsDBNull(14) ? null : cueReader.GetString(14),
                TranslationConfidence = cueReader.IsDBNull(15) ? null : cueReader.GetDouble(15),
                TranslationReviewedAtUtc = cueReader.IsDBNull(16)
                    ? null
                    : DateTime.Parse(cueReader.GetString(16), null, System.Globalization.DateTimeStyles.RoundtripKind),
                UpdatedAtUtc = DateTime.Parse(cueReader.GetString(17), null, System.Globalization.DateTimeStyles.RoundtripKind),
                VoiceEnabled = cueReader.GetBoolean(18)
            });
        }


        await using var artifactCommand = connection.CreateCommand();
        artifactCommand.CommandText = """
            SELECT artifact_id, track_id, artifact_type, track_revision,
                   relative_path, sha256, status, created_at_utc, updated_at_utc
            FROM subtitle_artifacts
            ORDER BY track_id, created_at_utc;
            """;
        await using var artifactReader = await artifactCommand.ExecuteReaderAsync(cancellationToken);
        while (await artifactReader.ReadAsync(cancellationToken))
        {
            var trackId = Guid.Parse(artifactReader.GetString(1));
            if (!tracksById.TryGetValue(trackId, out var track))
            {
                continue;
            }

            track.Artifacts.Add(new VietsubSubtitleArtifact
            {
                ArtifactId = Guid.Parse(artifactReader.GetString(0)),
                ArtifactType = artifactReader.GetString(2),
                TrackRevision = artifactReader.GetInt32(3),
                WorkspaceRelativePath = artifactReader.GetString(4),
                Sha256 = artifactReader.GetString(5),
                Status = artifactReader.GetString(6),
                CreatedAtUtc = DateTime.Parse(artifactReader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind),
                UpdatedAtUtc = DateTime.Parse(artifactReader.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind)
            });
        }

        return tracks;
    }

    public Task<VietsubTimelineWindowRecord?> LoadTimelineWindowAsync(
        Guid projectId, Guid trackId, long windowStartMilliseconds, long windowEndMilliseconds,
        int maximumCues, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadTimelineWindowCoreAsync(projectId, trackId, windowStartMilliseconds,
            windowEndMilliseconds, maximumCues, cancellationToken), cancellationToken);

    private async Task<VietsubTimelineWindowRecord?> LoadTimelineWindowCoreAsync(
        Guid projectId,
        Guid trackId,
        long windowStartMilliseconds,
        long windowEndMilliseconds,
        int maximumCues,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        int? trackRevision;
        await using (var revisionCommand = connection.CreateCommand())
        {
            revisionCommand.Transaction = transaction;
            revisionCommand.CommandText = "SELECT revision FROM subtitle_tracks WHERE track_id = $trackId;";
            revisionCommand.Parameters.AddWithValue("$trackId", trackId.ToString("D"));
            var value = await revisionCommand.ExecuteScalarAsync(cancellationToken);
            trackRevision = value is null or DBNull ? null : Convert.ToInt32(value);
        }

        if (!trackRevision.HasValue)
        {
            return null;
        }

        var cues = new List<VietsubTimelineCueRecord>(maximumCues + 1);
        await using var cueCommand = connection.CreateCommand();
        cueCommand.Transaction = transaction;
        cueCommand.CommandText = """
            SELECT cue_id, cue_index, start_ms, end_ms,
                   original_locked, translation_locked, quality_status,
                   CASE WHEN warning_json <> '[]' THEN 1 ELSE 0 END AS has_warnings,
                   CASE WHEN length(trim(translated_text)) > 0 THEN 1 ELSE 0 END AS has_translation,
                   CASE
                       WHEN length(trim(translated_text)) > 0 THEN substr(translated_text, 1, 200)
                       ELSE substr(original_text, 1, 200)
                   END AS preview_text, voice_enabled
            FROM subtitle_cues
            WHERE track_id = $trackId
              AND start_ms < $windowEnd
              AND end_ms > $windowStart
            ORDER BY start_ms, cue_index
            LIMIT $limit;
            """;
        cueCommand.Parameters.AddWithValue("$trackId", trackId.ToString("D"));
        cueCommand.Parameters.AddWithValue("$windowStart", windowStartMilliseconds);
        cueCommand.Parameters.AddWithValue("$windowEnd", windowEndMilliseconds);
        cueCommand.Parameters.AddWithValue("$limit", maximumCues + 1);
        await using var cueReader = await cueCommand.ExecuteReaderAsync(cancellationToken);
        while (await cueReader.ReadAsync(cancellationToken))
        {
            cues.Add(new VietsubTimelineCueRecord(
                Guid.Parse(cueReader.GetString(0)),
                cueReader.GetInt32(1),
                cueReader.GetInt64(2),
                cueReader.GetInt64(3),
                cueReader.GetBoolean(4),
                cueReader.GetBoolean(5),
                cueReader.IsDBNull(6) ? null : cueReader.GetString(6),
                cueReader.GetBoolean(7),
                cueReader.GetBoolean(8),
                cueReader.GetString(9),
                cueReader.GetBoolean(10)));
        }

        var truncated = cues.Count > maximumCues;
        if (truncated)
        {
            cues.RemoveAt(cues.Count - 1);
        }
        return new VietsubTimelineWindowRecord(trackRevision.Value, truncated, cues);
    }

    public async Task<bool> TrySaveArtifactAsync(
        Guid projectId,
        Guid trackId,
        int expectedTrackRevision,
        VietsubSubtitleArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        if (trackId == Guid.Empty || expectedTrackRevision < 1)
        {
            throw new ArgumentException("Track/revision của subtitle artifact không hợp lệ.");
        }
        ValidateArtifact(artifact);
        await InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO subtitle_artifacts(
                artifact_id, track_id, artifact_type, track_revision,
                relative_path, sha256, status, created_at_utc, updated_at_utc)
            SELECT $artifactId, track_id, $artifactType, revision,
                   $relativePath, $sha256, $status, $createdAtUtc, $updatedAtUtc
            FROM subtitle_tracks
            WHERE track_id = $trackId AND revision = $expectedRevision
            ON CONFLICT(artifact_id) DO UPDATE SET
                artifact_type = excluded.artifact_type,
                track_revision = excluded.track_revision,
                relative_path = excluded.relative_path,
                sha256 = excluded.sha256,
                status = excluded.status,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$artifactId", artifact.ArtifactId.ToString("D"));
        command.Parameters.AddWithValue("$artifactType", artifact.ArtifactType);
        command.Parameters.AddWithValue("$relativePath", artifact.WorkspaceRelativePath);
        command.Parameters.AddWithValue("$sha256", artifact.Sha256);
        command.Parameters.AddWithValue("$status", artifact.Status);
        command.Parameters.AddWithValue("$createdAtUtc", artifact.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", artifact.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$trackId", trackId.ToString("D"));
        command.Parameters.AddWithValue("$expectedRevision", expectedTrackRevision);
        var saved = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (saved)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        return saved;
    }

    private async Task<SqliteConnection> OpenAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = paths.GetProjectPath(projectId, "project.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task UpsertCueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid trackId,
        VietsubSubtitleCue cue,
        int index,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO subtitle_cues(
                cue_id, track_id, cue_index, start_ms, end_ms, speaker,
                original_text, translated_text, original_locked,
                translation_locked, quality_status, warning_json,
                translation_source, translation_engine_id,
                translation_engine_version, translation_source_fingerprint,
                translation_confidence, translation_reviewed_at_utc, updated_at_utc, voice_enabled)
            VALUES($cueId, $trackId, $cueIndex, $startMs, $endMs, $speaker,
                $originalText, $translatedText, $originalLocked,
                $translationLocked, $qualityStatus, $warningJson,
                $translationSource, $translationEngineId,
                $translationEngineVersion, $translationSourceFingerprint,
                $translationConfidence, $translationReviewedAtUtc, $updatedAtUtc, $voiceEnabled)
            ON CONFLICT(cue_id) DO UPDATE SET
                track_id = excluded.track_id,
                cue_index = excluded.cue_index,
                start_ms = excluded.start_ms,
                end_ms = excluded.end_ms,
                speaker = excluded.speaker,
                original_text = excluded.original_text,
                translated_text = excluded.translated_text,
                original_locked = excluded.original_locked,
                translation_locked = excluded.translation_locked,
                voice_enabled = excluded.voice_enabled,
                quality_status = excluded.quality_status,
                warning_json = excluded.warning_json,
                translation_source = excluded.translation_source,
                translation_engine_id = excluded.translation_engine_id,
                translation_engine_version = excluded.translation_engine_version,
                translation_source_fingerprint = excluded.translation_source_fingerprint,
                translation_confidence = excluded.translation_confidence,
                translation_reviewed_at_utc = excluded.translation_reviewed_at_utc,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$cueId", cue.CueId.ToString("D"));
        command.Parameters.AddWithValue("$voiceEnabled", cue.VoiceEnabled);
        command.Parameters.AddWithValue("$trackId", trackId.ToString("D"));
        command.Parameters.AddWithValue("$cueIndex", index);
        command.Parameters.AddWithValue("$startMs", cue.StartMilliseconds);
        command.Parameters.AddWithValue("$endMs", cue.EndMilliseconds);
        command.Parameters.AddWithValue("$speaker", cue.Speaker.Trim());
        command.Parameters.AddWithValue("$originalText", cue.OriginalText);
        command.Parameters.AddWithValue("$translatedText", cue.TranslatedText);
        command.Parameters.AddWithValue("$originalLocked", cue.OriginalLocked);
        command.Parameters.AddWithValue("$translationLocked", cue.TranslationLocked);
        command.Parameters.AddWithValue("$qualityStatus", (object?)cue.QualityStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$warningJson", JsonSerializer.Serialize(cue.Warnings));
        command.Parameters.AddWithValue("$translationSource", (object?)cue.TranslationSource ?? DBNull.Value);
        command.Parameters.AddWithValue("$translationEngineId", (object?)cue.TranslationEngineId ?? DBNull.Value);
        command.Parameters.AddWithValue("$translationEngineVersion", (object?)cue.TranslationEngineVersion ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$translationSourceFingerprint",
            (object?)cue.TranslationSourceFingerprint ?? DBNull.Value);
        command.Parameters.AddWithValue("$translationConfidence", (object?)cue.TranslationConfidence ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$translationReviewedAtUtc",
            cue.TranslationReviewedAtUtc is DateTime reviewedAtUtc
                ? reviewedAtUtc.ToString("O")
                : DBNull.Value);
        command.Parameters.AddWithValue("$updatedAtUtc", cue.UpdatedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertArtifactAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid trackId,
        VietsubSubtitleArtifact artifact,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO subtitle_artifacts(
                artifact_id, track_id, artifact_type, track_revision,
                relative_path, sha256, status, created_at_utc, updated_at_utc)
            VALUES($artifactId, $trackId, $artifactType, $trackRevision,
                $relativePath, $sha256, $status, $createdAtUtc, $updatedAtUtc)
            ON CONFLICT(artifact_id) DO UPDATE SET
                artifact_type = excluded.artifact_type,
                track_revision = excluded.track_revision,
                relative_path = excluded.relative_path,
                sha256 = excluded.sha256,
                status = excluded.status,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$artifactId", artifact.ArtifactId.ToString("D"));
        command.Parameters.AddWithValue("$trackId", trackId.ToString("D"));
        command.Parameters.AddWithValue("$artifactType", artifact.ArtifactType);
        command.Parameters.AddWithValue("$trackRevision", artifact.TrackRevision);
        command.Parameters.AddWithValue("$relativePath", artifact.WorkspaceRelativePath);
        command.Parameters.AddWithValue("$sha256", artifact.Sha256);
        command.Parameters.AddWithValue("$status", artifact.Status);
        command.Parameters.AddWithValue("$createdAtUtc", artifact.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", artifact.UpdatedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MigrateFromVersion1Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            ALTER TABLE subtitle_cues ADD COLUMN quality_status TEXT NULL;
            ALTER TABLE subtitle_cues ADD COLUMN warning_json TEXT NOT NULL DEFAULT '[]';

            CREATE TABLE IF NOT EXISTS subtitle_artifacts (
                artifact_id TEXT NOT NULL PRIMARY KEY,
                track_id TEXT NOT NULL,
                artifact_type TEXT NOT NULL,
                track_revision INTEGER NOT NULL,
                relative_path TEXT NOT NULL,
                sha256 TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                CONSTRAINT fk_subtitle_artifacts_track
                    FOREIGN KEY(track_id) REFERENCES subtitle_tracks(track_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_subtitle_artifacts_track_revision
                ON subtitle_artifacts(track_id, track_revision, status);
            UPDATE schema_info SET schema_version = 2;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task MigrateFromVersion2Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "UPDATE schema_info SET schema_version = 3;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task MigrateFromVersion3Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var sqliteTransaction = (SqliteTransaction)transaction;
        await AddColumnIfMissingAsync(
            connection,
            sqliteTransaction,
            "translation_source",
            "ALTER TABLE subtitle_cues ADD COLUMN translation_source TEXT NULL;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            connection,
            sqliteTransaction,
            "translation_engine_id",
            "ALTER TABLE subtitle_cues ADD COLUMN translation_engine_id TEXT NULL;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            connection,
            sqliteTransaction,
            "translation_engine_version",
            "ALTER TABLE subtitle_cues ADD COLUMN translation_engine_version TEXT NULL;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            connection,
            sqliteTransaction,
            "translation_source_fingerprint",
            "ALTER TABLE subtitle_cues ADD COLUMN translation_source_fingerprint TEXT NULL;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            connection,
            sqliteTransaction,
            "translation_confidence",
            "ALTER TABLE subtitle_cues ADD COLUMN translation_confidence REAL NULL;",
            cancellationToken);
        await AddColumnIfMissingAsync(
            connection,
            sqliteTransaction,
            "translation_reviewed_at_utc",
            "ALTER TABLE subtitle_cues ADD COLUMN translation_reviewed_at_utc TEXT NULL;",
            cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText = "UPDATE schema_info SET schema_version = 4;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task MigrateFromVersion4Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS voice_artifacts (
                artifact_id TEXT NOT NULL PRIMARY KEY,
                track_id TEXT NOT NULL,
                track_revision INTEGER NOT NULL,
                artifact_kind TEXT NOT NULL,
                phrase_id TEXT NULL,
                relative_path TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                sha256 TEXT NOT NULL,
                content_fingerprint TEXT NOT NULL,
                engine_id TEXT NOT NULL,
                engine_version TEXT NOT NULL,
                model_id TEXT NOT NULL,
                model_version TEXT NOT NULL,
                voice_id TEXT NOT NULL,
                duration_ms INTEGER NOT NULL,
                sample_rate INTEGER NOT NULL,
                channels INTEGER NOT NULL,
                status TEXT NOT NULL,
                timing_status TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                CONSTRAINT fk_voice_artifacts_track
                    FOREIGN KEY(track_id) REFERENCES subtitle_tracks(track_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_voice_artifacts_fingerprint
                ON voice_artifacts(content_fingerprint, artifact_kind, status, updated_at_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_voice_artifacts_track_revision
                ON voice_artifacts(track_id, track_revision, artifact_kind, status);

            CREATE TABLE IF NOT EXISTS voice_artifact_cues (
                artifact_id TEXT NOT NULL,
                cue_id TEXT NOT NULL,
                cue_order INTEGER NOT NULL,
                PRIMARY KEY(artifact_id, cue_id),
                CONSTRAINT fk_voice_artifact_cues_artifact
                    FOREIGN KEY(artifact_id) REFERENCES voice_artifacts(artifact_id) ON DELETE CASCADE,
                CONSTRAINT fk_voice_artifact_cues_cue
                    FOREIGN KEY(cue_id) REFERENCES subtitle_cues(cue_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS voice_phrase_boundaries (
                track_id TEXT NOT NULL,
                cue_id TEXT NOT NULL,
                mode TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY(track_id, cue_id),
                CONSTRAINT fk_voice_phrase_boundaries_track
                    FOREIGN KEY(track_id) REFERENCES subtitle_tracks(track_id) ON DELETE CASCADE,
                CONSTRAINT fk_voice_phrase_boundaries_cue
                    FOREIGN KEY(cue_id) REFERENCES subtitle_cues(cue_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS voice_cue_timings (
                track_id TEXT NOT NULL,
                track_revision INTEGER NOT NULL,
                phrase_id TEXT NOT NULL,
                natural_duration_ms INTEGER NOT NULL,
                target_duration_ms INTEGER NOT NULL,
                borrowed_gap_ms INTEGER NOT NULL,
                tempo REAL NOT NULL,
                status TEXT NOT NULL,
                suggested_max_characters INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY(track_id, track_revision, phrase_id),
                CONSTRAINT fk_voice_cue_timings_track
                    FOREIGN KEY(track_id) REFERENCES subtitle_tracks(track_id) ON DELETE CASCADE
            );

            UPDATE schema_info SET schema_version = 5;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string columnName,
        string alterSql,
        CancellationToken cancellationToken)
    {
        await using var existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('subtitle_cues') WHERE name = $name;";
        existsCommand.Parameters.AddWithValue("$name", columnName);
        if (Convert.ToInt32(await existsCommand.ExecuteScalarAsync(cancellationToken)) > 0)
        {
            return;
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.Transaction = transaction;
        alterCommand.CommandText = alterSql;
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ValidateVersion5SchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var columns = connection.CreateCommand();
        columns.CommandText = """
            SELECT COUNT(*) FROM pragma_table_info('subtitle_cues')
            WHERE name IN (
                'translation_source',
                'translation_engine_id',
                'translation_engine_version',
                'translation_source_fingerprint',
                'translation_confidence',
                'translation_reviewed_at_utc');
            """;
        var columnCount = Convert.ToInt32(await columns.ExecuteScalarAsync(cancellationToken));

        await using var tables = connection.CreateCommand();
        tables.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table'
              AND name IN (
                  'translation_memory', 'translation_cache', 'translation_job_items',
                  'voice_artifacts', 'voice_artifact_cues',
                  'voice_phrase_boundaries', 'voice_cue_timings');
            """;
        var tableCount = Convert.ToInt32(await tables.ExecuteScalarAsync(cancellationToken));
        if (columnCount != 6 || tableCount != 7)
        {
            throw new InvalidDataException("Database Vietsub schema 5 thiếu cấu trúc dịch hoặc giọng local bắt buộc.");
        }
    }

    private static List<string> DeserializeWarnings(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return ["subtitle_warning_data_invalid"];
        }
    }

    private static void ValidateTrack(VietsubSubtitleTrack track)
    {
        if (track.TrackId == Guid.Empty
            || track.Revision < 1
            || string.IsNullOrWhiteSpace(track.DisplayName)
            || string.IsNullOrWhiteSpace(track.LanguageCode)
            || string.IsNullOrWhiteSpace(track.Source))
        {
            throw new ArgumentException("Subtitle track không hợp lệ.", nameof(track));
        }

        var cueIds = new HashSet<Guid>();
        foreach (var cue in track.Cues)
        {
            if (cue.CueId == Guid.Empty
                || !cueIds.Add(cue.CueId)
                || cue.StartMilliseconds < 0
                || cue.EndMilliseconds <= cue.StartMilliseconds
                || string.IsNullOrWhiteSpace(cue.Speaker)
                || cue.Warnings.Count > 20)
            {
                throw new ArgumentException("Subtitle cue không hợp lệ.", nameof(track));
            }
        }


        var artifactIds = new HashSet<Guid>();
        foreach (var artifact in track.Artifacts)
        {
            if (!artifactIds.Add(artifact.ArtifactId))
            {
                throw new ArgumentException("Subtitle artifact không hợp lệ.", nameof(track));
            }
            ValidateArtifact(artifact);
        }
    }

    private static void ValidateArtifact(VietsubSubtitleArtifact artifact)
    {
        if (artifact.ArtifactId == Guid.Empty
            || artifact.TrackRevision < 1
            || string.IsNullOrWhiteSpace(artifact.ArtifactType)
            || string.IsNullOrWhiteSpace(artifact.WorkspaceRelativePath)
            || Path.IsPathFullyQualified(artifact.WorkspaceRelativePath)
            || artifact.WorkspaceRelativePath
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
                .Any(part => part == "..")
            || artifact.Sha256.Length != 64
            || !artifact.Sha256.All(Uri.IsHexDigit)
            || artifact.Status is not (VietsubSubtitleArtifactStatuses.Ready or VietsubSubtitleArtifactStatuses.Stale))
        {
            throw new ArgumentException("Subtitle artifact không hợp lệ.", nameof(artifact));
        }
    }
}

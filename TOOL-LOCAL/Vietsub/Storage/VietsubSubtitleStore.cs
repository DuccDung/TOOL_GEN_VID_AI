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
    string PreviewText);

internal sealed record VietsubTimelineWindowRecord(
    int TrackRevision,
    bool Truncated,
    IReadOnlyList<VietsubTimelineCueRecord> Cues);

internal sealed class VietsubSubtitleStore(VietsubAppPaths paths)
{
    private const int SchemaVersion = 2;

    public async Task InitializeAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        paths.CreateProjectDirectories(projectId);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS schema_info (
                schema_version INTEGER NOT NULL
            );

            INSERT INTO schema_info(schema_version)
            SELECT 2
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
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT schema_version FROM schema_info LIMIT 1;";
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken));
        if (version == 1)
        {
            await MigrateFromVersion1Async(connection, cancellationToken);
            version = SchemaVersion;
        }
        if (version != SchemaVersion)
        {
            throw new InvalidDataException("Phiên bản database Vietsub chưa được hỗ trợ.");
        }
    }

    public async Task SaveTrackAsync(
        Guid projectId,
        VietsubSubtitleTrack track,
        CancellationToken cancellationToken = default)
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

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VietsubSubtitleTrack>> LoadTracksAsync(
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
                   translation_locked, quality_status, warning_json, updated_at_utc
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
                UpdatedAtUtc = DateTime.Parse(cueReader.GetString(11), null, System.Globalization.DateTimeStyles.RoundtripKind)
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

    public async Task<VietsubTimelineWindowRecord?> LoadTimelineWindowAsync(
        Guid projectId,
        Guid trackId,
        long windowStartMilliseconds,
        long windowEndMilliseconds,
        int maximumCues,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        int? trackRevision;
        await using (var revisionCommand = connection.CreateCommand())
        {
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
        cueCommand.CommandText = """
            SELECT cue_id, cue_index, start_ms, end_ms,
                   original_locked, translation_locked, quality_status,
                   CASE WHEN warning_json <> '[]' THEN 1 ELSE 0 END AS has_warnings,
                   CASE WHEN length(trim(translated_text)) > 0 THEN 1 ELSE 0 END AS has_translation,
                   CASE
                       WHEN length(trim(translated_text)) > 0 THEN substr(translated_text, 1, 200)
                       ELSE substr(original_text, 1, 200)
                   END AS preview_text
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
                cueReader.GetString(9)));
        }

        var truncated = cues.Count > maximumCues;
        if (truncated)
        {
            cues.RemoveAt(cues.Count - 1);
        }
        return new VietsubTimelineWindowRecord(trackRevision.Value, truncated, cues);
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
                translation_locked, quality_status, warning_json, updated_at_utc)
            VALUES($cueId, $trackId, $cueIndex, $startMs, $endMs, $speaker,
                $originalText, $translatedText, $originalLocked,
                $translationLocked, $qualityStatus, $warningJson, $updatedAtUtc)
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
                quality_status = excluded.quality_status,
                warning_json = excluded.warning_json,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$cueId", cue.CueId.ToString("D"));
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
            if (artifact.ArtifactId == Guid.Empty
                || !artifactIds.Add(artifact.ArtifactId)
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
                throw new ArgumentException("Subtitle artifact không hợp lệ.", nameof(track));
            }
        }
    }
}

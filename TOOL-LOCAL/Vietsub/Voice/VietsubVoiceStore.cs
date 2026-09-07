using Microsoft.Data.Sqlite;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_LOCAL.Vietsub.Voice;

internal sealed class VietsubVoiceStore(VietsubAppPaths paths, VietsubSubtitleStore subtitleStore)
{
    public bool IsTrackRevisionCurrent(Guid projectId, Guid trackId, int expectedRevision)
    {
        if (projectId == Guid.Empty || trackId == Guid.Empty || expectedRevision < 1) return false;
        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = paths.GetProjectPath(projectId, "project.db"),
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT revision FROM subtitle_tracks WHERE track_id = $trackId;";
            command.Parameters.AddWithValue("$trackId", trackId.ToString("D"));
            var revision = command.ExecuteScalar();
            return revision is not null and not DBNull && Convert.ToInt32(revision) == expectedRevision;
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task<VietsubVoiceArtifact?> FindReusablePhraseAsync(
        Guid projectId,
        string contentFingerprint,
        CancellationToken cancellationToken = default)
    {
        ValidateFingerprint(contentFingerprint);
        await subtitleStore.InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = ArtifactSelect + Environment.NewLine + """
            WHERE content_fingerprint = $fingerprint
              AND artifact_kind = 'PHRASE'
              AND status = 'READY'
            ORDER BY updated_at_utc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$fingerprint", contentFingerprint);
        return await ReadSingleArtifactAsync(connection, command, cancellationToken);
    }

    public async Task<bool> SaveArtifactAsync(
        Guid projectId,
        VietsubVoiceArtifact artifact,
        int expectedTrackRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateArtifact(artifact);
        await subtitleStore.InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var revision = connection.CreateCommand();
        revision.Transaction = transaction;
        revision.CommandText = "SELECT revision FROM subtitle_tracks WHERE track_id = $trackId;";
        revision.Parameters.AddWithValue("$trackId", artifact.TrackId.ToString("D"));
        var current = await revision.ExecuteScalarAsync(cancellationToken);
        if (current is null or DBNull || Convert.ToInt32(current) != expectedTrackRevision)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO voice_artifacts(
                    artifact_id, track_id, track_revision, artifact_kind, phrase_id,
                    relative_path, size_bytes, sha256, content_fingerprint,
                    engine_id, engine_version, model_id, model_version, voice_id,
                    duration_ms, sample_rate, channels, status, timing_status,
                    created_at_utc, updated_at_utc)
                VALUES(
                    $artifactId, $trackId, $trackRevision, $artifactKind, $phraseId,
                    $relativePath, $sizeBytes, $sha256, $contentFingerprint,
                    $engineId, $engineVersion, $modelId, $modelVersion, $voiceId,
                    $durationMs, $sampleRate, $channels, $status, $timingStatus,
                    $createdAtUtc, $updatedAtUtc)
                ON CONFLICT(artifact_id) DO UPDATE SET
                    relative_path = excluded.relative_path,
                    size_bytes = excluded.size_bytes,
                    sha256 = excluded.sha256,
                    duration_ms = excluded.duration_ms,
                    sample_rate = excluded.sample_rate,
                    channels = excluded.channels,
                    status = excluded.status,
                    timing_status = excluded.timing_status,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            AddArtifactParameters(command, artifact);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM voice_artifact_cues WHERE artifact_id = $artifactId;";
            delete.Parameters.AddWithValue("$artifactId", artifact.ArtifactId.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        for (var index = 0; index < artifact.CueIds.Count; index++)
        {
            await using var mapping = connection.CreateCommand();
            mapping.Transaction = transaction;
            mapping.CommandText = """
                INSERT INTO voice_artifact_cues(artifact_id, cue_id, cue_order)
                VALUES($artifactId, $cueId, $cueOrder);
                """;
            mapping.Parameters.AddWithValue("$artifactId", artifact.ArtifactId.ToString("D"));
            mapping.Parameters.AddWithValue("$cueId", artifact.CueIds[index].ToString("D"));
            mapping.Parameters.AddWithValue("$cueOrder", index);
            await mapping.ExecuteNonQueryAsync(cancellationToken);
        }

        if (artifact.ArtifactKind == VietsubVoiceArtifactKinds.Timeline
            && artifact.Status == VietsubVoiceArtifactStatuses.Ready)
        {
            await using var stale = connection.CreateCommand();
            stale.Transaction = transaction;
            stale.CommandText = """
                UPDATE voice_artifacts
                SET status = 'STALE', updated_at_utc = $updatedAtUtc
                WHERE track_id = $trackId
                  AND artifact_kind = 'TIMELINE'
                  AND artifact_id <> $artifactId
                  AND status = 'READY';
                """;
            stale.Parameters.AddWithValue("$updatedAtUtc", artifact.UpdatedAtUtc.ToString("O"));
            stale.Parameters.AddWithValue("$trackId", artifact.TrackId.ToString("D"));
            stale.Parameters.AddWithValue("$artifactId", artifact.ArtifactId.ToString("D"));
            await stale.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task SaveTimingDiagnosticsAsync(
        Guid projectId,
        Guid trackId,
        int trackRevision,
        IReadOnlyList<VietsubVoiceTimingDiagnostic> diagnostics,
        CancellationToken cancellationToken = default)
    {
        await subtitleStore.InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var revision = connection.CreateCommand())
        {
            revision.Transaction = transaction;
            revision.CommandText = "SELECT revision FROM subtitle_tracks WHERE track_id = $trackId;";
            revision.Parameters.AddWithValue("$trackId", trackId.ToString("D"));
            var current = await revision.ExecuteScalarAsync(cancellationToken);
            if (current is null or DBNull || Convert.ToInt32(current) != trackRevision)
            {
                throw new VietsubVoiceException(VietsubVoiceErrorCodes.TrackChanged, "Phụ đề đã thay đổi trong khi đồng bộ giọng đọc.");
            }
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM voice_cue_timings WHERE track_id = $trackId AND track_revision = $revision;";
            delete.Parameters.AddWithValue("$trackId", trackId.ToString("D"));
            delete.Parameters.AddWithValue("$revision", trackRevision);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var diagnostic in diagnostics)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO voice_cue_timings(
                    track_id, track_revision, phrase_id, natural_duration_ms,
                    target_duration_ms, borrowed_gap_ms, tempo, status,
                    suggested_max_characters, updated_at_utc)
                VALUES(
                    $trackId, $revision, $phraseId, $naturalDuration,
                    $targetDuration, $borrowedGap, $tempo, $status,
                    $suggestedMaximumCharacters, $updatedAtUtc);
                """;
            insert.Parameters.AddWithValue("$trackId", trackId.ToString("D"));
            insert.Parameters.AddWithValue("$revision", trackRevision);
            insert.Parameters.AddWithValue("$phraseId", diagnostic.PhraseId);
            insert.Parameters.AddWithValue("$naturalDuration", diagnostic.NaturalDurationMilliseconds);
            insert.Parameters.AddWithValue("$targetDuration", diagnostic.TargetDurationMilliseconds);
            insert.Parameters.AddWithValue("$borrowedGap", diagnostic.BorrowedGapMilliseconds);
            insert.Parameters.AddWithValue("$tempo", diagnostic.Tempo);
            insert.Parameters.AddWithValue("$status", diagnostic.Status);
            insert.Parameters.AddWithValue("$suggestedMaximumCharacters", diagnostic.SuggestedMaximumCharacters);
            insert.Parameters.AddWithValue("$updatedAtUtc", DateTime.UtcNow.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<VietsubVoiceWorkspaceSummary> LoadWorkspaceAsync(
        Guid projectId,
        Guid? activeTrackId,
        int? activeTrackRevision,
        VietsubVoiceSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (activeTrackId is null || activeTrackRevision is null)
        {
            return new(settings, VietsubVoiceCatalog.Items, null, null, []);
        }

        await subtitleStore.InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var timelineCommand = connection.CreateCommand();
        timelineCommand.CommandText = ArtifactSelect + Environment.NewLine + """
            WHERE track_id = $trackId
              AND track_revision = $revision
              AND artifact_kind = 'TIMELINE'
              AND status = 'READY'
            ORDER BY updated_at_utc DESC
            LIMIT 1;
            """;
        timelineCommand.Parameters.AddWithValue("$trackId", activeTrackId.Value.ToString("D"));
        timelineCommand.Parameters.AddWithValue("$revision", activeTrackRevision.Value);
        var timeline = await ReadSingleArtifactAsync(connection, timelineCommand, cancellationToken);

        var diagnostics = new List<VietsubVoiceTimingDiagnostic>();
        await using var timing = connection.CreateCommand();
        timing.CommandText = """
            SELECT phrase_id, natural_duration_ms, target_duration_ms,
                   borrowed_gap_ms, tempo, status, suggested_max_characters
            FROM voice_cue_timings
            WHERE track_id = $trackId AND track_revision = $revision
            ORDER BY phrase_id;
            """;
        timing.Parameters.AddWithValue("$trackId", activeTrackId.Value.ToString("D"));
        timing.Parameters.AddWithValue("$revision", activeTrackRevision.Value);
        await using var reader = await timing.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            diagnostics.Add(new(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetDouble(4),
                reader.GetString(5),
                reader.GetInt32(6)));
        }
        return new(settings, VietsubVoiceCatalog.Items, timeline, null, diagnostics);
    }

    private async Task<VietsubVoiceArtifact?> ReadSingleArtifactAsync(
        SqliteConnection connection,
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        Guid artifactId;
        VietsubVoiceArtifact artifact;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            artifactId = Guid.Parse(reader.GetString(0));
            artifact = new(
                artifactId,
                Guid.Parse(reader.GetString(1)),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.GetString(13),
                reader.GetInt64(14),
                reader.GetInt32(15),
                reader.GetInt32(16),
                reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                DateTime.Parse(reader.GetString(19), null, System.Globalization.DateTimeStyles.RoundtripKind),
                DateTime.Parse(reader.GetString(20), null, System.Globalization.DateTimeStyles.RoundtripKind),
                []);
        }

        var cueIds = new List<Guid>();
        await using var cueCommand = connection.CreateCommand();
        cueCommand.CommandText = """
            SELECT cue_id FROM voice_artifact_cues
            WHERE artifact_id = $artifactId
            ORDER BY cue_order;
            """;
        cueCommand.Parameters.AddWithValue("$artifactId", artifactId.ToString("D"));
        await using var cueReader = await cueCommand.ExecuteReaderAsync(cancellationToken);
        while (await cueReader.ReadAsync(cancellationToken)) cueIds.Add(Guid.Parse(cueReader.GetString(0)));
        return artifact with { CueIds = cueIds };
    }

    private async Task<SqliteConnection> OpenAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = paths.GetProjectPath(projectId, "project.db"),
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void AddArtifactParameters(SqliteCommand command, VietsubVoiceArtifact artifact)
    {
        command.Parameters.AddWithValue("$artifactId", artifact.ArtifactId.ToString("D"));
        command.Parameters.AddWithValue("$trackId", artifact.TrackId.ToString("D"));
        command.Parameters.AddWithValue("$trackRevision", artifact.TrackRevision);
        command.Parameters.AddWithValue("$artifactKind", artifact.ArtifactKind);
        command.Parameters.AddWithValue("$phraseId", (object?)artifact.PhraseId ?? DBNull.Value);
        command.Parameters.AddWithValue("$relativePath", artifact.RelativePath);
        command.Parameters.AddWithValue("$sizeBytes", artifact.SizeBytes);
        command.Parameters.AddWithValue("$sha256", artifact.Sha256);
        command.Parameters.AddWithValue("$contentFingerprint", artifact.ContentFingerprint);
        command.Parameters.AddWithValue("$engineId", artifact.EngineId);
        command.Parameters.AddWithValue("$engineVersion", artifact.EngineVersion);
        command.Parameters.AddWithValue("$modelId", artifact.ModelId);
        command.Parameters.AddWithValue("$modelVersion", artifact.ModelVersion);
        command.Parameters.AddWithValue("$voiceId", artifact.VoiceId);
        command.Parameters.AddWithValue("$durationMs", artifact.DurationMilliseconds);
        command.Parameters.AddWithValue("$sampleRate", artifact.SampleRate);
        command.Parameters.AddWithValue("$channels", artifact.Channels);
        command.Parameters.AddWithValue("$status", artifact.Status);
        command.Parameters.AddWithValue("$timingStatus", (object?)artifact.TimingStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", artifact.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", artifact.UpdatedAtUtc.ToString("O"));
    }

    private static void ValidateArtifact(VietsubVoiceArtifact artifact)
    {
        if (artifact.ArtifactId == Guid.Empty || artifact.TrackId == Guid.Empty || artifact.TrackRevision < 1
            || artifact.ArtifactKind is not (VietsubVoiceArtifactKinds.Phrase or VietsubVoiceArtifactKinds.Timeline)
            || string.IsNullOrWhiteSpace(artifact.RelativePath) || Path.IsPathFullyQualified(artifact.RelativePath)
            || artifact.RelativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]).Any(part => part == "..")
            || artifact.SizeBytes <= 44 || artifact.DurationMilliseconds <= 0 || artifact.SampleRate <= 0 || artifact.Channels is < 1 or > 2
            || artifact.Sha256.Length != 64 || !artifact.Sha256.All(Uri.IsHexDigit)
            || artifact.ContentFingerprint.Length != 64 || !artifact.ContentFingerprint.All(Uri.IsHexDigit)
            || artifact.CueIds.Count == 0 || artifact.CueIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException("Voice artifact không hợp lệ.", nameof(artifact));
        }
    }

    private static void ValidateFingerprint(string fingerprint)
    {
        if (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Voice fingerprint không hợp lệ.", nameof(fingerprint));
        }
    }

    private const string ArtifactSelect = """
        SELECT artifact_id, track_id, track_revision, artifact_kind, phrase_id,
               relative_path, size_bytes, sha256, content_fingerprint,
               engine_id, engine_version, model_id, model_version, voice_id,
               duration_ms, sample_rate, channels, status, timing_status,
               created_at_utc, updated_at_utc
        FROM voice_artifacts
        """;
}

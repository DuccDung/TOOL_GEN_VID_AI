using Microsoft.Data.Sqlite;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_LOCAL.Vietsub.Storage;

internal sealed partial class VietsubSubtitleStore
{
    private static async Task MigrateToVersion6Async(SqliteConnection connection, CancellationToken token)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        await AddColumnIfMissingAsync(connection, transaction, "voice_enabled",
            "ALTER TABLE subtitle_cues ADD COLUMN voice_enabled INTEGER NOT NULL DEFAULT 1 CHECK (voice_enabled IN (0, 1));", token);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE schema_info SET schema_version = 6;";
        await command.ExecuteNonQueryAsync(token);
        transaction.Commit();
    }

    private static async Task ValidateVoiceSelectionSchemaAsync(SqliteConnection connection, CancellationToken token)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('subtitle_cues') WHERE name = 'voice_enabled';";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) != 1)
            throw new InvalidDataException("Database Vietsub schema 6 thiếu lựa chọn tạo giọng theo câu.");
    }

    public async Task<int> SetVoiceEnabledAsync(Guid projectId, Guid trackId, int expectedRevision,
        IReadOnlyList<Guid> cueIds, bool enabled, CancellationToken token = default)
    {
        if (trackId == Guid.Empty || expectedRevision < 1 || cueIds is null || cueIds.Count is < 1 or > 500
            || cueIds.Any(id => id == Guid.Empty) || cueIds.Distinct().Count() != cueIds.Count)
            throw new VietsubVoiceException("VOICE_SELECTION_INVALID", "Hãy chọn từ 1 đến 500 câu phụ đề hợp lệ.");
        await InitializeAsync(projectId, token);
        await using var connection = await OpenAsync(projectId, token);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM local_jobs WHERE status IN ('PENDING','RUNNING','PAUSING','PAUSED','INTERRUPTED');";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) > 0)
            throw new VietsubVoiceException("VOICE_SELECTION_JOB_ACTIVE", "Hãy hủy tác vụ local hiện tại trước khi đổi lựa chọn tạo giọng.");
        command.Parameters.AddWithValue("$track", trackId.ToString("D"));
        command.CommandText = "SELECT revision FROM subtitle_tracks WHERE track_id = $track;";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) != expectedRevision)
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.TrackChanged, "Phụ đề đã thay đổi. Hãy tải lại trước khi chọn câu tạo giọng.");
        var names = cueIds.Select((id, index) =>
        {
            var name = "$cue" + index;
            command.Parameters.AddWithValue(name, id.ToString("D"));
            return name;
        });
        var predicate = $"track_id = $track AND cue_id IN ({string.Join(',', names)})";
        command.CommandText = "SELECT COUNT(*) FROM subtitle_cues WHERE " + predicate;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) != cueIds.Count)
            throw new VietsubVoiceException("VOICE_SELECTION_INVALID", "Có câu không thuộc track đang chọn.");
        command.Parameters.AddWithValue("$enabled", enabled);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.CommandText = "UPDATE subtitle_cues SET voice_enabled = $enabled, updated_at_utc = $now WHERE "
            + predicate + " AND voice_enabled <> $enabled;";
        var changed = await command.ExecuteNonQueryAsync(token);
        if (changed > 0)
        {
            command.CommandText = """
                UPDATE subtitle_tracks SET revision = revision + 1, updated_at_utc = $now WHERE track_id = $track;
                UPDATE subtitle_artifacts SET status = 'STALE', updated_at_utc = $now WHERE track_id = $track;
                """;
            await command.ExecuteNonQueryAsync(token);
        }
        transaction.Commit();
        return expectedRevision + (changed > 0 ? 1 : 0);
    }
}

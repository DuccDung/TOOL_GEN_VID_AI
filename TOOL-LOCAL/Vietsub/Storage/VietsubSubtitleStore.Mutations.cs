using Microsoft.Data.Sqlite;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Subtitles;

namespace TOOL_LOCAL.Vietsub.Storage;

internal sealed record VietsubCueEditSnapshot(Guid TrackId, int Revision, int CueIndex, VietsubSubtitleCue Cue);

internal sealed partial class VietsubSubtitleStore
{
    public Task<VietsubCueEditSnapshot?> LoadCueEditAsync(Guid projectId, Guid trackId, Guid cueId, CancellationToken token) =>
        Task.Run<VietsubCueEditSnapshot?>(async () =>
        {
            await InitializeAsync(projectId, token);
            await using var connection = await OpenAsync(projectId, token);
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT revision FROM subtitle_tracks WHERE track_id=$track;";
            command.Parameters.AddWithValue("$track", trackId.ToString("D"));
            var revision = await command.ExecuteScalarAsync(token);
            if (revision is null or DBNull) return null;
            command.CommandText = CueSelect + " WHERE track_id=$track AND cue_id=$cue;";
            command.Parameters.AddWithValue("$cue", cueId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
                throw new VietsubSubtitleException("vietsub_subtitle_cue_not_found", "Không tìm thấy phân đoạn phụ đề.");
            return new(trackId, Convert.ToInt32(revision), reader.GetInt32(18), ReadCue(reader));
        }, token);

    public Task<bool> SaveCueEditAsync(Guid projectId, VietsubCueEditSnapshot snapshot, CancellationToken token) =>
        Task.Run(async () =>
        {
            await InitializeAsync(projectId, token);
            await using var connection = await OpenAsync(projectId, token);
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                UPDATE subtitle_tracks SET revision=revision+1,updated_at_utc=$now
                WHERE track_id=$track AND revision=$revision
                  AND EXISTS(SELECT 1 FROM subtitle_cues WHERE track_id=$track AND cue_id=$cue);
                """;
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$track", snapshot.TrackId.ToString("D"));
            command.Parameters.AddWithValue("$revision", snapshot.Revision);
            command.Parameters.AddWithValue("$cue", snapshot.Cue.CueId.ToString("D"));
            if (await command.ExecuteNonQueryAsync(token) != 1) return false;
            await UpsertCueAsync(connection, transaction, snapshot.TrackId, snapshot.Cue, snapshot.CueIndex, token);
            command.CommandText = "UPDATE subtitle_artifacts SET status='STALE',updated_at_utc=$now WHERE track_id=$track AND status='READY';";
            await command.ExecuteNonQueryAsync(token);
            await transaction.CommitAsync(token);
            return true;
        }, token);

    public async Task SaveOcrDeltaCheckpointAsync(Guid projectId, VietsubSubtitleTrack track, Guid jobId,
        string checkpointJson, int expectedRevision, IReadOnlySet<Guid> addedIds, CancellationToken token)
    {
        ValidateTrack(track);
        if (expectedRevision < 1 || track.Revision != checked(expectedRevision + 1))
            throw new InvalidOperationException("Revision checkpoint OCR không hợp lệ.");
        await InitializeAsync(projectId, token);
        await using var connection = await OpenAsync(projectId, token);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            UPDATE subtitle_tracks SET revision=$nextRevision,updated_at_utc=$now
            WHERE track_id=$track AND revision=$expected AND source='PADDLE_OCR_LOCAL';
            """;
        command.Parameters.AddWithValue("$nextRevision", track.Revision); command.Parameters.AddWithValue("$expected", expectedRevision);
        command.Parameters.AddWithValue("$now", track.UpdatedAtUtc.ToString("O")); command.Parameters.AddWithValue("$track", track.TrackId.ToString("D"));
        if (await command.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Track OCR đã thay đổi trước checkpoint.");
        command.CommandText = "SELECT cue_id,cue_index FROM subtitle_cues WHERE track_id=$track;";
        var order = new Dictionary<Guid, int>();
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) order.Add(Guid.Parse(reader.GetString(0)), reader.GetInt32(1));
        var nextOrder = track.Cues.Select((cue, index) => (cue, index)).ToArray();
        if (order.Keys.Except(nextOrder.Select(item => item.cue.CueId)).Any())
            throw new InvalidOperationException("Checkpoint OCR không được bỏ cue đã lưu.");
        var moved = nextOrder.Where(item => order.TryGetValue(item.cue.CueId, out var previous) && previous != item.index).ToArray();
        foreach (var item in moved)
        {
            command.CommandText = "UPDATE subtitle_cues SET cue_index=-cue_index-1 WHERE track_id=$track AND cue_id=$cue;";
            command.Parameters.AddWithValue("$cue", item.cue.CueId.ToString("D"));
            await command.ExecuteNonQueryAsync(token); command.Parameters.RemoveAt("$cue");
        }
        foreach (var item in nextOrder)
        {
            if (!order.ContainsKey(item.cue.CueId))
            {
                if (!addedIds.Contains(item.cue.CueId)) throw new InvalidOperationException("Checkpoint OCR thiếu cue đã lưu.");
                command.CommandText = "SELECT COUNT(*) FROM subtitle_cues WHERE cue_id=$candidate;";
                command.Parameters.AddWithValue("$candidate", item.cue.CueId.ToString("D"));
                if (Convert.ToInt64(await command.ExecuteScalarAsync(token)) != 0)
                    throw new InvalidOperationException("Cue OCR đã thuộc track khác.");
                command.Parameters.RemoveAt("$candidate");
                await UpsertCueAsync(connection, transaction, track.TrackId, item.cue, item.index, token);
            }
        }
        foreach (var item in moved)
        {
            command.CommandText = "UPDATE subtitle_cues SET cue_index=$index WHERE track_id=$track AND cue_id=$cue;";
            command.Parameters.AddWithValue("$index", item.index); command.Parameters.AddWithValue("$cue", item.cue.CueId.ToString("D"));
            await command.ExecuteNonQueryAsync(token); command.Parameters.RemoveAt("$index"); command.Parameters.RemoveAt("$cue");
        }
        command.CommandText = """
            UPDATE local_jobs SET checkpoint_json=$checkpoint,updated_at_utc=$now
            WHERE id=$job AND project_id=$project AND output_track_id=$track AND status IN ('RUNNING','PAUSING');
            """;
        command.Parameters.AddWithValue("$checkpoint", checkpointJson); command.Parameters.AddWithValue("$job", jobId.ToString("D"));
        command.Parameters.AddWithValue("$project", projectId.ToString("D"));
        if (await command.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Không thể lưu checkpoint của OCR job.");
        await transaction.CommitAsync(token);
    }
}

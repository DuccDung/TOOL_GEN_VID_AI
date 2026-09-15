using Microsoft.Data.Sqlite;
using System.Globalization;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Subtitles;

namespace TOOL_LOCAL.Vietsub.Storage;

internal sealed partial class VietsubSubtitleStore
{
    private const string CueSelect = """
        SELECT cue_id, start_ms, end_ms, speaker, original_text, translated_text,
               original_locked, translation_locked, quality_status, warning_json,
               updated_at_utc, voice_enabled, translation_source, translation_engine_id,
               translation_engine_version, translation_source_fingerprint, translation_confidence,
               translation_reviewed_at_utc, cue_index
        FROM subtitle_cues
        """;

    private static VietsubSubtitleCue ReadCue(SqliteDataReader reader) => new()
    {
        CueId = Guid.Parse(reader.GetString(0)), StartMilliseconds = reader.GetInt64(1), EndMilliseconds = reader.GetInt64(2),
        Speaker = reader.GetString(3), OriginalText = reader.GetString(4), TranslatedText = reader.GetString(5),
        OriginalLocked = reader.GetBoolean(6), TranslationLocked = reader.GetBoolean(7),
        QualityStatus = reader.IsDBNull(8) ? null : reader.GetString(8), Warnings = DeserializeWarnings(reader.GetString(9)),
        UpdatedAtUtc = DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind), VoiceEnabled = reader.GetBoolean(11),
        TranslationSource = reader.IsDBNull(12) ? null : reader.GetString(12),
        TranslationEngineId = reader.IsDBNull(13) ? null : reader.GetString(13),
        TranslationEngineVersion = reader.IsDBNull(14) ? null : reader.GetString(14),
        TranslationSourceFingerprint = reader.IsDBNull(15) ? null : reader.GetString(15),
        TranslationConfidence = reader.IsDBNull(16) ? null : reader.GetDouble(16),
        TranslationReviewedAtUtc = reader.IsDBNull(17) ? null : DateTime.Parse(reader.GetString(17), null, DateTimeStyles.RoundtripKind)
    };

    private static void RegisterQueryFunctions(SqliteConnection connection)
    {
        // Match the editor's existing .NET Unicode/culture semantics, including non-ASCII whitespace.
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        connection.CreateFunction<string?, bool>("vs_has_text", text => !string.IsNullOrWhiteSpace(text), isDeterministic: true);
        connection.CreateFunction<string, bool>("vs_has_warning", json => DeserializeWarnings(json).Count > 0, isDeterministic: true);
        connection.CreateFunction<string, string, bool>("vs_contains", (text, search) => compare.IndexOf(text, search, CompareOptions.IgnoreCase) >= 0, isDeterministic: true);
        connection.CreateFunction<string?, string, bool>("vs_equals", (text, other) => compare.Compare(text, other, CompareOptions.IgnoreCase) == 0, isDeterministic: true);
        connection.CreateFunction<string?, string, bool>("vs_code_equals", (text, other) => string.Equals(text, other, StringComparison.OrdinalIgnoreCase), isDeterministic: true);
        connection.CreateCollation("VS_CURRENT", (left, right) => compare.Compare(left, right, CompareOptions.IgnoreCase));
    }

    public Task<IReadOnlyList<VietsubSubtitleTrackSummary>> LoadSummariesAsync(Guid projectId, CancellationToken token) =>
        Task.Run<IReadOnlyList<VietsubSubtitleTrackSummary>>(async () =>
        {
            await InitializeAsync(projectId, token);
            await using var connection = await OpenAsync(projectId, token);
            RegisterQueryFunctions(connection);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT t.track_id, t.display_name, t.language_code, t.source, t.revision,
                       COUNT(c.cue_id), COALESCE(SUM(vs_has_text(c.translated_text)),0),
                       COALESCE(SUM(CASE WHEN c.cue_id IS NOT NULL AND
                          vs_has_warning(c.warning_json) THEN 1 ELSE 0 END),0),
                       t.updated_at_utc, COALESCE(SUM(c.voice_enabled),0),
                       COALESCE(SUM(CASE WHEN c.voice_enabled=1 AND vs_has_text(c.translated_text) THEN 1 ELSE 0 END),0)
                FROM subtitle_tracks t LEFT JOIN subtitle_cues c ON c.track_id=t.track_id
                GROUP BY t.track_id ORDER BY t.updated_at_utc DESC;
                """;
            var summaries = new List<VietsubSubtitleTrackSummary>();
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) summaries.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6),
                reader.GetInt32(7), DateTime.Parse(reader.GetString(8), null, DateTimeStyles.RoundtripKind), reader.GetInt32(9), reader.GetInt32(10)));
            return summaries;
        }, token);

    public Task<VietsubSubtitlePage?> LoadPageAsync(Guid projectId, Guid trackId, int offset, int pageSize,
        string search, string status, string speaker, int maximumTextCharacters, CancellationToken token) =>
        Task.Run<VietsubSubtitlePage?>(async () =>
        {
            await InitializeAsync(projectId, token);
            await using var connection = await OpenAsync(projectId, token);
            RegisterQueryFunctions(connection);
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT revision FROM subtitle_tracks WHERE track_id=$track;";
            command.Parameters.AddWithValue("$track", trackId.ToString("D"));
            var revision = await command.ExecuteScalarAsync(token);
            if (revision is null or DBNull) return null;
            var where = "WHERE track_id=$track";
            if (search.Length > 0)
            {
                where += " AND (vs_contains(original_text,$search) OR vs_contains(translated_text,$search) OR vs_contains(speaker,$search))";
                command.Parameters.AddWithValue("$search", search);
            }
            if (speaker.Length > 0) { where += " AND vs_equals(speaker,$speaker)"; command.Parameters.AddWithValue("$speaker", speaker); }
            where += status switch
            {
                "PENDING" => " AND NOT vs_has_text(translated_text)", "TRANSLATED" => " AND vs_has_text(translated_text)",
                "LOCKED" => " AND (original_locked=1 OR translation_locked=1)",
                "WARNING" => " AND (vs_has_warning(warning_json) OR vs_code_equals(quality_status,'WARNING') OR vs_code_equals(quality_status,'INVALID'))", _ => ""
            };
            command.CommandText = "SELECT COUNT(*) FROM subtitle_cues " + where;
            var total = Convert.ToInt32(await command.ExecuteScalarAsync(token));
            offset = Math.Clamp(offset, 0, Math.Max(0, total - 1));
            command.CommandText = CueSelect + " " + where + " ORDER BY cue_index LIMIT $limit OFFSET $offset;";
            command.Parameters.AddWithValue("$limit", pageSize); command.Parameters.AddWithValue("$offset", offset);
            var page = new List<VietsubSubtitleCueSummary>(pageSize);
            var characters = 0;
            await using (var reader = await command.ExecuteReaderAsync(token))
            {
                while (await reader.ReadAsync(token))
                {
                    var cue = ReadCue(reader);
                    var size = cue.OriginalText.Length + cue.TranslatedText.Length + cue.Speaker.Length + cue.Warnings.Sum(value => value.Length);
                    if (page.Count > 0 && characters + size > maximumTextCharacters) break;
                    characters += size;
                    page.Add(new(cue.CueId, reader.GetInt32(18), cue.StartMilliseconds, cue.EndMilliseconds, cue.Speaker,
                        cue.OriginalText, cue.TranslatedText, cue.OriginalLocked, cue.TranslationLocked, cue.QualityStatus,
                        cue.Warnings, cue.UpdatedAtUtc, cue.VoiceEnabled));
                }
            }
            command.CommandText = "SELECT DISTINCT speaker COLLATE VS_CURRENT FROM subtitle_cues WHERE track_id=$track AND vs_has_text(speaker) ORDER BY speaker COLLATE VS_CURRENT;";
            var speakers = new List<string>();
            await using (var reader = await command.ExecuteReaderAsync(token))
                while (await reader.ReadAsync(token)) speakers.Add(reader.GetString(0));
            return new(trackId, Convert.ToInt32(revision), offset, page.Count > 0 ? page.Count : pageSize,
                total, search, status, speaker, speakers, page);
        }, token);

    public async Task<IReadOnlyList<VietsubSubtitleCue>> LoadCueContextAsync(Guid projectId, Guid trackId,
        Guid cueId, int contextCount, CancellationToken token)
    {
        await InitializeAsync(projectId, token);
        await using var connection = await OpenAsync(projectId, token);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = CueSelect + " WHERE track_id=$track AND cue_id=$cue;";
        command.Parameters.AddWithValue("$track", trackId.ToString("D")); command.Parameters.AddWithValue("$cue", cueId.ToString("D"));
        VietsubSubtitleCue cue; int order;
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) return [];
            cue = ReadCue(reader); order = reader.GetInt32(18);
        }
        command.Parameters.AddWithValue("$start", cue.StartMilliseconds); command.Parameters.AddWithValue("$end", cue.EndMilliseconds);
        command.Parameters.AddWithValue("$order", order); command.Parameters.AddWithValue("$limit", Math.Clamp(contextCount, 0, 10));
        var before = new List<VietsubSubtitleCue>();
        command.CommandText = CueSelect + " WHERE track_id=$track AND (start_ms,end_ms,cue_index)<($start,$end,$order) ORDER BY start_ms DESC,end_ms DESC,cue_index DESC LIMIT $limit;";
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) before.Add(ReadCue(reader));
        before.Reverse(); before.Add(cue);
        command.CommandText = CueSelect + " WHERE track_id=$track AND (start_ms,end_ms,cue_index)>($start,$end,$order) ORDER BY start_ms,end_ms,cue_index LIMIT $limit;";
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) before.Add(ReadCue(reader));
        return before;
    }
}

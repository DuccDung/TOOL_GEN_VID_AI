using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_LOCAL.Vietsub.Translation;

internal static class VietsubTranslationMemorySourceKinds
{
    public const string ManualApproved = "MANUAL_APPROVED";
    public const string ImportedApproved = "IMPORTED_APPROVED";
}

internal static class VietsubTranslationJobItemStatuses
{
    public const string Pending = "PENDING";
    public const string Running = "RUNNING";
    public const string Completed = "COMPLETED";
    public const string Review = "REVIEW";
    public const string Invalid = "INVALID";
    public const string Stale = "STALE";
    public const string Failed = "FAILED";
    public const string SkippedLocked = "SKIPPED_LOCKED";

    public static bool IsTerminalForFingerprint(string value) => value is
        Completed or Review or Invalid or SkippedLocked;
}

internal sealed record VietsubTranslationMemoryMatch(
    Guid EntryId,
    string TranslatedText,
    string? ContextFingerprint,
    string SourceKind,
    int UseCount);

internal sealed record VietsubTranslationCacheHit(
    string CacheKey,
    string ResultJson,
    DateTime CreatedAtUtc);

internal sealed record VietsubTranslationJobItem(
    Guid JobId,
    Guid CueId,
    int SceneNumber,
    int ChapterNumber,
    string InputFingerprint,
    string Status,
    int AttemptCount,
    string? TranslatedText,
    double? Confidence,
    IReadOnlyList<string> Warnings,
    string? ErrorCode,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc);

internal sealed record VietsubTranslationJobItemSeed(
    Guid CueId,
    int SceneNumber,
    int ChapterNumber,
    string InputFingerprint);

internal sealed record VietsubTranslationCueCommit(
    Guid CueId,
    DateTime ExpectedUpdatedAtUtc,
    string ExpectedOriginalText,
    long ExpectedStartMilliseconds,
    long ExpectedEndMilliseconds,
    string ExpectedSpeaker,
    string InputFingerprint,
    string TranslatedText,
    string QualityStatus,
    double? Confidence,
    IReadOnlyList<string> Warnings,
    string EngineId,
    string EngineVersion,
    string TranslationSource = VietsubTranslationSources.LocalAuto);

internal sealed class VietsubTranslationStore(
    VietsubAppPaths paths,
    VietsubSubtitleStore subtitleStore)
{
    private const int MemoryEntryLimit = 500;
    private const int CacheEntryLimit = 1_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<VietsubTranslationMemoryMatch?> FindApprovedMemoryAsync(
        Guid projectId,
        string sourceLanguageCode,
        string targetLanguageCode,
        string sourceText,
        string? contextFingerprint,
        CancellationToken cancellationToken = default)
    {
        var sourceLanguage = VietsubTranslationLimits.NormalizeSourceLanguage(sourceLanguageCode);
        var targetLanguage = VietsubTranslationLimits.NormalizeTargetLanguage(targetLanguageCode);
        var sourceHash = HashNormalizedSource(sourceText);
        var normalizedContext = NormalizeOptionalFingerprint(contextFingerprint);
        await subtitleStore.InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT entry_id, translated_text, context_fingerprint, source_kind, use_count
            FROM translation_memory
            WHERE project_id = $projectId
              AND source_language_code = $sourceLanguage
              AND target_language_code = $targetLanguage
              AND normalized_source_hash = $sourceHash
              AND (context_fingerprint = $contextFingerprint OR context_fingerprint IS NULL)
            ORDER BY
              CASE WHEN context_fingerprint = $contextFingerprint THEN 0 ELSE 1 END,
              CASE WHEN source_kind = 'MANUAL_APPROVED' THEN 0 ELSE 1 END,
              updated_at_utc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        command.Parameters.AddWithValue("$sourceLanguage", sourceLanguage);
        command.Parameters.AddWithValue("$targetLanguage", targetLanguage);
        command.Parameters.AddWithValue("$sourceHash", sourceHash);
        command.Parameters.AddWithValue("$contextFingerprint", (object?)normalizedContext ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new VietsubTranslationMemoryMatch(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4));
    }

    public async Task SaveApprovedMemoryAsync(
        Guid projectId,
        string sourceLanguageCode,
        string targetLanguageCode,
        string sourceText,
        string translatedText,
        string? contextFingerprint,
        string sourceKind,
        CancellationToken cancellationToken = default)
    {
        var sourceLanguage = VietsubTranslationLimits.NormalizeSourceLanguage(sourceLanguageCode);
        var targetLanguage = VietsubTranslationLimits.NormalizeTargetLanguage(targetLanguageCode);
        var normalizedSource = NormalizeRequiredText(sourceText, 4_000, nameof(sourceText));
        var normalizedTranslation = NormalizeRequiredText(translatedText, 8_000, nameof(translatedText));
        var normalizedContext = NormalizeOptionalFingerprint(contextFingerprint);
        if (sourceKind is not (VietsubTranslationMemorySourceKinds.ManualApproved
            or VietsubTranslationMemorySourceKinds.ImportedApproved))
        {
            throw new ArgumentException("Nguồn Translation Memory không hợp lệ.", nameof(sourceKind));
        }

        var now = DateTime.UtcNow.ToString("O");
        await subtitleStore.InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO translation_memory(
                    entry_id, project_id, source_language_code, target_language_code,
                    normalized_source_hash, source_text, translated_text,
                    context_fingerprint, source_kind, use_count, created_at_utc, updated_at_utc)
                VALUES($entryId, $projectId, $sourceLanguage, $targetLanguage,
                    $sourceHash, $sourceText, $translatedText,
                    $contextFingerprint, $sourceKind, 0, $now, $now);
                """;
            command.Parameters.AddWithValue("$entryId", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
            command.Parameters.AddWithValue("$sourceLanguage", sourceLanguage);
            command.Parameters.AddWithValue("$targetLanguage", targetLanguage);
            command.Parameters.AddWithValue("$sourceHash", HashNormalizedSource(normalizedSource));
            command.Parameters.AddWithValue("$sourceText", normalizedSource);
            command.Parameters.AddWithValue("$translatedText", normalizedTranslation);
            command.Parameters.AddWithValue("$contextFingerprint", (object?)normalizedContext ?? DBNull.Value);
            command.Parameters.AddWithValue("$sourceKind", sourceKind);
            command.Parameters.AddWithValue("$now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await CleanupAsync(
            connection,
            (SqliteTransaction)transaction,
            "translation_memory",
            "entry_id",
            "ORDER BY use_count DESC, updated_at_utc DESC",
            projectId,
            MemoryEntryLimit,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkMemoryUsedAsync(
        Guid projectId,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        await subtitleStore.InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE translation_memory
            SET use_count = use_count + 1, updated_at_utc = $updatedAtUtc
            WHERE project_id = $projectId AND entry_id = $entryId;
            """;
        command.Parameters.AddWithValue("$updatedAtUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        command.Parameters.AddWithValue("$entryId", entryId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static string BuildCacheKey(
        string engineId,
        string engineVersion,
        string configurationFingerprint,
        string inputFingerprint)
    {
        var payload = JsonSerializer.Serialize(new
        {
            version = 1,
            engineId = NormalizeRequiredText(engineId, 120, nameof(engineId)),
            engineVersion = NormalizeRequiredText(engineVersion, 120, nameof(engineVersion)),
            configurationFingerprint = NormalizeFingerprint(configurationFingerprint),
            inputFingerprint = NormalizeFingerprint(inputFingerprint)
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public async Task<VietsubTranslationCacheHit?> TryGetCacheAsync(
        Guid projectId,
        string cacheKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeFingerprint(cacheKey);
        await subtitleStore.InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        string? resultJson = null;
        DateTime createdAtUtc = default;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT result_json, created_at_utc
                FROM translation_cache
                WHERE project_id = $projectId AND cache_key = $cacheKey;
                """;
            command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
            command.Parameters.AddWithValue("$cacheKey", normalizedKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                resultJson = reader.GetString(0);
                createdAtUtc = DateTime.Parse(
                    reader.GetString(1),
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind);
            }
        }

        if (resultJson is null)
        {
            return null;
        }

        try
        {
            using var _ = JsonDocument.Parse(resultJson);
        }
        catch (JsonException)
        {
            await DeleteCacheAsync(connection, projectId, normalizedKey, cancellationToken);
            return null;
        }

        await using var touch = connection.CreateCommand();
        touch.CommandText = """
            UPDATE translation_cache SET last_used_at_utc = $lastUsedAtUtc
            WHERE project_id = $projectId AND cache_key = $cacheKey;
            """;
        touch.Parameters.AddWithValue("$lastUsedAtUtc", DateTime.UtcNow.ToString("O"));
        touch.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        touch.Parameters.AddWithValue("$cacheKey", normalizedKey);
        await touch.ExecuteNonQueryAsync(cancellationToken);
        return new VietsubTranslationCacheHit(normalizedKey, resultJson, createdAtUtc);
    }

    public async Task SaveCacheAsync(
        Guid projectId,
        string engineId,
        string engineVersion,
        string configurationFingerprint,
        string inputFingerprint,
        string resultJson,
        CancellationToken cancellationToken = default)
    {
        using var _ = JsonDocument.Parse(resultJson);
        var cacheKey = BuildCacheKey(engineId, engineVersion, configurationFingerprint, inputFingerprint);
        var now = DateTime.UtcNow.ToString("O");
        await subtitleStore.InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO translation_cache(
                    cache_key, project_id, engine_id, engine_version,
                    configuration_fingerprint, input_fingerprint,
                    result_json, created_at_utc, last_used_at_utc)
                VALUES($cacheKey, $projectId, $engineId, $engineVersion,
                    $configurationFingerprint, $inputFingerprint,
                    $resultJson, $now, $now)
                ON CONFLICT(project_id, cache_key) DO UPDATE SET
                    result_json = excluded.result_json,
                    last_used_at_utc = excluded.last_used_at_utc;
                """;
            command.Parameters.AddWithValue("$cacheKey", cacheKey);
            command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
            command.Parameters.AddWithValue("$engineId", engineId.Trim());
            command.Parameters.AddWithValue("$engineVersion", engineVersion.Trim());
            command.Parameters.AddWithValue("$configurationFingerprint", NormalizeFingerprint(configurationFingerprint));
            command.Parameters.AddWithValue("$inputFingerprint", NormalizeFingerprint(inputFingerprint));
            command.Parameters.AddWithValue("$resultJson", resultJson);
            command.Parameters.AddWithValue("$now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await CleanupAsync(
            connection,
            (SqliteTransaction)transaction,
            "translation_cache",
            "cache_key",
            "ORDER BY last_used_at_utc DESC",
            projectId,
            CacheEntryLimit,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpsertJobItemsAsync(
        Guid projectId,
        Guid jobId,
        IReadOnlyList<VietsubTranslationJobItemSeed> seeds,
        CancellationToken cancellationToken = default)
    {
        if (jobId == Guid.Empty || seeds.Any(seed => seed.CueId == Guid.Empty))
        {
            throw new ArgumentException("Job item dịch local không hợp lệ.");
        }

        await subtitleStore.InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var seed in seeds)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO translation_job_items(
                    job_id, cue_id, scene_number, chapter_number, input_fingerprint,
                    status, attempt_count, warning_json, created_at_utc, updated_at_utc)
                VALUES($jobId, $cueId, $sceneNumber, $chapterNumber, $inputFingerprint,
                    'PENDING', 0, '[]', $now, $now)
                ON CONFLICT(job_id, cue_id) DO UPDATE SET
                    scene_number = excluded.scene_number,
                    chapter_number = excluded.chapter_number,
                    input_fingerprint = excluded.input_fingerprint,
                    status = CASE
                        WHEN translation_job_items.input_fingerprint = excluded.input_fingerprint
                         AND translation_job_items.status IN ('COMPLETED', 'REVIEW', 'INVALID', 'SKIPPED_LOCKED')
                        THEN translation_job_items.status
                        ELSE 'PENDING'
                    END,
                    translated_text = CASE
                        WHEN translation_job_items.input_fingerprint = excluded.input_fingerprint
                        THEN translation_job_items.translated_text ELSE NULL END,
                    confidence = CASE
                        WHEN translation_job_items.input_fingerprint = excluded.input_fingerprint
                        THEN translation_job_items.confidence ELSE NULL END,
                    warning_json = CASE
                        WHEN translation_job_items.input_fingerprint = excluded.input_fingerprint
                        THEN translation_job_items.warning_json ELSE '[]' END,
                    error_code = CASE
                        WHEN translation_job_items.input_fingerprint = excluded.input_fingerprint
                        THEN translation_job_items.error_code ELSE NULL END,
                    completed_at_utc = CASE
                        WHEN translation_job_items.input_fingerprint = excluded.input_fingerprint
                        THEN translation_job_items.completed_at_utc ELSE NULL END,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            command.Parameters.AddWithValue("$cueId", seed.CueId.ToString("D"));
            command.Parameters.AddWithValue("$sceneNumber", seed.SceneNumber);
            command.Parameters.AddWithValue("$chapterNumber", seed.ChapterNumber);
            command.Parameters.AddWithValue("$inputFingerprint", NormalizeFingerprint(seed.InputFingerprint));
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VietsubTranslationJobItem>> LoadJobItemsAsync(
        Guid projectId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        await subtitleStore.InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT job_id, cue_id, scene_number, chapter_number, input_fingerprint,
                   status, attempt_count, translated_text, confidence, warning_json,
                   error_code, created_at_utc, updated_at_utc, completed_at_utc
            FROM translation_job_items
            WHERE job_id = $jobId
            ORDER BY scene_number, cue_id;
            """;
        command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
        var results = new List<VietsubTranslationJobItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new VietsubTranslationJobItem(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                DeserializeWarnings(reader.GetString(9)),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                ParseDate(reader.GetString(11)),
                ParseDate(reader.GetString(12)),
                reader.IsDBNull(13) ? null : ParseDate(reader.GetString(13))));
        }

        return results;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> LoadLatestItemStatusesAsync(
        Guid projectId,
        Guid inputTrackId,
        CancellationToken cancellationToken = default)
    {
        await subtitleStore.InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT item.cue_id, item.status
            FROM translation_job_items item
            INNER JOIN local_jobs job ON job.id = item.job_id
            WHERE job.project_id = $projectId AND job.input_track_id = $trackId
            ORDER BY job.updated_at_utc DESC, item.updated_at_utc DESC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        command.Parameters.AddWithValue("$trackId", inputTrackId.ToString("D"));
        var results = new Dictionary<Guid, string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.TryAdd(Guid.Parse(reader.GetString(0)), reader.GetString(1));
        }
        return results;
    }

    public async Task UpdateJobItemAsync(
        Guid projectId,
        Guid jobId,
        Guid cueId,
        string inputFingerprint,
        string status,
        string checkpointJson,
        string? translatedText = null,
        double? confidence = null,
        IReadOnlyList<string>? warnings = null,
        string? errorCode = null,
        CancellationToken cancellationToken = default)
    {
        if (status is not (VietsubTranslationJobItemStatuses.Running
            or VietsubTranslationJobItemStatuses.Invalid
            or VietsubTranslationJobItemStatuses.Stale
            or VietsubTranslationJobItemStatuses.Failed
            or VietsubTranslationJobItemStatuses.SkippedLocked))
        {
            throw new ArgumentException("Trạng thái translation job item không hợp lệ.", nameof(status));
        }
        using var _ = JsonDocument.Parse(checkpointJson);
        await subtitleStore.InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow.ToString("O");
        await using (var item = connection.CreateCommand())
        {
            item.Transaction = (SqliteTransaction)transaction;
            item.CommandText = """
                UPDATE translation_job_items
                SET status = $status,
                    attempt_count = CASE WHEN $status = 'RUNNING' THEN attempt_count ELSE attempt_count + 1 END,
                    translated_text = $translatedText,
                    confidence = $confidence,
                    warning_json = $warningJson,
                    error_code = $errorCode,
                    updated_at_utc = $updatedAtUtc,
                    completed_at_utc = CASE
                        WHEN $status = 'RUNNING' THEN NULL ELSE $updatedAtUtc END
                WHERE job_id = $jobId AND cue_id = $cueId
                  AND input_fingerprint = $inputFingerprint;
                """;
            item.Parameters.AddWithValue("$status", status);
            item.Parameters.AddWithValue("$translatedText", (object?)translatedText ?? DBNull.Value);
            item.Parameters.AddWithValue("$confidence", (object?)confidence ?? DBNull.Value);
            item.Parameters.AddWithValue("$warningJson", JsonSerializer.Serialize((warnings ?? []).Take(20)));
            item.Parameters.AddWithValue("$errorCode", (object?)errorCode ?? DBNull.Value);
            item.Parameters.AddWithValue("$updatedAtUtc", now);
            item.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            item.Parameters.AddWithValue("$cueId", cueId.ToString("D"));
            item.Parameters.AddWithValue("$inputFingerprint", NormalizeFingerprint(inputFingerprint));
            if (await item.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Không thể cập nhật translation job item theo fingerprint.");
            }
        }

        await using (var job = connection.CreateCommand())
        {
            job.Transaction = (SqliteTransaction)transaction;
            job.CommandText = """
                UPDATE local_jobs
                SET checkpoint_json = $checkpointJson, updated_at_utc = $updatedAtUtc
                WHERE id = $jobId AND project_id = $projectId
                  AND status IN ('RUNNING', 'PAUSING');
                """;
            job.Parameters.AddWithValue("$checkpointJson", checkpointJson);
            job.Parameters.AddWithValue("$updatedAtUtc", now);
            job.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            job.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
            if (await job.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Không thể lưu checkpoint translation job đang chạy.");
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<bool> TryCommitCueResultAsync(
        Guid projectId,
        Guid jobId,
        VietsubTranslationCueCommit result,
        string checkpointJson,
        CancellationToken cancellationToken = default) =>
        TryCommitCueResultCoreAsync(projectId, jobId, result, checkpointJson, false, cancellationToken);

    public Task<bool> TryRecoverCloudCueResultAsync(Guid projectId, Guid jobId,
        VietsubTranslationCueCommit result, string checkpointJson, CancellationToken cancellationToken = default)
    {
        if (result.TranslationSource != VietsubTranslationSources.CloudAuto)
            throw new ArgumentException("Chỉ được khôi phục kết quả Cloud từ snapshot cũ.");
        return TryCommitCueResultCoreAsync(projectId, jobId, result, checkpointJson, true, cancellationToken);
    }

    private async Task<bool> TryCommitCueResultCoreAsync(Guid projectId, Guid jobId,
        VietsubTranslationCueCommit result, string checkpointJson, bool recoverCloud, CancellationToken cancellationToken)
    {
        if (result.TranslationSource is not (VietsubTranslationSources.LocalAuto or VietsubTranslationSources.CloudAuto))
            throw new ArgumentException("Nguồn bản dịch tự động không hợp lệ.");
        if (result.QualityStatus is not (VietsubTranslationQualityStatuses.Valid
            or VietsubTranslationQualityStatuses.Review))
        {
            throw new ArgumentException("Chỉ result VALID/REVIEW mới được ghi vào cue.", nameof(result));
        }
        using var _ = JsonDocument.Parse(checkpointJson);
        await subtitleStore.InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        await using (var cue = connection.CreateCommand())
        {
            cue.Transaction = (SqliteTransaction)transaction;
            cue.CommandText = """
                UPDATE subtitle_cues
                SET translated_text = $translatedText,
                    translation_locked = 0,
                    quality_status = $qualityStatus,
                    warning_json = $warningJson,
                    translation_source = $translationSource,
                    translation_engine_id = $engineId,
                    translation_engine_version = $engineVersion,
                    translation_source_fingerprint = $inputFingerprint,
                    translation_confidence = $confidence,
                    translation_reviewed_at_utc = NULL,
                    updated_at_utc = $updatedAtUtc
                WHERE cue_id = $cueId
                  AND updated_at_utc = $expectedUpdatedAtUtc
                  AND original_text = $expectedOriginalText
                  AND start_ms = $expectedStartMs
                  AND end_ms = $expectedEndMs
                  AND speaker = $expectedSpeaker
                  AND translation_locked = 0
                  AND ($translationSource <> 'CLOUD_AUTO' OR original_locked = 0)
                  AND (translation_source IS NULL OR translation_source <> 'MANUAL')
                  AND EXISTS (
                      SELECT 1 FROM local_jobs job
                      WHERE job.id = $jobId AND job.project_id = $projectId
                        AND ($translationSource <> 'CLOUD_AUTO' OR job.input_track_id = subtitle_cues.track_id)
                        AND (($recoverCloud = 0 AND job.status IN ('RUNNING', 'PAUSING'))
                          OR ($recoverCloud = 1 AND job.type = 'TRANSLATE_CLOUD' AND job.status IN ('CANCELLED', 'FAILED')))
                  )
                  AND EXISTS (
                      SELECT 1 FROM translation_job_items item
                      WHERE item.job_id = $jobId AND item.cue_id = $cueId
                        AND item.input_fingerprint = $inputFingerprint
                  );
                """;
            cue.Parameters.AddWithValue("$translatedText", NormalizeRequiredText(result.TranslatedText, 8_000, "translation"));
            cue.Parameters.AddWithValue("$translationSource", result.TranslationSource);
            cue.Parameters.AddWithValue("$qualityStatus", result.QualityStatus);
            cue.Parameters.AddWithValue("$warningJson", JsonSerializer.Serialize(result.Warnings.Take(20)));
            cue.Parameters.AddWithValue("$engineId", NormalizeRequiredText(result.EngineId, 120, "engineId"));
            cue.Parameters.AddWithValue("$engineVersion", NormalizeRequiredText(result.EngineVersion, 120, "engineVersion"));
            cue.Parameters.AddWithValue("$inputFingerprint", NormalizeFingerprint(result.InputFingerprint));
            cue.Parameters.AddWithValue("$confidence", (object?)result.Confidence ?? DBNull.Value);
            cue.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            cue.Parameters.AddWithValue("$cueId", result.CueId.ToString("D"));
            cue.Parameters.AddWithValue("$expectedUpdatedAtUtc", result.ExpectedUpdatedAtUtc.ToString("O"));
            cue.Parameters.AddWithValue("$expectedOriginalText", result.ExpectedOriginalText);
            cue.Parameters.AddWithValue("$expectedStartMs", result.ExpectedStartMilliseconds);
            cue.Parameters.AddWithValue("$expectedEndMs", result.ExpectedEndMilliseconds);
            cue.Parameters.AddWithValue("$expectedSpeaker", result.ExpectedSpeaker);
            cue.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            cue.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
            cue.Parameters.AddWithValue("$recoverCloud", recoverCloud ? 1 : 0);
            if (await cue.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await using (var metadata = connection.CreateCommand())
        {
            metadata.Transaction = (SqliteTransaction)transaction;
            metadata.CommandText = """
                UPDATE subtitle_tracks
                SET revision = revision + 1, updated_at_utc = $updatedAtUtc
                WHERE track_id = (SELECT track_id FROM subtitle_cues WHERE cue_id = $cueId);
                UPDATE subtitle_artifacts
                SET status = 'STALE', updated_at_utc = $updatedAtUtc
                WHERE track_id = (SELECT track_id FROM subtitle_cues WHERE cue_id = $cueId)
                  AND status = 'READY';
                UPDATE translation_job_items
                SET status = $itemStatus,
                    attempt_count = attempt_count + 1,
                    translated_text = $translatedText,
                    confidence = $confidence,
                    warning_json = $warningJson,
                    error_code = NULL,
                    updated_at_utc = $updatedAtUtc,
                    completed_at_utc = $updatedAtUtc
                WHERE job_id = $jobId AND cue_id = $cueId AND input_fingerprint = $inputFingerprint;
                UPDATE local_jobs
                SET checkpoint_json = $checkpointJson, updated_at_utc = $updatedAtUtc
                WHERE id = $jobId AND project_id = $projectId
                  AND (($recoverCloud = 0 AND status IN ('RUNNING', 'PAUSING'))
                    OR ($recoverCloud = 1 AND type = 'TRANSLATE_CLOUD' AND status IN ('CANCELLED', 'FAILED')));
                """;
            metadata.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            metadata.Parameters.AddWithValue("$cueId", result.CueId.ToString("D"));
            metadata.Parameters.AddWithValue(
                "$itemStatus",
                result.QualityStatus == VietsubTranslationQualityStatuses.Review
                    ? VietsubTranslationJobItemStatuses.Review
                    : VietsubTranslationJobItemStatuses.Completed);
            metadata.Parameters.AddWithValue("$translatedText", result.TranslatedText.Trim());
            metadata.Parameters.AddWithValue("$confidence", (object?)result.Confidence ?? DBNull.Value);
            metadata.Parameters.AddWithValue("$warningJson", JsonSerializer.Serialize(result.Warnings.Take(20)));
            metadata.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            metadata.Parameters.AddWithValue("$inputFingerprint", NormalizeFingerprint(result.InputFingerprint));
            metadata.Parameters.AddWithValue("$checkpointJson", checkpointJson);
            metadata.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
            metadata.Parameters.AddWithValue("$recoverCloud", recoverCloud ? 1 : 0);
            await metadata.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task CleanupAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string keyColumn,
        string orderBy,
        Guid projectId,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            DELETE FROM {table}
            WHERE project_id = $projectId
              AND {keyColumn} NOT IN (
                  SELECT {keyColumn} FROM {table}
                  WHERE project_id = $projectId
                  {orderBy}
                  LIMIT $limit
              );
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        command.Parameters.AddWithValue("$limit", limit);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteCacheAsync(
        SqliteConnection connection,
        Guid projectId,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM translation_cache WHERE project_id = $projectId AND cache_key = $cacheKey;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        command.Parameters.AddWithValue("$cacheKey", cacheKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.GetProjectPath(projectId, "project.db"),
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string HashNormalizedSource(string sourceText)
    {
        var normalized = string.Join(' ', (sourceText ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Source text của Translation Memory không được rỗng.", nameof(sourceText));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static string NormalizeRequiredText(string? value, int maximumLength, string field)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Length > maximumLength)
        {
            throw new ArgumentException($"{field} rỗng hoặc vượt giới hạn {maximumLength} ký tự.", field);
        }
        return normalized;
    }

    private static string NormalizeFingerprint(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Fingerprint dịch local phải là SHA-256 hợp lệ.");
        }
        return normalized;
    }

    private static string? NormalizeOptionalFingerprint(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeFingerprint(value);

    private static IReadOnlyList<string> DeserializeWarnings(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return ["translation_warning_data_invalid"];
        }
    }

    private static DateTime ParseDate(string value) => DateTime.Parse(
        value,
        null,
        System.Globalization.DateTimeStyles.RoundtripKind);
}

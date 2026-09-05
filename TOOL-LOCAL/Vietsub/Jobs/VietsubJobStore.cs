using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_LOCAL.Vietsub.Jobs;

internal sealed record VietsubJobEventRecord(
    long Id,
    Guid JobId,
    string EventType,
    string? Message,
    DateTime CreatedAtUtc);

internal sealed class VietsubJobStore(
    VietsubAppPaths paths,
    VietsubSubtitleStore workspaceDatabase,
    TimeProvider? timeProvider = null)
{
    private const int MaximumEventsPerJob = 200;
    private static readonly TimeProvider DefaultTimeProvider = TimeProvider.System;
    private readonly TimeProvider _timeProvider = timeProvider ?? DefaultTimeProvider;

    public async Task<VietsubLocalJob> CreateAsync(
        Guid projectId,
        string jobType,
        IReadOnlyList<string> stepCodes,
        string parametersJson = "{}",
        Guid? inputTrackId = null,
        int? inputRevision = null,
        int maxAttempts = 3,
        Guid? jobId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        var normalizedType = VietsubJobTypes.Normalize(jobType);
        var normalizedSteps = NormalizeStepCodes(stepCodes);
        parametersJson = ValidateJson(parametersJson, "{}", nameof(parametersJson));
        if (inputRevision is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(inputRevision));
        }
        if (maxAttempts is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        await InitializeAsync(projectId, cancellationToken);
        var now = UtcNow();
        var job = new VietsubLocalJob
        {
            Id = jobId ?? Guid.NewGuid(),
            ProjectId = projectId,
            Type = normalizedType,
            Status = VietsubJobStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ParametersJson = parametersJson,
            InputTrackId = inputTrackId,
            InputRevision = inputRevision,
            MaxAttempts = maxAttempts,
            Steps = normalizedSteps.Select((code, index) => new VietsubLocalJobStep
            {
                Index = index,
                Code = code,
                Status = VietsubJobStatus.Pending,
                UpdatedAtUtc = now
            }).ToList()
        };
        if (job.Id == Guid.Empty)
        {
            throw new ArgumentException("Mã job Vietsub không hợp lệ.", nameof(jobId));
        }

        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await InsertJobAsync(connection, (SqliteTransaction)transaction, job, cancellationToken);
            foreach (var step in job.Steps)
            {
                await InsertStepAsync(connection, (SqliteTransaction)transaction, job.Id, step, cancellationToken);
            }
            await AppendEventCoreAsync(
                connection,
                (SqliteTransaction)transaction,
                job.Id,
                "QUEUED",
                "Job đã được đưa vào hàng đợi.",
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new VietsubJobException(
                "vietsub_job_already_active",
                "Dự án đã có một job đang chờ, chạy hoặc tạm dừng.",
                exception);
        }

        return job;
    }

    public async Task<VietsubLocalJob?> GetAsync(
        Guid projectId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(projectId, jobId);
        await InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        return await LoadJobAsync(connection, transaction: null, projectId, jobId, cancellationToken);
    }

    public async Task<IReadOnlyList<VietsubLocalJob>> ListAsync(
        Guid projectId,
        int maximumCount = 20,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        maximumCount = Math.Clamp(maximumCount, 1, 100);
        await InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        var jobs = new List<VietsubLocalJob>();
        await using var command = connection.CreateCommand();
        command.CommandText = JobSelectSql + "\n" + """
            WHERE project_id = $projectId
            ORDER BY updated_at_utc DESC, created_at_utc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        command.Parameters.AddWithValue("$limit", maximumCount);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(ReadJob(reader));
        }
        await reader.DisposeAsync();

        foreach (var job in jobs)
        {
            job.Steps = await LoadStepsAsync(connection, transaction: null, job.Id, cancellationToken);
        }
        return jobs;
    }

    public async Task<VietsubLocalJob> TransitionAsync(
        Guid projectId,
        Guid jobId,
        VietsubJobStatus next,
        string eventType,
        string? eventMessage = null,
        string? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(projectId, jobId);
        var normalizedEventType = NormalizeEventType(eventType);
        await InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var job = await LoadJobRequiredAsync(
            connection,
            (SqliteTransaction)transaction,
            projectId,
            jobId,
            cancellationToken);
        var previous = job.Status;
        VietsubJobStateMachine.Apply(job, next, UtcNow(), errorCode, errorMessage);
        if (previous != next)
        {
            await UpdateJobAsync(
                connection,
                (SqliteTransaction)transaction,
                job,
                expectedStatus: previous,
                cancellationToken);
            await UpdateStepsForTransitionAsync(
                connection,
                (SqliteTransaction)transaction,
                job,
                next,
                cancellationToken);
            await AppendEventCoreAsync(
                connection,
                (SqliteTransaction)transaction,
                job.Id,
                normalizedEventType,
                eventMessage,
                job.UpdatedAtUtc,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetRequiredAsync(projectId, jobId, cancellationToken);
    }

    public async Task<VietsubLocalJob> UpdateProgressAsync(
        Guid projectId,
        Guid jobId,
        VietsubJobProgressUpdate update,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(projectId, jobId);
        ArgumentNullException.ThrowIfNull(update);
        var stepCode = NormalizeStepCode(update.StepCode);
        var stepProgress = ValidateProgress(update.StepProgressPercent, nameof(update.StepProgressPercent));
        var jobProgress = ValidateProgress(update.JobProgressPercent, nameof(update.JobProgressPercent));
        var statusMessage = NormalizeMessage(update.StatusMessage, 500);
        var checkpointJson = update.CheckpointJson is null
            ? null
            : ValidateJson(update.CheckpointJson, null, nameof(update.CheckpointJson));
        var metricsJson = update.MetricsJson is null
            ? null
            : ValidateJson(update.MetricsJson, null, nameof(update.MetricsJson));

        await InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var job = await LoadJobRequiredAsync(
            connection,
            (SqliteTransaction)transaction,
            projectId,
            jobId,
            cancellationToken);
        if (job.Status is not (VietsubJobStatus.Running or VietsubJobStatus.Pausing))
        {
            throw new VietsubJobException(
                "vietsub_job_transition_invalid",
                "Chỉ job đang chạy mới được cập nhật tiến độ.");
        }

        var step = job.Steps.SingleOrDefault(item => item.Code == stepCode)
            ?? throw new VietsubJobException(
                "vietsub_job_step_not_found",
                $"Job không có bước {stepCode}.");
        var now = UtcNow();
        step.StartedAtUtc ??= now;
        step.UpdatedAtUtc = now;
        step.ProgressPercent = stepProgress;
        step.Status = stepProgress >= 100
            ? VietsubJobStatus.Completed
            : job.Status == VietsubJobStatus.Pausing
                ? VietsubJobStatus.Pausing
                : VietsubJobStatus.Running;
        step.CompletedAtUtc = stepProgress >= 100 ? now : null;
        step.ErrorCode = null;
        step.ErrorMessage = null;
        job.ProgressPercent = jobProgress;
        job.StatusMessage = statusMessage ?? job.StatusMessage;
        job.UpdatedAtUtc = now;
        job.CheckpointJson = checkpointJson ?? job.CheckpointJson;
        job.MetricsJson = metricsJson ?? job.MetricsJson;

        await UpdateJobAsync(
            connection,
            (SqliteTransaction)transaction,
            job,
            expectedStatus: job.Status,
            cancellationToken);
        await UpdateStepAsync(connection, (SqliteTransaction)transaction, job.Id, step, cancellationToken);
        if (statusMessage is not null)
        {
            await AppendEventCoreAsync(
                connection,
                (SqliteTransaction)transaction,
                job.Id,
                "PROGRESS",
                statusMessage,
                now,
                cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await GetRequiredAsync(projectId, jobId, cancellationToken);
    }

    public async Task<VietsubLocalJob> SaveCheckpointAsync(
        Guid projectId,
        Guid jobId,
        string checkpointJson,
        CancellationToken cancellationToken = default)
    {
        checkpointJson = ValidateJson(checkpointJson, null, nameof(checkpointJson));
        ValidateIds(projectId, jobId);
        await InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE local_jobs
            SET checkpoint_json = $checkpointJson,
                updated_at_utc = $updatedAtUtc
            WHERE id = $jobId
              AND project_id = $projectId
              AND status IN ('RUNNING', 'PAUSING');
            """;
        command.Parameters.AddWithValue("$checkpointJson", checkpointJson);
        command.Parameters.AddWithValue("$updatedAtUtc", UtcNow().ToString("O"));
        command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new VietsubJobException(
                "vietsub_job_transition_invalid",
                "Không thể lưu checkpoint vì job không còn chạy.");
        }
        return await GetRequiredAsync(projectId, jobId, cancellationToken);
    }

    public async Task<VietsubLocalJob> BindOutputTrackAsync(
        Guid projectId,
        Guid jobId,
        Guid outputTrackId,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(projectId, jobId);
        if (outputTrackId == Guid.Empty)
        {
            throw new ArgumentException("Track đầu ra không hợp lệ.", nameof(outputTrackId));
        }
        await InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE local_jobs
            SET output_track_id = $outputTrackId,
                updated_at_utc = $updatedAtUtc
            WHERE id = $jobId
              AND project_id = $projectId
              AND (output_track_id IS NULL OR output_track_id = $outputTrackId);
            """;
        command.Parameters.AddWithValue("$outputTrackId", outputTrackId.ToString("D"));
        command.Parameters.AddWithValue("$updatedAtUtc", UtcNow().ToString("O"));
        command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new VietsubJobException(
                "vietsub_job_output_track_conflict",
                "Job đã được gắn với một track đầu ra khác.");
        }
        return await GetRequiredAsync(projectId, jobId, cancellationToken);
    }

    public async Task<int> MarkRunningAsInterruptedAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectId(projectId);
        await InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var jobIds = new List<Guid>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = (SqliteTransaction)transaction;
            select.CommandText = """
                SELECT id
                FROM local_jobs
                WHERE project_id = $projectId
                  AND status IN ('RUNNING', 'PAUSING');
                """;
            select.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                jobIds.Add(Guid.Parse(reader.GetString(0)));
            }
        }
        if (jobIds.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        var now = UtcNow();
        foreach (var jobId in jobIds)
        {
            await using var updateJob = connection.CreateCommand();
            updateJob.Transaction = (SqliteTransaction)transaction;
            updateJob.CommandText = """
                UPDATE local_jobs
                SET status = 'INTERRUPTED',
                    updated_at_utc = $now,
                    error_code = 'vietsub_job_interrupted',
                    error_message = 'Ứng dụng đã đóng trước khi job hoàn thành.'
                WHERE id = $jobId
                  AND status IN ('RUNNING', 'PAUSING');
                """;
            updateJob.Parameters.AddWithValue("$now", now.ToString("O"));
            updateJob.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            await updateJob.ExecuteNonQueryAsync(cancellationToken);

            await using var updateSteps = connection.CreateCommand();
            updateSteps.Transaction = (SqliteTransaction)transaction;
            updateSteps.CommandText = """
                UPDATE local_job_steps
                SET status = 'INTERRUPTED',
                    updated_at_utc = $now,
                    error_code = 'vietsub_job_interrupted',
                    error_message = 'Ứng dụng đã đóng trước khi bước xử lý hoàn thành.'
                WHERE job_id = $jobId
                  AND status IN ('RUNNING', 'PAUSING');
                """;
            updateSteps.Parameters.AddWithValue("$now", now.ToString("O"));
            updateSteps.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            await updateSteps.ExecuteNonQueryAsync(cancellationToken);
            await AppendEventCoreAsync(
                connection,
                (SqliteTransaction)transaction,
                jobId,
                "INTERRUPTED",
                "Job được phục hồi ở trạng thái gián đoạn.",
                now,
                cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return jobIds.Count;
    }

    internal async Task<IReadOnlyList<VietsubJobEventRecord>> LoadEventsAsync(
        Guid projectId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(projectId, jobId);
        await InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        var events = new List<VietsubJobEventRecord>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT event.id, event.job_id, event.event_type, event.message, event.created_at_utc
            FROM local_job_events event
            INNER JOIN local_jobs job ON job.id = event.job_id
            WHERE event.job_id = $jobId
              AND job.project_id = $projectId
            ORDER BY event.id;
            """;
        command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
        command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new VietsubJobEventRecord(
                reader.GetInt64(0),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                ParseDate(reader.GetString(4))));
        }
        return events;
    }

    internal async Task AppendDiagnosticEventAsync(
        Guid projectId,
        Guid jobId,
        string eventType,
        string? message,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(projectId, jobId);
        var normalizedEventType = NormalizeEventType(eventType);
        await InitializeAsync(projectId, cancellationToken);
        await using var connection = await OpenAsync(projectId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        _ = await LoadJobRequiredAsync(
            connection,
            (SqliteTransaction)transaction,
            projectId,
            jobId,
            cancellationToken);
        await AppendEventCoreAsync(
            connection,
            (SqliteTransaction)transaction,
            jobId,
            normalizedEventType,
            message,
            UtcNow(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<VietsubLocalJob> GetRequiredAsync(
        Guid projectId,
        Guid jobId,
        CancellationToken cancellationToken) =>
        await GetAsync(projectId, jobId, cancellationToken)
        ?? throw JobNotFound();

    private async Task InitializeAsync(Guid projectId, CancellationToken cancellationToken) =>
        await workspaceDatabase.InitializeAsync(projectId, cancellationToken);

    private async Task<SqliteConnection> OpenAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = paths.GetProjectPath(projectId, "project.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 5
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task InsertJobAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        VietsubLocalJob job,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO local_jobs(
                id, project_id, type, status, created_at_utc, started_at_utc,
                updated_at_utc, completed_at_utc, progress_percent, status_message,
                input_track_id, output_track_id, input_revision, parameters_json,
                checkpoint_json, metrics_json, attempt_count, max_attempts,
                error_code, error_message)
            VALUES(
                $id, $projectId, $type, $status, $createdAtUtc, NULL,
                $updatedAtUtc, NULL, 0, NULL,
                $inputTrackId, NULL, $inputRevision, $parametersJson,
                NULL, NULL, 0, $maxAttempts, NULL, NULL);
            """;
        command.Parameters.AddWithValue("$id", job.Id.ToString("D"));
        command.Parameters.AddWithValue("$projectId", job.ProjectId.ToString("D"));
        command.Parameters.AddWithValue("$type", job.Type);
        command.Parameters.AddWithValue("$status", VietsubJobStatusNames.ToStorage(job.Status));
        command.Parameters.AddWithValue("$createdAtUtc", job.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", job.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$inputTrackId", DbValue(job.InputTrackId));
        command.Parameters.AddWithValue("$inputRevision", DbValue(job.InputRevision));
        command.Parameters.AddWithValue("$parametersJson", job.ParametersJson);
        command.Parameters.AddWithValue("$maxAttempts", job.MaxAttempts);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertStepAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid jobId,
        VietsubLocalJobStep step,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO local_job_steps(
                job_id, step_index, code, status, progress_percent,
                started_at_utc, updated_at_utc, completed_at_utc,
                error_code, error_message)
            VALUES($jobId, $stepIndex, $code, $status, 0, NULL, $updatedAtUtc, NULL, NULL, NULL);
            """;
        command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
        command.Parameters.AddWithValue("$stepIndex", step.Index);
        command.Parameters.AddWithValue("$code", step.Code);
        command.Parameters.AddWithValue("$status", VietsubJobStatusNames.ToStorage(step.Status));
        command.Parameters.AddWithValue("$updatedAtUtc", step.UpdatedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateJobAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        VietsubLocalJob job,
        VietsubJobStatus expectedStatus,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE local_jobs
            SET status = $status,
                started_at_utc = $startedAtUtc,
                updated_at_utc = $updatedAtUtc,
                completed_at_utc = $completedAtUtc,
                progress_percent = $progressPercent,
                status_message = $statusMessage,
                output_track_id = $outputTrackId,
                checkpoint_json = $checkpointJson,
                metrics_json = $metricsJson,
                attempt_count = $attemptCount,
                error_code = $errorCode,
                error_message = $errorMessage
            WHERE id = $id
              AND project_id = $projectId
              AND status = $expectedStatus;
            """;
        command.Parameters.AddWithValue("$status", VietsubJobStatusNames.ToStorage(job.Status));
        command.Parameters.AddWithValue("$startedAtUtc", DbValue(job.StartedAtUtc));
        command.Parameters.AddWithValue("$updatedAtUtc", job.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedAtUtc", DbValue(job.CompletedAtUtc));
        command.Parameters.AddWithValue("$progressPercent", job.ProgressPercent);
        command.Parameters.AddWithValue("$statusMessage", DbValue(job.StatusMessage));
        command.Parameters.AddWithValue("$outputTrackId", DbValue(job.OutputTrackId));
        command.Parameters.AddWithValue("$checkpointJson", DbValue(job.CheckpointJson));
        command.Parameters.AddWithValue("$metricsJson", DbValue(job.MetricsJson));
        command.Parameters.AddWithValue("$attemptCount", job.AttemptCount);
        command.Parameters.AddWithValue("$errorCode", DbValue(job.ErrorCode));
        command.Parameters.AddWithValue("$errorMessage", DbValue(job.ErrorMessage));
        command.Parameters.AddWithValue("$id", job.Id.ToString("D"));
        command.Parameters.AddWithValue("$projectId", job.ProjectId.ToString("D"));
        command.Parameters.AddWithValue("$expectedStatus", VietsubJobStatusNames.ToStorage(expectedStatus));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new VietsubJobException(
                "vietsub_job_transition_invalid",
                "Trạng thái job đã thay đổi bởi một thao tác khác.");
        }
    }

    private static async Task UpdateStepAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid jobId,
        VietsubLocalJobStep step,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE local_job_steps
            SET status = $status,
                progress_percent = $progressPercent,
                started_at_utc = $startedAtUtc,
                updated_at_utc = $updatedAtUtc,
                completed_at_utc = $completedAtUtc,
                error_code = $errorCode,
                error_message = $errorMessage
            WHERE job_id = $jobId AND step_index = $stepIndex;
            """;
        command.Parameters.AddWithValue("$status", VietsubJobStatusNames.ToStorage(step.Status));
        command.Parameters.AddWithValue("$progressPercent", step.ProgressPercent);
        command.Parameters.AddWithValue("$startedAtUtc", DbValue(step.StartedAtUtc));
        command.Parameters.AddWithValue("$updatedAtUtc", step.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedAtUtc", DbValue(step.CompletedAtUtc));
        command.Parameters.AddWithValue("$errorCode", DbValue(step.ErrorCode));
        command.Parameters.AddWithValue("$errorMessage", DbValue(step.ErrorMessage));
        command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
        command.Parameters.AddWithValue("$stepIndex", step.Index);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateStepsForTransitionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        VietsubLocalJob job,
        VietsubJobStatus next,
        CancellationToken cancellationToken)
    {
        string? sql = next switch
        {
            VietsubJobStatus.Pending => """
                UPDATE local_job_steps
                SET status = 'PENDING', error_code = NULL, error_message = NULL,
                    completed_at_utc = NULL, updated_at_utc = $now
                WHERE job_id = $jobId AND status <> 'COMPLETED';
                """,
            VietsubJobStatus.Pausing => """
                UPDATE local_job_steps
                SET status = 'PAUSING', updated_at_utc = $now
                WHERE job_id = $jobId AND status = 'RUNNING';
                """,
            VietsubJobStatus.Paused => BuildStepTerminalSql(VietsubJobStatusNames.Paused),
            VietsubJobStatus.Interrupted => BuildStepTerminalSql(VietsubJobStatusNames.Interrupted),
            VietsubJobStatus.Failed => BuildStepTerminalSql(VietsubJobStatusNames.Failed),
            VietsubJobStatus.Cancelled => BuildStepTerminalSql(VietsubJobStatusNames.Cancelled),
            VietsubJobStatus.Completed => """
                UPDATE local_job_steps
                SET status = 'COMPLETED', progress_percent = 100,
                    completed_at_utc = $now, updated_at_utc = $now,
                    error_code = NULL, error_message = NULL
                WHERE job_id = $jobId AND status IN ('PENDING', 'RUNNING', 'PAUSING');
                """,
            _ => null
        };
        if (sql is null)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$now", job.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$jobId", job.Id.ToString("D"));
        if (next is VietsubJobStatus.Paused
            or VietsubJobStatus.Interrupted
            or VietsubJobStatus.Failed
            or VietsubJobStatus.Cancelled)
        {
            command.Parameters.AddWithValue("$errorCode", DbValue(job.ErrorCode));
            command.Parameters.AddWithValue("$errorMessage", DbValue(job.ErrorMessage));
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string BuildStepTerminalSql(string status) => $"""
        UPDATE local_job_steps
        SET status = '{status}', updated_at_utc = $now,
            error_code = $errorCode, error_message = $errorMessage
        WHERE job_id = $jobId AND status IN ('RUNNING', 'PAUSING');
        """;

    private static async Task AppendEventCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid jobId,
        string eventType,
        string? message,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO local_job_events(job_id, event_type, message, created_at_utc)
                VALUES($jobId, $eventType, $message, $createdAtUtc);
                """;
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            command.Parameters.AddWithValue("$eventType", NormalizeEventType(eventType));
            command.Parameters.AddWithValue("$message", DbValue(NormalizeMessage(message, 1000)));
            command.Parameters.AddWithValue("$createdAtUtc", now.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var trim = connection.CreateCommand();
        trim.Transaction = transaction;
        trim.CommandText = """
            DELETE FROM local_job_events
            WHERE id IN (
                SELECT id
                FROM local_job_events
                WHERE job_id = $jobId
                ORDER BY id DESC
                LIMIT -1 OFFSET $maximumEvents
            );
            """;
        trim.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
        trim.Parameters.AddWithValue("$maximumEvents", MaximumEventsPerJob);
        await trim.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<VietsubLocalJob> LoadJobRequiredAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        Guid jobId,
        CancellationToken cancellationToken) =>
        await LoadJobAsync(connection, transaction, projectId, jobId, cancellationToken)
        ?? throw JobNotFound();

    private static async Task<VietsubLocalJob?> LoadJobAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid projectId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        VietsubLocalJob? job = null;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = JobSelectSql + "\n" + """
                WHERE id = $jobId AND project_id = $projectId;
                """;
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            command.Parameters.AddWithValue("$projectId", projectId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                job = ReadJob(reader);
            }
        }
        if (job is not null)
        {
            job.Steps = await LoadStepsAsync(connection, transaction, job.Id, cancellationToken);
        }
        return job;
    }

    private static async Task<List<VietsubLocalJobStep>> LoadStepsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var steps = new List<VietsubLocalJobStep>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT step_index, code, status, progress_percent,
                   started_at_utc, updated_at_utc, completed_at_utc,
                   error_code, error_message
            FROM local_job_steps
            WHERE job_id = $jobId
            ORDER BY step_index;
            """;
        command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            steps.Add(new VietsubLocalJobStep
            {
                Index = reader.GetInt32(0),
                Code = reader.GetString(1),
                Status = VietsubJobStatusNames.Parse(reader.GetString(2)),
                ProgressPercent = reader.GetDouble(3),
                StartedAtUtc = ReadNullableDate(reader, 4),
                UpdatedAtUtc = ParseDate(reader.GetString(5)),
                CompletedAtUtc = ReadNullableDate(reader, 6),
                ErrorCode = reader.IsDBNull(7) ? null : reader.GetString(7),
                ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }
        return steps;
    }

    private static VietsubLocalJob ReadJob(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        ProjectId = Guid.Parse(reader.GetString(1)),
        Type = reader.GetString(2),
        Status = VietsubJobStatusNames.Parse(reader.GetString(3)),
        CreatedAtUtc = ParseDate(reader.GetString(4)),
        StartedAtUtc = ReadNullableDate(reader, 5),
        UpdatedAtUtc = ParseDate(reader.GetString(6)),
        CompletedAtUtc = ReadNullableDate(reader, 7),
        ProgressPercent = reader.GetDouble(8),
        StatusMessage = reader.IsDBNull(9) ? null : reader.GetString(9),
        InputTrackId = ReadNullableGuid(reader, 10),
        OutputTrackId = ReadNullableGuid(reader, 11),
        InputRevision = reader.IsDBNull(12) ? null : reader.GetInt32(12),
        ParametersJson = reader.GetString(13),
        CheckpointJson = reader.IsDBNull(14) ? null : reader.GetString(14),
        MetricsJson = reader.IsDBNull(15) ? null : reader.GetString(15),
        AttemptCount = reader.GetInt32(16),
        MaxAttempts = reader.GetInt32(17),
        ErrorCode = reader.IsDBNull(18) ? null : reader.GetString(18),
        ErrorMessage = reader.IsDBNull(19) ? null : reader.GetString(19)
    };

    private static readonly string JobSelectSql = """
        SELECT id, project_id, type, status, created_at_utc, started_at_utc,
               updated_at_utc, completed_at_utc, progress_percent, status_message,
               input_track_id, output_track_id, input_revision, parameters_json,
               checkpoint_json, metrics_json, attempt_count, max_attempts,
               error_code, error_message
        FROM local_jobs
        """;

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static IReadOnlyList<string> NormalizeStepCodes(IReadOnlyList<string> stepCodes)
    {
        ArgumentNullException.ThrowIfNull(stepCodes);
        if (stepCodes.Count is < 1 or > 20)
        {
            throw new ArgumentException("Job phải có từ 1 đến 20 bước.", nameof(stepCodes));
        }
        var normalized = stepCodes.Select(NormalizeStepCode).ToArray();
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw new ArgumentException("Mã bước trong job không được trùng.", nameof(stepCodes));
        }
        return normalized;
    }

    private static string NormalizeStepCode(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length is < 1 or > 64
            || normalized.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException("Mã bước job Vietsub không hợp lệ.", nameof(value));
        }
        return normalized;
    }

    private static string NormalizeEventType(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length is < 1 or > 64
            || normalized.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException("Loại sự kiện job Vietsub không hợp lệ.", nameof(value));
        }
        return normalized;
    }

    private static string ValidateJson(string? value, string? fallback, string parameterName)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (normalized is null || normalized.Length > 64 * 1024)
        {
            throw new ArgumentException("Dữ liệu JSON của job không hợp lệ.", parameterName);
        }
        try
        {
            using var document = JsonDocument.Parse(normalized);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException();
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Dữ liệu JSON của job phải là một object hợp lệ.", parameterName, exception);
        }
        return normalized;
    }

    private static double ValidateProgress(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
        return value;
    }

    private static string? NormalizeMessage(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static void ValidateIds(Guid projectId, Guid jobId)
    {
        ValidateProjectId(projectId);
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("Mã job Vietsub không hợp lệ.", nameof(jobId));
        }
    }

    private static void ValidateProjectId(Guid projectId)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Mã dự án Vietsub không hợp lệ.", nameof(projectId));
        }
    }

    private static Guid? ReadNullableGuid(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));

    private static DateTime? ReadNullableDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));

    private static DateTime ParseDate(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static object DbValue(Guid? value) => value is null ? DBNull.Value : value.Value.ToString("D");

    private static object DbValue(int? value) => value ?? (object)DBNull.Value;

    private static object DbValue(DateTime? value) => value is null ? DBNull.Value : value.Value.ToString("O");

    private static object DbValue(string? value) => value ?? (object)DBNull.Value;

    private static VietsubJobException JobNotFound() => new(
        "vietsub_job_not_found",
        "Không tìm thấy job trong dự án Vietsub hiện tại.");
}

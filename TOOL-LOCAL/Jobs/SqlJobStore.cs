using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;

namespace TOOL_LOCAL.Jobs;

public sealed class SqlJobStore(
    string connectionString,
    IDbContextFactory<VideoFactoryDbContext> dbContextFactory) : IJobStore
{
    public async Task<Guid> EnqueueAsync(EnqueueJobCommand command, CancellationToken cancellationToken = default)
    {
        ValidateEnqueue(command);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existingId = await dbContext.Jobs
                .Where(x => x.IdempotencyKey == command.IdempotencyKey)
                .Select(x => (Guid?)x.JobId)
                .SingleOrDefaultAsync(cancellationToken);
            if (existingId.HasValue)
            {
                return existingId.Value;
            }
        }

        var now = DateTime.UtcNow;
        var jobId = Guid.NewGuid();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Jobs.Add(new Job
        {
            JobId = jobId,
            ProjectId = command.ProjectId,
            SceneId = command.SceneId,
            ParentJobId = command.ParentJobId,
            JobType = command.JobType,
            Status = JobStatuses.Pending,
            Priority = command.Priority,
            Attempt = 0,
            MaxAttempts = command.MaxAttempts,
            ProgressPercent = 0,
            AvailableAtUtc = now,
            IdempotencyKey = NormalizeOptional(command.IdempotencyKey, 450),
            PayloadJson = command.PayloadJson,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        foreach (var dependencyId in command.DependsOnJobIds?.Distinct() ?? [])
        {
            dbContext.JobDependencies.Add(new JobDependency
            {
                JobDependencyId = Guid.NewGuid(),
                JobId = jobId,
                DependsOnJobId = dependencyId,
                CreatedAtUtc = now
            });
        }

        dbContext.JobEvents.Add(NewEvent(jobId, "Enqueued", null, JobStatuses.Pending, "Job added to the local queue.", now));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return jobId;
    }

    public async Task RecoverExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("[vf].[usp_RecoverExpiredJobs]", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 30
        };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // Consume recovered IDs so the connection can be returned cleanly.
        }
    }

    public async Task<ClaimedJob?> ClaimAsync(
        string workerId,
        int leaseSeconds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("[vf].[usp_ClaimNextJob]", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 30
        };
        command.Parameters.Add(new SqlParameter("@WorkerId", SqlDbType.NVarChar, 200) { Value = workerId });
        command.Parameters.Add(new SqlParameter("@LeaseSeconds", SqlDbType.Int) { Value = leaseSeconds });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ClaimedJob(
            reader.GetGuid(reader.GetOrdinal("JobId")),
            reader.GetGuid(reader.GetOrdinal("ProjectId")),
            ReadNullableGuid(reader, "SceneId"),
            ReadNullableGuid(reader, "ParentJobId"),
            reader.GetString(reader.GetOrdinal("JobType")),
            reader.GetInt32(reader.GetOrdinal("Attempt")),
            reader.GetInt32(reader.GetOrdinal("MaxAttempts")),
            ReadNullableString(reader, "PayloadJson"));
    }

    public async Task<bool> HeartbeatAsync(
        Guid jobId,
        string workerId,
        int leaseSeconds,
        decimal? progressPercent,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("[vf].[usp_HeartbeatJob]", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 30
        };
        command.Parameters.Add(new SqlParameter("@JobId", SqlDbType.UniqueIdentifier) { Value = jobId });
        command.Parameters.Add(new SqlParameter("@WorkerId", SqlDbType.NVarChar, 200) { Value = workerId });
        command.Parameters.Add(new SqlParameter("@LeaseSeconds", SqlDbType.Int) { Value = leaseSeconds });
        var progress = new SqlParameter("@ProgressPercent", SqlDbType.Decimal)
        {
            Precision = 5,
            Scale = 2,
            Value = progressPercent.HasValue ? progressPercent.Value : DBNull.Value
        };
        command.Parameters.Add(progress);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) == 1;
    }

    public async Task CompleteAsync(
        Guid jobId,
        string workerId,
        string? resultJson,
        CancellationToken cancellationToken = default)
    {
        ValidateJson(resultJson, nameof(resultJson));
        await TransitionAsync(
            jobId,
            workerId,
            JobStatuses.Completed,
            "Completed",
            null,
            null,
            resultJson,
            100,
            cancellationToken);
    }

    public Task RetryOrFailAsync(
        ClaimedJob job,
        string workerId,
        string errorCode,
        string errorMessage,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default) =>
        job.Attempt >= job.MaxAttempts
            ? FailAsync(job.JobId, workerId, errorCode, errorMessage, cancellationToken)
            : ScheduleRetryAsync(job.JobId, workerId, errorCode, errorMessage, retryDelay, cancellationToken);

    public Task FailAsync(
        Guid jobId,
        string workerId,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(
            jobId,
            workerId,
            JobStatuses.Failed,
            "Failed",
            errorCode,
            errorMessage,
            null,
            null,
            cancellationToken);

    private async Task ScheduleRetryAsync(
        Guid jobId,
        string workerId,
        string errorCode,
        string errorMessage,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var updated = await dbContext.Jobs
            .Where(x => x.JobId == jobId && x.Status == JobStatuses.Running && x.LockedBy == workerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, JobStatuses.RetryScheduled)
                .SetProperty(x => x.AvailableAtUtc, now.Add(retryDelay))
                .SetProperty(x => x.LockedBy, (string?)null)
                .SetProperty(x => x.LockedAtUtc, (DateTime?)null)
                .SetProperty(x => x.HeartbeatAtUtc, (DateTime?)null)
                .SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(x => x.ErrorCode, NormalizeOptional(errorCode, 100))
                .SetProperty(x => x.ErrorMessage, NormalizeOptional(errorMessage, 4000))
                .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);
        EnsureTransition(updated, jobId);
        dbContext.JobEvents.Add(NewEvent(jobId, "RetryScheduled", JobStatuses.Running, JobStatuses.RetryScheduled, errorMessage, now));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task TransitionAsync(
        Guid jobId,
        string workerId,
        string nextStatus,
        string eventType,
        string? errorCode,
        string? errorMessage,
        string? resultJson,
        decimal? progress,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var updated = await dbContext.Jobs
            .Where(x => x.JobId == jobId && x.Status == JobStatuses.Running && x.LockedBy == workerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, nextStatus)
                .SetProperty(x => x.CompletedAtUtc, nextStatus == JobStatuses.Completed || nextStatus == JobStatuses.Failed ? now : null)
                .SetProperty(x => x.LockedBy, (string?)null)
                .SetProperty(x => x.LockedAtUtc, (DateTime?)null)
                .SetProperty(x => x.HeartbeatAtUtc, (DateTime?)null)
                .SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(x => x.ErrorCode, NormalizeOptional(errorCode, 100))
                .SetProperty(x => x.ErrorMessage, NormalizeOptional(errorMessage, 4000))
                .SetProperty(x => x.ResultJson, resultJson)
                .SetProperty(x => x.ProgressPercent, x => progress ?? x.ProgressPercent)
                .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);
        EnsureTransition(updated, jobId);
        dbContext.JobEvents.Add(NewEvent(jobId, eventType, JobStatuses.Running, nextStatus, errorMessage, now));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static JobEvent NewEvent(
        Guid jobId,
        string eventType,
        string? from,
        string? to,
        string? message,
        DateTime occurredAtUtc) =>
        new()
        {
            JobId = jobId,
            EventType = eventType,
            FromStatus = from,
            ToStatus = to,
            Message = NormalizeOptional(message, 4000),
            OccurredAtUtc = occurredAtUtc
        };

    private static void ValidateEnqueue(EnqueueJobCommand command)
    {
        if (command.ProjectId == Guid.Empty)
        {
            throw new ArgumentException("ProjectId is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.JobType) || command.JobType.Length > 100)
        {
            throw new ArgumentException("JobType is invalid.", nameof(command));
        }

        if (command.MaxAttempts is < 1 or > 20)
        {
            throw new ArgumentException("MaxAttempts must be between 1 and 20.", nameof(command));
        }

        if (command.DependsOnJobIds?.Contains(Guid.Empty) == true)
        {
            throw new ArgumentException("Job dependency IDs must be valid.", nameof(command));
        }

        ValidateJson(command.PayloadJson, nameof(command.PayloadJson));
    }

    private static void ValidateJson(string? value, string parameterName)
    {
        if (value is null)
        {
            return;
        }

        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Value must be valid JSON.", parameterName, exception);
        }
    }

    private static void EnsureTransition(int updatedRows, Guid jobId)
    {
        if (updatedRows != 1)
        {
            throw new InvalidOperationException($"Job {jobId} is no longer owned by this worker.");
        }
    }

    private static Guid? ReadNullableGuid(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static string? ReadNullableString(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}

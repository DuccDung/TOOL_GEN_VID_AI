using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Data;
using TOOL_SERVER.Models;
using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_SERVER.Generation;

internal interface ILipSyncPollingProcessor
{
    Task ProcessDueAsync(CancellationToken cancellationToken);
}

internal sealed class LipSyncPollingWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<LipSyncPollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ILipSyncPollingProcessor>().ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Lip-sync provider polling cycle failed.");
            }
            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }
}

internal sealed class LipSyncPollingProcessor(
    VideoFactoryDbContext dbContext,
    IProviderRuntimeResolver providerResolver,
    ILipSyncProviderRouter providerRouter,
    IVideoOutputStore outputStore,
    IAiBudgetService budgetService,
    TimeProvider timeProvider,
    IOptions<VideoPollingOptions> options,
    ILogger<LipSyncPollingProcessor> logger) : ILipSyncPollingProcessor
{
    private readonly VideoPollingOptions _options = options.Value;

    public async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow();
        await MarkStaleSubmissionsUnknownAsync(now, cancellationToken);
        var ids = await dbContext.ProviderRequests.AsNoTracking()
            .Where(x => x.ProviderCode == ProviderCodes.Fal &&
                        x.RequestKind == "LipSync" &&
                        x.OrganizationId != null &&
                        x.ExternalRequestId != null &&
                        (x.Status == LipSyncStatuses.Submitted || x.Status == LipSyncStatuses.Queued || x.Status == LipSyncStatuses.Processing || x.Status == LipSyncStatuses.Unknown) &&
                        (x.NextPollAtUtc == null || x.NextPollAtUtc <= now))
            .OrderBy(x => x.NextPollAtUtc)
            .Select(x => x.ProviderRequestId)
            .Take(10)
            .ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            await ProcessOneAsync(id, cancellationToken);
        }
    }

    private async Task MarkStaleSubmissionsUnknownAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Fal only exposes a task after returning request_id. If the server dies
        // inside that submit window, retrying the POST could charge twice. Wait
        // well beyond FalRuntime's two-minute HTTP timeout, then make the
        // uncertainty explicit for manual reconciliation.
        var staleRequestIds = await dbContext.ProviderRequests
            .AsNoTracking()
            .Where(x => x.RequestKind == "LipSync" &&
                        x.Status == LipSyncStatuses.Submitting &&
                        x.ExternalRequestId == null &&
                        (x.SubmittedAtUtc ?? x.CreatedAtUtc) <= now.AddMinutes(-10))
            .OrderBy(x => x.SubmittedAtUtc ?? x.CreatedAtUtc)
            .Select(x => x.ProviderRequestId)
            .Take(10)
            .ToListAsync(cancellationToken);
        var markedCount = 0;
        foreach (var requestId in staleRequestIds)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            const string errorCode = "provider_submission_unknown";
            const string errorMessage = "Không thể xác nhận Fal đã nhận tác vụ hay chưa. Idempotency key và reservation được giữ để đối soát, không tự gửi lại.";
            var claimed = await dbContext.ProviderRequests
                .Where(x => x.ProviderRequestId == requestId &&
                            x.Status == LipSyncStatuses.Submitting &&
                            x.ExternalRequestId == null &&
                            (x.SubmittedAtUtc ?? x.CreatedAtUtc) <= now.AddMinutes(-10))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, LipSyncStatuses.Unknown)
                    .SetProperty(x => x.ErrorCode, errorCode)
                    .SetProperty(x => x.ErrorMessage, errorMessage)
                    .SetProperty(x => x.NextPollAtUtc, (DateTime?)null)
                    .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);
            if (claimed == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }
            var sceneId = await dbContext.LipSyncGenerations
                .Where(x => x.ProviderRequestId == requestId)
                .Select(x => x.SceneId)
                .SingleAsync(cancellationToken);
            await dbContext.LipSyncGenerations
                .Where(x => x.ProviderRequestId == requestId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, LipSyncStatuses.Unknown), cancellationToken);
            await dbContext.Scenes
                .Where(x => x.SceneId == sceneId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, "WaitingProvider")
                    .SetProperty(x => x.ApprovedRenderMediaAssetId, (Guid?)null)
                    .SetProperty(x => x.LastErrorCode, errorCode)
                    .SetProperty(x => x.LastErrorMessage, errorMessage)
                    .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            markedCount++;
        }
        if (markedCount > 0)
        {
            logger.LogWarning("Marked {Count} stale lip-sync submissions Unknown for manual reconciliation.", markedCount);
        }
    }

    private async Task ProcessOneAsync(Guid providerRequestId, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var claimUntil = now.AddMinutes(Math.Clamp(_options.ClaimLeaseMinutes, 5, 120));
        var claimed = await dbContext.ProviderRequests
            .Where(x => x.ProviderRequestId == providerRequestId && x.RequestKind == "LipSync" &&
                        (x.NextPollAtUtc == null || x.NextPollAtUtc <= now) &&
                        (x.Status == LipSyncStatuses.Submitted || x.Status == LipSyncStatuses.Queued || x.Status == LipSyncStatuses.Processing || x.Status == LipSyncStatuses.Unknown))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.NextPollAtUtc, claimUntil), cancellationToken);
        if (claimed == 0)
        {
            return;
        }

        var request = await dbContext.ProviderRequests.SingleAsync(x => x.ProviderRequestId == providerRequestId, cancellationToken);
        var generation = await dbContext.LipSyncGenerations.SingleAsync(x => x.ProviderRequestId == providerRequestId, cancellationToken);
        var project = await dbContext.Projects.SingleAsync(x => x.ProjectId == request.ProjectId, cancellationToken);
        var scene = await dbContext.Scenes.SingleAsync(x => x.SceneId == request.SceneId, cancellationToken);
        var providerResultReceived = false;
        var providerCompleted = false;
        var projectActualCostBeforePoll = project.ActualCost;
        try
        {
            var provider = await providerResolver.ResolveModelAsync(
                request.OrganizationId!.Value,
                request.ProviderCode,
                "LipSync",
                request.ModelCode,
                request.OrganizationProviderCredentialId,
                false,
                cancellationToken);
            var result = await providerRouter.Resolve(request.ProviderCode).GetStatusAsync(provider, request.ExternalRequestId!, cancellationToken);
            providerResultReceived = true;
            providerCompleted = result.Status == LipSyncStatuses.Completed;
            if (providerCompleted)
            {
                await outputStore.CacheAsync(
                    request.ProviderRequestId,
                    result.OutputUrl ?? throw new ProviderHttpException(request.ProviderCode, "provider_output_missing", "Provider không trả output lip-sync."),
                    cancellationToken);
            }
            request.PollCount++;
            request.LastPolledAtUtc = now;
            request.ExternalRequestId = result.ExternalRequestId;
            request.ResponseJson = result.ResponseJson;
            var status = result.Status;
            var errorCode = result.ErrorCode;
            var errorMessage = Safe(result.ErrorMessage);
            if (IsActive(status) && VideoPollingPolicy.ReachedTerminalLimit(
                    request.PollCount,
                    request.SubmittedAtUtc ?? request.CreatedAtUtc,
                    now,
                    _options))
            {
                status = LipSyncStatuses.Expired;
                errorCode = "provider_polling_exhausted";
                errorMessage = "Không thể xác nhận kết quả lip-sync trong thời hạn cho phép.";
            }
            request.Status = status;
            request.ErrorCode = errorCode;
            request.ErrorMessage = errorMessage;
            request.NextPollAtUtc = IsActive(status) ? now.AddSeconds(Math.Min(60, 10 + request.PollCount * 2)) : null;
            request.CompletedAtUtc = IsTerminal(status) ? now : null;
            request.UpdatedAtUtc = now;
            generation.Status = status;
            generation.ActualDurationMs = result.ActualDurationSeconds is { } duration ? duration * 1000L : generation.ActualDurationMs;
            generation.CompletedAtUtc = request.CompletedAtUtc;
            ApplySceneTaskStatus(scene, status, errorCode, errorMessage, now);
            if (status == LipSyncStatuses.Completed)
            {
                request.ActualCost = request.EstimatedCost;
                request.UsageJson = JsonSerializer.Serialize(new
                {
                    durationSeconds = Math.Ceiling(generation.RequestedDurationMs / 1000m),
                    outputDurationSeconds = result.ActualDurationSeconds,
                    syncMode = FalLipSyncPolicy.SyncMode,
                    usageSource = "rate_snapshot"
                });
                project.ActualCost += request.ActualCost;
                project.UpdatedAtUtc = now;
            }
            await dbContext.SaveChangesAsync(cancellationToken);

            await ReconcileReservationAsync(request, status, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (providerCompleted)
            {
                // A failed cache/database step must not leave an ActualCost
                // increment attached to a task that will be polled again.
                project.ActualCost = projectActualCostBeforePoll;
                request.ActualCost = 0;
            }
            request.PollCount++;
            request.LastPolledAtUtc = now;
            request.UpdatedAtUtc = now;
            var terminal = VideoPollingPolicy.ReachedTerminalLimit(
                request.PollCount,
                request.SubmittedAtUtc ?? request.CreatedAtUtc,
                now,
                _options);
            if (terminal)
            {
                request.Status = LipSyncStatuses.Failed;
                request.ErrorCode = providerCompleted ? "provider_output_download_failed" : "provider_polling_exhausted";
                request.ErrorMessage = providerCompleted
                    ? "Fal đã hoàn tất và đã phát sinh chi phí nhưng server không thể lưu output lip-sync trong thời hạn cho phép."
                    : "Không thể xác nhận kết quả lip-sync trong thời hạn cho phép.";
                request.NextPollAtUtc = null;
                request.CompletedAtUtc = now;
                generation.Status = LipSyncStatuses.Failed;
                generation.CompletedAtUtc = now;
                if (providerCompleted)
                {
                    request.ActualCost = request.EstimatedCost;
                    request.UsageJson = JsonSerializer.Serialize(new
                    {
                        durationSeconds = Math.Ceiling(generation.RequestedDurationMs / 1000m),
                        syncMode = FalLipSyncPolicy.SyncMode,
                        usageSource = "rate_snapshot",
                        outputUnavailable = true
                    });
                    project.ActualCost = projectActualCostBeforePoll + request.ActualCost;
                    project.UpdatedAtUtc = now;
                }
                ApplySceneTaskStatus(scene, LipSyncStatuses.Failed, request.ErrorCode, request.ErrorMessage, now);
            }
            else
            {
                if (providerResultReceived)
                {
                    request.Status = LipSyncStatuses.Unknown;
                    generation.Status = LipSyncStatuses.Unknown;
                }
                var retry = VideoPollingPolicy.RetryError(providerCompleted);
                request.ErrorCode = retry.ErrorCode;
                request.ErrorMessage = retry.ErrorMessage;
                request.NextPollAtUtc = now.AddSeconds(Math.Min(300, 15 * request.PollCount));
                request.CompletedAtUtc = null;
                generation.CompletedAtUtc = null;
                ApplySceneTaskStatus(scene, request.Status, request.ErrorCode, request.ErrorMessage, now);
            }
            await dbContext.SaveChangesAsync(CancellationToken.None);
            if (terminal && request.BudgetReservationId is { } reservationId)
            {
                if (providerCompleted)
                {
                    await ReconcileReservationAsync(request, LipSyncStatuses.Completed, CancellationToken.None);
                }
                else
                {
                    await ReleaseReservationAsync(reservationId, CancellationToken.None);
                }
            }
            logger.Log(terminal ? LogLevel.Error : LogLevel.Warning, exception, "Lip-sync polling failed for {ProviderRequestId}; terminal={Terminal}.", providerRequestId, terminal);
        }
    }

    private async Task ReconcileReservationAsync(
        ProviderRequest request,
        string status,
        CancellationToken cancellationToken)
    {
        if (request.BudgetReservationId is not { } reservationId)
        {
            return;
        }
        try
        {
            if (status == LipSyncStatuses.Completed)
            {
                await budgetService.SettleAsync(
                    reservationId,
                    request.ActualCost,
                    request.OrganizationProviderCredentialId,
                    Deserialize(request.UsageJson),
                    Deserialize(request.RateSnapshotJson),
                    cancellationToken);
            }
            else if (IsFailure(status))
            {
                await budgetService.ReleaseAsync(reservationId, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not reconcile lip-sync reservation {ReservationId} for status {Status}; the budget reconciliation worker must retry it.",
                reservationId,
                status);
        }
    }

    private async Task ReleaseReservationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        try
        {
            await budgetService.ReleaseAsync(reservationId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not release terminal lip-sync reservation {ReservationId}; reconciliation is required.", reservationId);
        }
    }

    private static object? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ApplySceneTaskStatus(
        Scene scene,
        string status,
        string? errorCode,
        string? errorMessage,
        DateTime now)
    {
        var hasActionableError = IsFailure(status) ||
                                 status == LipSyncStatuses.Unknown && !string.IsNullOrWhiteSpace(errorCode);
        scene.Status = status switch
        {
            LipSyncStatuses.Completed => "Generated",
            LipSyncStatuses.Failed or LipSyncStatuses.Cancelled or LipSyncStatuses.Expired => "PromptReady",
            _ => "WaitingProvider"
        };
        scene.ApprovedRenderMediaAssetId = null;
        scene.LastErrorCode = hasActionableError ? Safe(errorCode) : null;
        scene.LastErrorMessage = hasActionableError ? Safe(errorMessage) : null;
        scene.UpdatedAtUtc = now;
    }

    private static bool IsActive(string status) => status is LipSyncStatuses.Submitted or LipSyncStatuses.Queued or LipSyncStatuses.Processing or LipSyncStatuses.Unknown;
    private static bool IsTerminal(string status) => status is LipSyncStatuses.Completed or LipSyncStatuses.Failed or LipSyncStatuses.Cancelled or LipSyncStatuses.Expired;
    private static bool IsFailure(string status) => status is LipSyncStatuses.Failed or LipSyncStatuses.Cancelled or LipSyncStatuses.Expired;
    private static string? Safe(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= 4000 ? value : value[..4000];
    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}

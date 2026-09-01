using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Data;
using TOOL_SERVER.Models;
using TOOL_SERVER.Organizations;

namespace TOOL_SERVER.Generation;

internal sealed class VideoPollingOptions
{
    public const string SectionName = "Generation:VideoPolling";
    public int MaximumAttempts { get; set; } = 3000;
    public int MaximumAgeHours { get; set; } = 72;
    public int ClaimLeaseMinutes { get; set; } = 35;
}

internal static class VideoPollingPolicy
{
    public static bool ReachedTerminalLimit(
        int attemptedPollCount,
        DateTime submittedOrCreatedAtUtc,
        DateTime nowUtc,
        VideoPollingOptions options) =>
        attemptedPollCount >= Math.Clamp(options.MaximumAttempts, 1, 10_000) ||
        submittedOrCreatedAtUtc <= nowUtc.AddHours(-Math.Clamp(options.MaximumAgeHours, 1, 24 * 30));

    public static (string ErrorCode, string ErrorMessage) RetryError(bool providerReportedCompleted) =>
        providerReportedCompleted
            ? (
                "provider_output_download_failed",
                "Provider đã hoàn tất video nhưng server chưa thể lưu output; hệ thống sẽ thử lại.")
            : (
                "provider_status_check_failed",
                "Chưa thể kiểm tra trạng thái video từ provider; hệ thống sẽ thử lại.");
}

internal sealed class VideoPollingWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<VideoPollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IVideoPollingProcessor>()
                    .ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Video provider background polling cycle failed.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }
}

internal interface IVideoPollingProcessor
{
    Task ProcessDueAsync(CancellationToken cancellationToken);
}

internal sealed class VideoPollingProcessor(
    VideoFactoryDbContext dbContext,
    IProviderRuntimeResolver providerResolver,
    IVideoProviderRouter videoProviderRouter,
    IVideoOutputStore videoOutputStore,
    IAiBudgetService budgetService,
    IAiCostEstimator costEstimator,
    ILogger<VideoPollingProcessor> logger,
    TimeProvider timeProvider,
    IOptions<VideoPollingOptions> options) : IVideoPollingProcessor
{
    private readonly VideoPollingOptions _options = options.Value;

    public async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var dueIds = await dbContext.ProviderRequests
            .AsNoTracking()
            .Where(x => (x.ProviderCode == ProviderCodes.Kling ||
                         x.ProviderCode == ProviderCodes.BytePlus ||
                         x.ProviderCode == ProviderCodes.Fal) &&
                        x.RequestKind == "Video" &&
                        x.OrganizationId != null &&
                        x.ExternalRequestId != null &&
                        (x.Status == "Submitted" || x.Status == "Queued" || x.Status == "Processing" || x.Status == "Unknown") &&
                        (x.NextPollAtUtc == null || x.NextPollAtUtc <= now))
            .OrderBy(x => x.NextPollAtUtc)
            .Select(x => x.ProviderRequestId)
            .Take(10)
            .ToListAsync(cancellationToken);
        foreach (var providerRequestId in dueIds)
        {
            await ProcessOneAsync(providerRequestId, cancellationToken);
        }
    }

    private async Task ProcessOneAsync(Guid providerRequestId, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var claimUntil = now.AddMinutes(Math.Clamp(_options.ClaimLeaseMinutes, 5, 120));
        var claimed = await dbContext.ProviderRequests
            .Where(x => x.ProviderRequestId == providerRequestId &&
                        (x.NextPollAtUtc == null || x.NextPollAtUtc <= now) &&
                        (x.Status == "Submitted" || x.Status == "Queued" || x.Status == "Processing" || x.Status == "Unknown"))
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(x => x.NextPollAtUtc, claimUntil),
                cancellationToken);
        if (claimed == 0)
        {
            return;
        }

        var requestLog = await dbContext.ProviderRequests.SingleAsync(
            x => x.ProviderRequestId == providerRequestId,
            cancellationToken);
        var project = await dbContext.Projects.SingleAsync(
            x => x.ProjectId == requestLog.ProjectId,
            cancellationToken);
        var providerReportedCompleted = false;
        try
        {
            var provider = await providerResolver.ResolveModelAsync(
                requestLog.OrganizationId!.Value,
                requestLog.ProviderCode,
                "Video",
                requestLog.ModelCode,
                requestLog.OrganizationProviderCredentialId,
                false,
                cancellationToken);
            var result = await videoProviderRouter.Resolve(requestLog.ProviderCode).GetStatusAsync(
                provider,
                requestLog.ExternalRequestId!,
                cancellationToken);
            providerReportedCompleted = result.Status == "Completed";
            if (providerReportedCompleted)
            {
                if (string.IsNullOrWhiteSpace(result.OutputUrl))
                {
                    throw new ProviderHttpException(
                        requestLog.ProviderCode,
                        "provider_output_missing",
                        "Provider báo hoàn tất nhưng không trả về URL video.");
                }
                await videoOutputStore.CacheAsync(
                    requestLog.ProviderRequestId,
                    result.OutputUrl,
                    cancellationToken);
            }
            requestLog.PollCount++;
            requestLog.LastPolledAtUtc = now;
            requestLog.ExternalRequestId = result.ExternalRequestId;
            requestLog.ResponseJson = result.ResponseJson;
            var effectiveStatus = result.Status;
            var effectiveErrorCode = result.ErrorCode;
            var effectiveErrorMessage = Safe(result.ErrorMessage);
            if (IsActiveStatus(effectiveStatus) &&
                VideoPollingPolicy.ReachedTerminalLimit(
                    requestLog.PollCount,
                    requestLog.SubmittedAtUtc ?? requestLog.CreatedAtUtc,
                    now,
                    _options))
            {
                effectiveStatus = "Expired";
                effectiveErrorCode = "provider_polling_exhausted";
                effectiveErrorMessage = "Không thể xác nhận kết quả video từ provider trong thời hạn cho phép.";
            }
            requestLog.Status = effectiveStatus;
            requestLog.ErrorCode = effectiveErrorCode;
            requestLog.ErrorMessage = effectiveErrorMessage;
            requestLog.NextPollAtUtc = IsActiveStatus(effectiveStatus)
                ? now.AddSeconds(Math.Min(60, 10 + requestLog.PollCount * 2))
                : null;
            requestLog.CompletedAtUtc = IsTerminalStatus(effectiveStatus) ? now : null;
            requestLog.UpdatedAtUtc = now;
            if (effectiveStatus == "Completed")
            {
                requestLog.OutputTokens = result.CompletionTokens;
                requestLog.ActualCost = await costEstimator.CalculateVideoActualAsync(
                    requestLog.ProviderCode,
                    requestLog.RateSnapshotJson ?? "[]",
                    requestLog.EstimatedCost,
                    result.ReportedBillingAmount,
                    result.CompletionTokens,
                    cancellationToken);
                requestLog.UsageJson = MergeVideoUsage(
                    requestLog.UsageJson,
                    result.CompletionTokens,
                    result.ActualDurationSeconds);
                project.ActualCost += requestLog.ActualCost;
                project.UpdatedAtUtc = now;
            }
            await dbContext.SaveChangesAsync(cancellationToken);

            if (IsTerminalStatus(effectiveStatus))
            {
                logger.LogInformation(
                    "Video task {ProviderRequestId} reached terminal status {Status} for provider {ProviderCode} after {PollCount} polls; actual cost {ActualCost} {CurrencyCode}.",
                    providerRequestId,
                    effectiveStatus,
                    requestLog.ProviderCode,
                    requestLog.PollCount,
                    requestLog.ActualCost,
                    requestLog.CurrencyCode);
            }
            else
            {
                logger.LogDebug(
                    "Video task {ProviderRequestId} remains {Status} for provider {ProviderCode} after {PollCount} polls.",
                    providerRequestId,
                    effectiveStatus,
                    requestLog.ProviderCode,
                    requestLog.PollCount);
            }

            if (requestLog.BudgetReservationId is { } reservationId)
            {
                if (effectiveStatus == "Completed")
                {
                    await budgetService.SettleAsync(
                        reservationId,
                        requestLog.ActualCost,
                        requestLog.OrganizationProviderCredentialId,
                        DeserializeJson(requestLog.UsageJson),
                        DeserializeRateSnapshot(requestLog.RateSnapshotJson),
                        cancellationToken);
                }
                else if (effectiveStatus is "Failed" or "Cancelled" or "Expired")
                {
                    await budgetService.ReleaseAsync(reservationId, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            requestLog.PollCount++;
            requestLog.LastPolledAtUtc = now;
            requestLog.UpdatedAtUtc = now;
            var retryError = VideoPollingPolicy.RetryError(providerReportedCompleted);
            var submittedOrCreatedAt = requestLog.SubmittedAtUtc ?? requestLog.CreatedAtUtc;
            var terminal = VideoPollingPolicy.ReachedTerminalLimit(
                requestLog.PollCount,
                submittedOrCreatedAt,
                now,
                _options);
            if (terminal)
            {
                requestLog.Status = "Failed";
                requestLog.ErrorCode = "provider_polling_exhausted";
                requestLog.ErrorMessage = "Không thể xác nhận kết quả video từ provider trong thời hạn cho phép.";
                requestLog.NextPollAtUtc = null;
                requestLog.CompletedAtUtc = now;
            }
            else
            {
                if (providerReportedCompleted)
                {
                    requestLog.Status = "Processing";
                }
                requestLog.ErrorCode = retryError.ErrorCode;
                requestLog.ErrorMessage = retryError.ErrorMessage;
                requestLog.NextPollAtUtc = now.AddSeconds(Math.Min(300, 15 * requestLog.PollCount));
                requestLog.CompletedAtUtc = null;
            }
            await dbContext.SaveChangesAsync(CancellationToken.None);
            if (terminal)
            {
                logger.LogError(
                    exception,
                    "Video task {ProviderRequestId} polling reached its terminal retry limit after {PollCount} attempts.",
                    providerRequestId,
                    requestLog.PollCount);
            }
            else
            {
                logger.LogWarning(exception, "Video task {ProviderRequestId} polling failed and will be retried.", providerRequestId);
            }
        }
    }

    private static object? DeserializeRateSnapshot(string? value)
        => DeserializeJson(value);

    private static object? DeserializeJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(value);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string MergeVideoUsage(
        string? existing,
        long? completionTokens,
        int? actualDurationSeconds)
    {
        decimal? requestedDuration = null;
        string? resolution = null;
        bool? nativeAudio = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(existing))
            {
                using var document = System.Text.Json.JsonDocument.Parse(existing);
                var root = document.RootElement;
                requestedDuration = root.TryGetProperty("durationSeconds", out var duration) &&
                                    duration.TryGetDecimal(out var parsedDuration)
                    ? parsedDuration
                    : null;
                resolution = root.TryGetProperty("resolution", out var resolutionValue)
                    ? resolutionValue.GetString()
                    : null;
                nativeAudio = root.TryGetProperty("nativeAudio", out var audioValue) &&
                              audioValue.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
                    ? audioValue.GetBoolean()
                    : null;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Historical usage metadata is optional; preserve settlement with
            // the provider's authoritative completion token count.
        }
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            durationSeconds = actualDurationSeconds is { } actualDuration
                ? (decimal?)actualDuration
                : requestedDuration,
            requestedDurationSeconds = requestedDuration,
            resolution,
            nativeAudio,
            outputTokens = completionTokens,
            completionTokens
        });
    }

    private static string? Safe(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= 4000 ? value : value[..4000];

    private static bool IsActiveStatus(string status) =>
        status is "Submitted" or "Queued" or "Processing" or "Unknown";

    private static bool IsTerminalStatus(string status) =>
        status is "Completed" or "Failed" or "Cancelled" or "Expired";

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}

using System.Text.Json;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Vietsub.Api;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_LOCAL.Vietsub.Translation;

internal sealed class VietsubCloudTranslationJobExecutor(VietsubProjectStore projects, VietsubSubtitleStore subtitles,
    VietsubCloudTranslationResults results, VietsubAppPaths paths, IVietsubLocalJobAuthorizer authorizer,
    IVietsubCloudTranslationClient client, VietsubJobStore jobs) : IVietsubJobExecutor
{
    public string JobType => VietsubJobTypes.TranslateCloud;

    public async Task ExecuteAsync(VietsubJobExecutionContext context, CancellationToken ct)
    {
        try { await ExecuteCoreAsync(context, ct); }
        catch (AccountClientException e) { throw Failure(e.Code, e.Message, e.StatusCode != 410); }
        catch (HttpRequestException) { throw Failure("CLOUD_NETWORK", "Mất kết nối. Bạn có thể tiếp tục để nhận kết quả của tác vụ hiện tại."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw Failure("CLOUD_NETWORK", "Kết nối hết thời gian chờ. Hãy tiếp tục để nhận kết quả của tác vụ hiện tại."); }
        catch (VietsubTranslationException e) { throw Failure(e.Code, e.Message, e.Retryable); }
        catch (VietsubLocalJobAuthorizationException e) { throw Failure(e.Code, e.Message); }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        { throw Failure("CLOUD_SNAPSHOT_INVALID", "Dữ liệu tác vụ Cloud không hợp lệ. Không gửi thêm yêu cầu dịch.", false); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        { throw Failure("CLOUD_LOCAL_STORAGE", "Chưa lưu được kết quả Cloud vào dự án. Hãy kiểm tra dung lượng ổ đĩa và tiếp tục tác vụ."); }
    }

    private async Task ExecuteCoreAsync(VietsubJobExecutionContext context, CancellationToken ct)
    {
        var job = context.Job;
        var p = JsonSerializer.Deserialize<VietsubCloudJobParameters>(job.ParametersJson, VietsubCloudSnapshot.JsonOptions)
            ?? throw new InvalidDataException();
        var snapshot = await results.LoadSnapshotAsync(job, p, ct);
        var request = snapshot.Request;
        var project = await projects.LoadForBackgroundJobAsync(job.ProjectId, ct);
        await authorizer.AuthorizeAsync(p.UserId, p.OrganizationId, project, ct);
        var target = request.Cues.Where(x => x.IsTarget).ToDictionary(x => x.CueId);
        // Lookup by the immutable operation id before every possible POST, including after a lost response.
        var remote = await client.FindAsync(job.ProjectId, p.OrganizationId, p.OperationId, ct);
        if (remote is null)
        {
            var track = await results.CurrentTrackAsync(job.ProjectId, p.TrackId, ct);
            if (track.Revision != p.TrackRevision) throw Failure("CLOUD_TRACK_CHANGED", "Phụ đề đã thay đổi trước khi gửi dịch Cloud.", false);
            remote = await client.StartAsync(job.ProjectId, request, ct);
        }
        VietsubCloudTranslationResults.CheckRemote(remote, p);
        await context.SaveCheckpointAsync(JsonSerializer.Serialize(new { remoteJobId = remote.JobId, p.OperationId, p.SnapshotHash }, VietsubCloudSnapshot.JsonOptions), ct);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            remote = await client.GetAsync(job.ProjectId, p.OrganizationId, remote.JobId, ct);
            VietsubCloudTranslationResults.CheckRemote(remote, p);
            var items = await results.ApplyAsync(job, p, snapshot, remote, recovering: false, ct);
            var completed = items.Count(x => VietsubCloudTranslationResults.Applied(x.Status));
            var stale = items.Count(x => x.Status == VietsubTranslationJobItemStatuses.Stale);
            var checkpointJson = JsonSerializer.Serialize(new { remoteJobId = remote.JobId, p.OperationId, p.SnapshotHash,
                totalItems = target.Count, completedItems = completed, reviewItems = items.Count(x => x.Status == "REVIEW"),
                staleItems = stale, failedItems = remote.FailedCues }, VietsubCloudSnapshot.JsonOptions);
            await context.ReportProgressAsync(new("CLOUD_TRANSLATE", completed * 100d / target.Count,
                Math.Max(job.ProgressPercent, Math.Min(95, completed * 90d / target.Count + 5)), $"Đã lưu {completed}/{target.Count} câu dịch Cloud.", checkpointJson), ct);
            if (VietsubCloudStates.IsTerminal(remote.Status) || remote.Status is VietsubCloudStates.Unknown or VietsubCloudStates.Blocked or VietsubCloudStates.Paused)
            {
                if (completed > 0)
                    await VietsubTranslatedArtifactWriter.WriteAsync(paths, subtitles, job.ProjectId,
                        await results.CurrentTrackAsync(job.ProjectId, p.TrackId, ct), ct);
                if (remote.Status == VietsubCloudStates.Completed && completed == target.Count)
                {
                    await jobs.BindOutputTrackAsync(job.ProjectId, job.Id, p.TrackId, ct);
                    await client.ControlAsync(job.ProjectId, p.OrganizationId, remote.JobId, "ack", ct);
                    await context.ReportProgressAsync(new("CLOUD_APPLY", 100, 100, "Đã hoàn thành dịch Cloud.", checkpointJson), ct);
                    return;
                }
                if (stale > 0 && remote.Status == VietsubCloudStates.Completed)
                    throw Failure("CLOUD_CUE_CHANGED", $"Đã áp dụng {completed} câu; giữ nguyên {stale} câu đã được chỉnh sửa trong lúc dịch.", false);
                throw Failure(remote.ErrorCode ?? "CLOUD_" + remote.Status, remote.Message ?? "Tác vụ Cloud chưa hoàn thành. Các câu đã nhận được giữ lại.",
                    remote.Status is not (VietsubCloudStates.Unknown or VietsubCloudStates.Cancelled) && (remote.Status != VietsubCloudStates.Failed || remote.CanRetry));
            }
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    private static VietsubJobExecutionException Failure(string code, string message, bool retryable = true) => new(code, message, retryable);
}

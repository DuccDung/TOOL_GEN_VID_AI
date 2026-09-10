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
    VietsubTranslationStore translations, VietsubAppPaths paths, IVietsubLocalJobAuthorizer authorizer,
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
        var path = VietsubCloudTranslationService.SnapshotPath(paths, job.ProjectId, p.OperationId);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException();
        var snapshot = await JsonSerializer.DeserializeAsync<VietsubCloudLocalSnapshot>(stream, VietsubCloudSnapshot.JsonOptions, ct)
            ?? throw new InvalidDataException();
        var request = snapshot.Request;
        VietsubCloudSnapshot.Validate(request);
        if (VietsubCloudSnapshot.Hash(request) != p.SnapshotHash || request.OrganizationId != p.OrganizationId
            || request.TrackId != p.TrackId || request.ClientOperationId != p.OperationId || request.TrackRevision != p.TrackRevision
            || request.Cues.Any(x => !snapshot.UpdatedAt.ContainsKey(x.CueId))) throw new InvalidDataException();
        var project = await projects.LoadForBackgroundJobAsync(job.ProjectId, ct);
        await authorizer.AuthorizeAsync(p.UserId, p.OrganizationId, project, ct);
        var target = request.Cues.Where(x => x.IsTarget).ToDictionary(x => x.CueId);
        var items = await translations.LoadJobItemsAsync(job.ProjectId, job.Id, ct);
        if (items.Count == 0)
            await translations.UpsertJobItemsAsync(job.ProjectId, job.Id,
                target.Values.Select(x => new VietsubTranslationJobItemSeed(x.CueId, x.CueIndex + 1, 1, x.InputFingerprint)).ToArray(), ct);

        // Lookup by the immutable operation id before every possible POST, including after a lost response.
        var remote = await client.FindAsync(job.ProjectId, p.OrganizationId, p.OperationId, ct);
        if (remote is null)
        {
            var track = await CurrentTrackAsync(job.ProjectId, p.TrackId, ct);
            if (track.Revision != p.TrackRevision) throw Failure("CLOUD_TRACK_CHANGED", "Phụ đề đã thay đổi trước khi gửi dịch Cloud.", false);
            remote = await client.StartAsync(job.ProjectId, request, ct);
        }
        CheckRemote(remote, p);
        await context.SaveCheckpointAsync(JsonSerializer.Serialize(new { remoteJobId = remote.JobId, p.OperationId, p.SnapshotHash }, VietsubCloudSnapshot.JsonOptions), ct);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            remote = await client.GetAsync(job.ProjectId, p.OrganizationId, remote.JobId, ct);
            CheckRemote(remote, p);
            items = await translations.LoadJobItemsAsync(job.ProjectId, job.Id, ct);
            // Re-read pages from zero: transactional cue receipts make a lost cursor harmless.
            if (items.Any(x => !Applied(x.Status)))
            {
                var cursor = 0;
                while (true)
                {
                    var page = await client.ResultsAsync(job.ProjectId, p.OrganizationId, remote.JobId, cursor, ct);
                    if (page.JobId != remote.JobId || page.SnapshotHash != p.SnapshotHash || page.NextCursor < cursor)
                        throw new InvalidDataException();
                    var receipts = (await translations.LoadJobItemsAsync(job.ProjectId, job.Id, ct)).ToDictionary(x => x.CueId);
                    var currentCues = page.Items.Any(x => receipts.TryGetValue(x.CueId, out var item) && !Applied(item.Status) && item.Status != "STALE")
                        ? (await CurrentTrackAsync(job.ProjectId, p.TrackId, ct)).Cues.ToDictionary(x => x.CueId) : null;
                    foreach (var result in page.Items)
                    {
                        if (!target.TryGetValue(result.CueId, out var original) || result.InputFingerprint != original.InputFingerprint
                            || string.IsNullOrWhiteSpace(result.TranslatedText) || result.TranslatedText.Length > 8000)
                            throw new InvalidDataException();
                        var item = receipts[result.CueId];
                        if (Applied(item.Status) || item.Status == VietsubTranslationJobItemStatuses.Stale) continue;
                        var current = currentCues?.GetValueOrDefault(result.CueId);
                        var checkpoint = JsonSerializer.Serialize(new { remoteJobId = remote.JobId, p.OperationId, p.SnapshotHash }, VietsubCloudSnapshot.JsonOptions);
                        var valid = current is not null && VietsubCloudTranslationService.Fingerprint(p.TrackId, current) == original.InputFingerprint;
                        var committed = valid && await translations.TryCommitCueResultAsync(job.ProjectId, job.Id,
                            new VietsubTranslationCueCommit(original.CueId, snapshot.UpdatedAt[original.CueId], original.OriginalText,
                                original.StartMilliseconds, original.EndMilliseconds, original.Speaker, original.InputFingerprint,
                                result.TranslatedText, result.Warnings.Count > 0 ? VietsubTranslationQualityStatuses.Review : VietsubTranslationQualityStatuses.Valid,
                                null, result.Warnings, "CLOUD", "subtitle-v1", VietsubTranslationSources.CloudAuto), checkpoint, ct);
                        if (!committed)
                            await translations.UpdateJobItemAsync(job.ProjectId, job.Id, original.CueId, original.InputFingerprint,
                                VietsubTranslationJobItemStatuses.Stale, checkpoint, errorCode: "CLOUD_CUE_CHANGED", cancellationToken: ct);
                    }
                    if (!page.HasMore) break;
                    if (page.NextCursor <= cursor) throw new InvalidDataException();
                    cursor = page.NextCursor;
                }
            }
            items = await translations.LoadJobItemsAsync(job.ProjectId, job.Id, ct);
            var completed = items.Count(x => Applied(x.Status));
            var stale = items.Count(x => x.Status == VietsubTranslationJobItemStatuses.Stale);
            var checkpointJson = JsonSerializer.Serialize(new { remoteJobId = remote.JobId, p.OperationId, p.SnapshotHash,
                totalItems = target.Count, completedItems = completed, reviewItems = items.Count(x => x.Status == "REVIEW"),
                staleItems = stale, failedItems = remote.FailedCues }, VietsubCloudSnapshot.JsonOptions);
            await context.ReportProgressAsync(new("CLOUD_TRANSLATE", remote.CompletedCues * 100d / target.Count,
                Math.Min(95, remote.CompletedCues * 90d / target.Count + 5), $"Đang dịch Cloud: {remote.CompletedCues}/{target.Count} câu.", checkpointJson), ct);
            if (VietsubCloudStates.IsTerminal(remote.Status) || remote.Status is VietsubCloudStates.Unknown or VietsubCloudStates.Blocked or VietsubCloudStates.Paused)
            {
                if (completed > 0)
                    await VietsubTranslatedArtifactWriter.WriteAsync(paths, subtitles, job.ProjectId,
                        await CurrentTrackAsync(job.ProjectId, p.TrackId, ct), ct);
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

    private async Task<VietsubSubtitleTrack> CurrentTrackAsync(Guid projectId, Guid trackId, CancellationToken ct)
    {
        var project = await projects.LoadForBackgroundJobAsync(projectId, ct);
        var track = (await subtitles.LoadTracksAsync(projectId, ct)).SingleOrDefault(x => x.TrackId == trackId);
        return project.ActiveSubtitleTrackId == trackId && track?.Source == "PADDLE_OCR_LOCAL" ? track
            : throw Failure("CLOUD_TRACK_CHANGED", "Track đang biên tập đã thay đổi. Kết quả Cloud chưa được ghi vào phụ đề.", false);
    }
    private static bool Applied(string status) => status is VietsubTranslationJobItemStatuses.Completed or VietsubTranslationJobItemStatuses.Review;
    private static void CheckRemote(VietsubCloudJobResponse remote, VietsubCloudJobParameters p)
    { if (remote.SnapshotHash != p.SnapshotHash || remote.TrackId != p.TrackId || remote.ClientOperationId != p.OperationId) throw new InvalidDataException(); }
    private static VietsubJobExecutionException Failure(string code, string message, bool retryable = true) => new(code, message, retryable);
}

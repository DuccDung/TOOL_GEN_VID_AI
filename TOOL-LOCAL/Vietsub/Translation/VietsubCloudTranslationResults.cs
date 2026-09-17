using System.Text.Json;
using TOOL_LOCAL.Vietsub.Api;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_LOCAL.Vietsub.Translation;

// Shared by polling and recovery before a new operation. Cue writes always use
// the original snapshot timestamp/fingerprint and transactional local receipts.
internal sealed class VietsubCloudTranslationResults(VietsubProjectStore projects, VietsubSubtitleStore subtitles,
    VietsubTranslationStore translations, VietsubAppPaths paths, IVietsubCloudTranslationClient client)
{
    public async Task<VietsubCloudLocalSnapshot> LoadSnapshotAsync(VietsubLocalJob job, VietsubCloudJobParameters p, CancellationToken ct)
    {
        var path = VietsubCloudTranslationService.SnapshotPath(paths, job.ProjectId, p.OperationId);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException();
        var snapshot = await JsonSerializer.DeserializeAsync<VietsubCloudLocalSnapshot>(stream, VietsubCloudSnapshot.JsonOptions, ct)
            ?? throw new InvalidDataException();
        var request = snapshot.Request;
        VietsubCloudSnapshot.Validate(request);
        if (VietsubCloudSnapshot.Hash(request) != p.SnapshotHash || request.OrganizationId != p.OrganizationId
            || request.TrackId != p.TrackId || request.ClientOperationId != p.OperationId || request.TrackRevision != p.TrackRevision
            || job.InputTrackId != p.TrackId || request.Cues.Any(x => !snapshot.UpdatedAt.ContainsKey(x.CueId)))
            throw new InvalidDataException();
        return snapshot;
    }

    public async Task<IReadOnlyList<VietsubTranslationJobItem>> ApplyAsync(VietsubLocalJob job,
        VietsubCloudJobParameters p, VietsubCloudLocalSnapshot snapshot, VietsubCloudJobResponse remote,
        bool recovering, CancellationToken ct)
    {
        CheckRemote(remote, p);
        var target = snapshot.Request.Cues.Where(x => x.IsTarget).ToDictionary(x => x.CueId);
        var items = await translations.LoadJobItemsAsync(job.ProjectId, job.Id, ct);
        if (items.Count == 0)
        {
            await translations.UpsertJobItemsAsync(job.ProjectId, job.Id,
                target.Values.Select(x => new VietsubTranslationJobItemSeed(x.CueId, x.CueIndex + 1, 1, x.InputFingerprint)).ToArray(), ct);
            items = await translations.LoadJobItemsAsync(job.ProjectId, job.Id, ct);
        }
        if (items.Count != target.Count) throw new InvalidDataException();
        if (items.All(x => Applied(x.Status) || x.Status == VietsubTranslationJobItemStatuses.Stale)) return items;

        // Re-read pages from zero: transactional cue receipts make a lost cursor harmless.
        var cursor = 0;
        while (true)
        {
            var page = await client.ResultsAsync(job.ProjectId, p.OrganizationId, remote.JobId, cursor, ct);
            if (page.JobId != remote.JobId || page.SnapshotHash != p.SnapshotHash || page.NextCursor < cursor)
                throw new InvalidDataException();
            var receipts = (await translations.LoadJobItemsAsync(job.ProjectId, job.Id, ct)).ToDictionary(x => x.CueId);
            var currentCues = page.Items.Any(x => receipts.TryGetValue(x.CueId, out var item) && !Applied(item.Status)
                && item.Status != VietsubTranslationJobItemStatuses.Stale)
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
                var commit = new VietsubTranslationCueCommit(original.CueId, snapshot.UpdatedAt[original.CueId], original.OriginalText,
                    original.StartMilliseconds, original.EndMilliseconds, original.Speaker, original.InputFingerprint,
                    result.TranslatedText, result.Warnings.Count > 0 ? VietsubTranslationQualityStatuses.Review : VietsubTranslationQualityStatuses.Valid,
                    null, result.Warnings, "CLOUD", "subtitle-v1", VietsubTranslationSources.CloudAuto);
                var committed = valid && (recovering
                    ? await translations.TryRecoverCloudCueResultAsync(job.ProjectId, job.Id, commit, checkpoint, ct)
                    : await translations.TryCommitCueResultAsync(job.ProjectId, job.Id, commit, checkpoint, ct));
                if (!committed && !recovering)
                    await translations.UpdateJobItemAsync(job.ProjectId, job.Id, original.CueId, original.InputFingerprint,
                        VietsubTranslationJobItemStatuses.Stale, checkpoint, errorCode: "CLOUD_CUE_CHANGED", cancellationToken: ct);
            }
            if (!page.HasMore) break;
            if (page.NextCursor <= cursor) throw new InvalidDataException();
            cursor = page.NextCursor;
        }
        return await translations.LoadJobItemsAsync(job.ProjectId, job.Id, ct);
    }

    public async Task<VietsubSubtitleTrack> CurrentTrackAsync(Guid projectId, Guid trackId, CancellationToken ct)
    {
        var project = await projects.LoadForBackgroundJobAsync(projectId, ct);
        var track = (await subtitles.LoadTracksAsync(projectId, ct)).SingleOrDefault(x => x.TrackId == trackId);
        return project.ActiveSubtitleTrackId == trackId && track?.Source == "PADDLE_OCR_LOCAL" ? track
            : throw new VietsubTranslationException("CLOUD_TRACK_CHANGED", "Track đang biên tập đã thay đổi. Kết quả Cloud chưa được ghi vào phụ đề.");
    }

    internal static bool Applied(string status) => status is VietsubTranslationJobItemStatuses.Completed or VietsubTranslationJobItemStatuses.Review;
    internal static void CheckRemote(VietsubCloudJobResponse remote, VietsubCloudJobParameters p)
    {
        if (remote.SnapshotHash != p.SnapshotHash || remote.TrackId != p.TrackId || remote.ClientOperationId != p.OperationId)
            throw new InvalidDataException();
    }
}

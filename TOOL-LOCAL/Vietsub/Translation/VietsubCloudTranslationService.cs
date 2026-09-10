using System.Text.Json;
using TOOL_LOCAL.Vietsub.Api;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_LOCAL.Vietsub.Translation;

internal sealed record VietsubCloudJobParameters(Guid OrganizationId, string UserId, Guid OperationId,
    Guid TrackId, int TrackRevision, string SnapshotHash);
internal sealed record VietsubCloudLocalSnapshot(VietsubCloudStartRequest Request, IReadOnlyDictionary<Guid, DateTime> UpdatedAt);

internal sealed class VietsubCloudTranslationService(IVietsubLocalJobAuthorizer authorizer,
    IVietsubCloudTranslationClient client, VietsubSubtitleStore subtitles, VietsubAppPaths paths,
    VietsubJobStore jobs, VietsubJobManager manager)
{
    public Task<VietsubCloudAvailability> AvailabilityAsync(VietsubProjectSession session, CancellationToken ct) =>
        client.AvailabilityAsync(session.Manifest.ProjectId, session.Manifest.OrganizationId, ct);

    public async Task<VietsubJobSummary?> StartAsync(VietsubProjectSession session, string userId, Guid org,
        VietsubStartTranslationInput input, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(userId, org, session.Manifest, ct);
        var project = session.Manifest;
        var track = (await subtitles.LoadTracksAsync(project.ProjectId, ct)).SingleOrDefault(x => x.TrackId == input.ExpectedTrackId);
        if (track is null || project.ActiveSubtitleTrackId != track.TrackId || track.Source != "PADDLE_OCR_LOCAL"
            || track.Cues.Count == 0 || track.LanguageCode is not ("en" or "zh"))
            throw new VietsubTranslationException("TRANSLATION_SOURCE_TRACK_REQUIRED", "Bạn cần quét OCR nhận dạng phụ đề trước khi dịch.");
        if (track.Revision != input.ExpectedTrackRevision)
            throw new VietsubTranslationException("TRANSLATION_TRACK_CHANGED", "Phụ đề đã thay đổi. Hãy tải lại trước khi dịch.");
        var operation = Guid.NewGuid();
        var cues = track.Cues.Select((cue, index) => new VietsubCloudCue(cue.CueId, index, cue.StartMilliseconds,
            cue.EndMilliseconds, cue.Speaker, cue.OriginalText, IsTarget(track.TrackId, cue), Fingerprint(track.TrackId, cue),
            VietsubTranslationResultValidator.IsApprovedContext(cue) ? cue.TranslatedText : null)).ToArray();
        if (!cues.Any(x => x.IsTarget)) return null;
        var request = new VietsubCloudStartRequest(org, operation, track.TrackId, track.Revision, track.LanguageCode, "vi", cues,
            BuildContext(project.TranslationSettings));
        VietsubCloudSnapshot.Validate(request);
        // Resolve an existing remote job before allowing another operation for this project.
        if (await client.FindAsync(project.ProjectId, org, Guid.Empty, ct) is not null)
            throw new VietsubTranslationException("CLOUD_JOB_ACTIVE", "Dự án đang có tác vụ Dịch Cloud. Hãy tiếp tục tác vụ hiện tại.");
        var available = await client.AvailabilityAsync(project.ProjectId, org, ct);
        if (!available.Available) throw new VietsubTranslationException(available.ErrorCode ?? "CLOUD_UNAVAILABLE", available.Message ?? "Dịch Cloud chưa sẵn sàng.");
        var snapshot = new VietsubCloudLocalSnapshot(request, track.Cues.ToDictionary(x => x.CueId, x => x.UpdatedAtUtc));
        await SaveSnapshotAsync(paths, project.ProjectId, operation, snapshot, ct);
        var parameters = new VietsubCloudJobParameters(org, userId, operation, track.TrackId, track.Revision, VietsubCloudSnapshot.Hash(request));
        return await manager.EnqueueAsync(project.ProjectId, VietsubJobTypes.TranslateCloud,
            ["CLOUD_PREPARE", "CLOUD_TRANSLATE", "CLOUD_APPLY"], JsonSerializer.Serialize(parameters, VietsubCloudSnapshot.JsonOptions),
            track.TrackId, track.Revision, maxAttempts: 10, cancellationToken: ct);
    }

    internal static bool IsTarget(Guid trackId, VietsubSubtitleCue cue) => !cue.OriginalLocked && !cue.TranslationLocked
        && cue.TranslationSource != VietsubTranslationSources.Manual && !string.IsNullOrWhiteSpace(cue.OriginalText)
        && (string.IsNullOrWhiteSpace(cue.TranslatedText) || cue.QualityStatus is "INVALID" or "STALE"
            || (cue.TranslationSource == VietsubTranslationSources.CloudAuto && cue.TranslationSourceFingerprint != Fingerprint(trackId, cue)));
    internal static string Fingerprint(Guid trackId, VietsubSubtitleCue cue) => VietsubCloudSnapshot.CueFingerprint(
        trackId, cue.CueId, cue.OriginalText, cue.Speaker, cue.StartMilliseconds, cue.EndMilliseconds);
    private static string BuildContext(VietsubTranslationSettings settings) =>
        JsonSerializer.Serialize(new { settings.ContextSummary, settings.CharacterInstructions, settings.StyleInstructions,
            glossary = settings.Glossary.Select(x => new { x.SourceText, x.TargetText }) }, VietsubCloudSnapshot.JsonOptions);

    internal static string SnapshotPath(VietsubAppPaths paths, Guid project, Guid operation) =>
        paths.GetProjectPath(project, "jobs", $"cloud-{operation:N}.json");
    private static async Task SaveSnapshotAsync(VietsubAppPaths paths, Guid project, Guid operation,
        VietsubCloudLocalSnapshot snapshot, CancellationToken ct)
    {
        var path = SnapshotPath(paths, project, operation); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".part";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            { await JsonSerializer.SerializeAsync(stream, snapshot, VietsubCloudSnapshot.JsonOptions, ct); await stream.FlushAsync(ct); stream.Flush(true); }
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task ControlRemoteAsync(Guid projectId, Guid jobId, string action, CancellationToken ct)
    {
        var local = await jobs.GetAsync(projectId, jobId, ct);
        if (local?.Type != VietsubJobTypes.TranslateCloud) return;
        var p = JsonSerializer.Deserialize<VietsubCloudJobParameters>(local.ParametersJson, VietsubCloudSnapshot.JsonOptions)!;
        var remote = await client.FindAsync(projectId, p.OrganizationId, p.OperationId, ct);
        if (remote is null) return;
        var command = action switch
        {
            "PAUSE" when remote.Status is VietsubCloudStates.Queued or VietsubCloudStates.Running => "pause",
            "CANCEL" when !VietsubCloudStates.IsTerminal(remote.Status) => "cancel",
            "RESUME" or "RETRY" when remote.Status is VietsubCloudStates.Blocked or VietsubCloudStates.Paused => "resume",
            "RETRY" when remote.Status == VietsubCloudStates.Failed => "retry",
            _ => null
        };
        if (command != null) await client.ControlAsync(projectId, p.OrganizationId, remote.JobId, command, ct);
    }
}

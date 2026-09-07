using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_LOCAL.Vietsub.Voice;

internal sealed class VietsubVoiceService(
    IVietsubLocalJobAuthorizer authorizer,
    VietsubSubtitleStore subtitleStore,
    VietsubVoiceStore voiceStore,
    VietsubAppPaths paths,
    VietsubVoiceComponentStore components,
    VietsubVoicePlaybackRegistry playbackRegistry,
    VietsubJobManager jobManager)
{
    public VietsubVoicePlaybackRegistry PlaybackRegistry => playbackRegistry;

    public VietsubVoiceRuntimeStatus GetRuntimeStatus() => components.GetStatus();

    public async Task<VietsubVoiceRuntimeStatus> InstallRuntimeAsync(
        VietsubProjectSession session,
        string userId,
        Guid organizationId,
        IProgress<VietsubVoiceRuntimeInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        await AuthorizeAsync(session.Manifest, userId, organizationId, cancellationToken);
        return await components.InstallAsync(progress, cancellationToken);
    }

    public async Task<VietsubVoiceWorkspaceSummary> GetWorkspaceAsync(
        VietsubProjectManifest project,
        CancellationToken cancellationToken)
    {
        var trackRevision = project.ActiveSubtitleTrackId is Guid trackId
            ? (await subtitleStore.LoadTracksAsync(project.ProjectId, cancellationToken))
                .SingleOrDefault(track => track.TrackId == trackId)?.Revision
            : null;
        var workspace = await voiceStore.LoadWorkspaceAsync(
            project.ProjectId,
            project.ActiveSubtitleTrackId,
            trackRevision,
            project.VoiceSettings,
            cancellationToken);
        playbackRegistry.ClearProject(project.ProjectId);
        if (workspace.Timeline is not { } timeline)
        {
            return workspace;
        }

        var absolutePath = paths.GetProjectPath(
            project.ProjectId,
            timeline.RelativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries));
        playbackRegistry.RegisterCurrent(new(
            project.ProjectId,
            timeline.ArtifactId,
            timeline.TrackId,
            timeline.TrackRevision,
            absolutePath,
            timeline.SizeBytes,
            timeline.Sha256));
        return workspace with
        {
            TimelinePlaybackUrl = VietsubVoicePlaybackRegistry.CreateUrl(
                project.ProjectId,
                timeline.ArtifactId,
                timeline.Sha256)
        };
    }

    public async Task<VietsubJobSummary> StartAsync(
        VietsubProjectSession session,
        string userId,
        Guid organizationId,
        VietsubStartVoiceInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var project = session.Manifest;
        await AuthorizeAsync(project, userId, organizationId, cancellationToken);
        var runtime = components.GetStatus();
        if (!runtime.Ready)
        {
            throw new VietsubVoiceException(runtime.ErrorCode ?? VietsubVoiceErrorCodes.RuntimeNotInstalled, runtime.Message);
        }
        if (input.ExpectedTrackId == Guid.Empty || input.ExpectedTrackRevision < 1
            || project.ActiveSubtitleTrackId != input.ExpectedTrackId)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.TrackRequired, "Hãy chọn subtitle track đã dịch trước khi tạo giọng.");
        }
        var tracks = await subtitleStore.LoadTracksAsync(project.ProjectId, cancellationToken);
        var track = tracks.SingleOrDefault(item => item.TrackId == input.ExpectedTrackId)
            ?? throw new VietsubVoiceException(VietsubVoiceErrorCodes.TrackRequired, "Không tìm thấy subtitle track đang dùng.");
        if (track.Revision != input.ExpectedTrackRevision)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.TrackChanged, "Phụ đề đã thay đổi. Hãy tải lại trước khi tạo giọng.");
        }
        VietsubVoiceTranslationPolicy.EnsureComplete(track);

        project.VoiceSettings.Normalize();
        var snapshot = new VietsubVoiceSettingsSnapshot(
            project.VoiceSettings.EngineId,
            VietsubVoiceCatalog.PiperEngineVersion,
            project.VoiceSettings.ModelId,
            VietsubVoiceCatalog.PiperModelVersion,
            project.VoiceSettings.VoiceId,
            project.VoiceSettings.MaximumPhraseGapMilliseconds,
            project.VoiceSettings.MaximumPhraseDurationMilliseconds,
            project.VoiceSettings.MaximumPhraseCharacters,
            project.VoiceSettings.MaximumBorrowedGapMilliseconds,
            project.VoiceSettings.PreferredMaximumTempo,
            project.VoiceSettings.MaximumTempo,
            project.VoiceSettings.TrimSilence);
        var parameters = new VietsubVoiceJobParameters(
            1,
            track.TrackId,
            track.Revision,
            VietsubVoiceFingerprintBuilder.BuildConfigurationFingerprint(snapshot),
            snapshot);

        VietsubJobSummary job;
        try
        {
            job = await jobManager.EnqueueAsync(
                project.ProjectId,
                VietsubJobTypes.SynthesizeVoiceLocal,
                ["VOICE_PREPARE", "VOICE_SYNTHESIZE", "VOICE_TIMELINE", "VOICE_PUBLISH"],
                parameters.ToJson(),
                track.TrackId,
                track.Revision,
                maxAttempts: 3,
                startImmediately: false,
                cancellationToken: cancellationToken);
        }
        catch (VietsubJobException exception) when (exception.Code == "vietsub_job_already_active")
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.JobConflict, "Project đang có local job khác chưa kết thúc.", innerException: exception);
        }

        try
        {
            await session.UpdateAsync(manifest => manifest.Status = VietsubProjectStatuses.Processing, cancellationToken);
            await session.FlushAsync(cancellationToken);
            await jobManager.StartAsync(project.ProjectId, job.Id, cancellationToken);
            return job;
        }
        catch
        {
            try { await jobManager.CancelAsync(project.ProjectId, job.Id, CancellationToken.None); } catch { }
            try
            {
                await session.UpdateAsync(manifest => manifest.Status = VietsubProjectStatuses.Ready, CancellationToken.None);
                await session.FlushAsync(CancellationToken.None);
            }
            catch { }
            throw;
        }
    }

    private async Task AuthorizeAsync(
        VietsubProjectManifest project,
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await authorizer.AuthorizeAsync(userId, organizationId, project, cancellationToken);
        }
        catch (VietsubLocalJobAuthorizationException exception)
        {
            throw new VietsubVoiceException(
                exception.Code == VietsubLocalJobAuthorizationErrorCodes.LicenseRequired
                    ? VietsubVoiceErrorCodes.LicenseRequired
                    : VietsubVoiceErrorCodes.AccessDenied,
                exception.Message,
                innerException: exception);
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.LocalVoice;
using TOOL_LOCAL.Generation;
using TOOL_LOCAL.Storage;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_LOCAL.Projects;

internal sealed record FinalRenderResult(
    Guid RenderJobId,
    Guid FinalVideoId,
    Guid MediaAssetId,
    int Version,
    string RelativePath,
    long DurationMs);

internal sealed record FinalVideoExportResult(
    Guid FinalVideoId,
    int Version,
    string FileName,
    long SizeBytes);

internal interface IProjectRenderService
{
    Task<FinalRenderResult> RenderFinalVideoAsync(
        Guid projectId,
        string remoteUserId,
        CancellationToken cancellationToken);

    Task<FinalVideoExportResult> ExportFinalVideoAsync(
        Guid projectId,
        string remoteUserId,
        string destinationPath,
        CancellationToken cancellationToken);
}

internal sealed class ProjectRenderService(
    IDbContextFactory<VideoFactoryDbContext> dbContextFactory,
    ProjectWorkspaceService workspaceService,
    IMediaToolPreflightService mediaToolPreflight,
    IFinalMediaRenderer renderer,
    IFinalOutputInspector outputInspector,
    bool speechVerificationEnabled = true,
    decimal targetSceneLoudnessLufs = -16m,
    LocalVoiceService? localVoice = null,
    IShortVideoLineageValidator? shortVideoOutfit = null) : IProjectRenderService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string SceneAudioSyncPolicyVersion = "scene-audio-sync-v3";
    private const string LegacyApprovedSceneAudioSyncPolicyVersion = "scene-audio-sync-v2";
    private readonly SemaphoreSlim _renderLock = new(1, 1);

    public async Task<FinalRenderResult> RenderFinalVideoAsync(
        Guid projectId,
        string remoteUserId,
        CancellationToken cancellationToken)
    {
        await _renderLock.WaitAsync(cancellationToken);
        try
        {
            var toolStatus = await mediaToolPreflight.RequireReadyAsync(cancellationToken);
            RenderInput input;
            await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
            {
                var project = await dbContext.Projects
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.ProjectId == projectId &&
                             x.RemoteUserId == remoteUserId &&
                             x.DeletedAtUtc == null,
                        cancellationToken)
                    ?? throw new ArgumentException("Không tìm thấy dự án hoặc bạn không có quyền dựng video.");
                if (project.CurrentScenePlanVersion is not { } scenePlanVersion)
                {
                    throw new ArgumentException("Dự án chưa có kế hoạch cảnh hiện hành.");
                }

                var currentScenes = await dbContext.Scenes
                    .AsNoTracking()
                    .Include(x => x.ApprovedGeneration)
                    .ThenInclude(x => x!.OutputMediaAsset)
                    .Include(x => x.ApprovedVoiceGeneration)
                    .ThenInclude(x => x!.OutputMediaAsset)
                    .Include(x => x.ApprovedRenderMediaAsset)
                    .Where(x => x.ProjectId == projectId && x.ScenePlanVersion == scenePlanVersion)
                    .OrderBy(x => x.SequenceNumber)
                    .ToListAsync(cancellationToken);
                if (currentScenes.Count == 0)
                {
                    throw new ArgumentException("Dự án chưa có cảnh để dựng video.");
                }
                var workflowStructureType = project.CurrentScriptVersion is null
                    ? null
                    : await dbContext.Scripts
                        .AsNoTracking()
                        .Where(x =>
                            x.ProjectId == projectId &&
                            x.Version == project.CurrentScriptVersion.Value)
                        .Select(x => x.StructureType)
                        .SingleOrDefaultAsync(cancellationToken);
                var isLongFormWorkflow = string.Equals(
                    workflowStructureType,
                    KlingLongFormVietnameseValidator.OpenAiStructuredPlan,
                    StringComparison.Ordinal);

                // Cho phép dựng một bản video từ bất kỳ số lượng cảnh đã duyệt nào.
                // Các cảnh chưa duyệt vẫn ở lại storyboard và không được đưa vào manifest.
                var scenes = currentScenes
                    .Where(x => x.Status == "Approved")
                    .ToList();
                if (scenes.Count == 0)
                {
                    throw new ArgumentException("Dự án chưa có cảnh đã duyệt để dựng video.");
                }

                var sources = new List<RenderSource>(scenes.Count);
                foreach (var scene in scenes)
                {
                    var generation = scene.ApprovedGeneration;
                    if (ShortVideoWorkflowService.RequiresImageReview(scene.RequiredCapabilitiesJson))
                        await (shortVideoOutfit ?? throw new InvalidDataException("Thiếu kiểm tra ảnh phối đồ."))
                            .ValidateLineageAsync(project, scene, generation?.ProviderRequestId, cancellationToken);
                    var speechMode = !string.IsNullOrWhiteSpace(scene.Dialogue)
                        ? KlingSpeechModes.OnCameraDialogue
                        : !string.IsNullOrWhiteSpace(scene.Narration)
                            ? KlingSpeechModes.NativeVoiceOver
                            : KlingSpeechModes.None;
                    var canonicalSpeech =
                        string.Equals(project.SpeechProductionPolicy, SpeechProductionPolicies.CanonicalVoice, StringComparison.Ordinal) &&
                        speechMode != KlingSpeechModes.None;
                    var canonicalNarration = canonicalSpeech;
                    var asset = canonicalNarration
                        ? scene.ApprovedRenderMediaAsset
                        : scene.ApprovedRenderMediaAsset ?? generation?.OutputMediaAsset;
                    var localVoiceRequired = project.LocalVoicePolicyVersion == LocalVoicePolicies.VeoLocalVoiceConsistency &&
                        speechMode == KlingSpeechModes.OnCameraDialogue;
                    if (localVoiceRequired)
                    {
                        if (localVoice is null) throw new ArgumentException("Chưa có bộ kiểm tra giọng local cho project này.");
                        asset = await localVoice.ResolveRenderAsync(dbContext, project, scene, cancellationToken);
                        // Native approval alone is not final approval after local voice is enabled.
                        if (asset is null) continue;
                    }
                    var silentOutput = string.Equals(
                        ReadStringProperty(generation?.OutputMediaAsset?.MetadataJson, "audioStrategy"),
                        "SilentOutput",
                        StringComparison.OrdinalIgnoreCase);
                    var canonicalVoiceApproved =
                        !canonicalNarration ||
                        (scene.ApprovedVoiceGenerationId.HasValue &&
                         scene.ApprovedVoiceGeneration is not null &&
                         scene.ApprovedVoiceGeneration.VoiceGenerationId == scene.ApprovedVoiceGenerationId &&
                          scene.ApprovedVoiceGeneration.Status == "Approved" &&
                          scene.ApprovedVoiceGeneration.ApprovedAtUtc != null &&
                          scene.ApprovedVoiceGeneration.ScenePlanVersion == scene.ScenePlanVersion &&
                          scene.SpeechStatus == "SpeechApproved");
                    var expectedSpeechHash = speechMode == KlingSpeechModes.None
                        ? null
                        : Sha256Hex(NormalizeNarration(
                            speechMode == KlingSpeechModes.OnCameraDialogue ? scene.Dialogue : scene.Narration));
                    var verificationSourceAssetId = canonicalNarration
                        ? scene.ApprovedVoiceGeneration?.OutputMediaAssetId
                        : generation?.OutputMediaAssetId;
                    var requiresSpeechVerification = !canonicalSpeech &&
                                                     speechVerificationEnabled &&
                                                     !isLongFormWorkflow;
                    var speechVerified = speechMode == KlingSpeechModes.None ||
                        !requiresSpeechVerification ||
                        await dbContext.SpeechVerificationReports.AsNoTracking().AnyAsync(
                            x => x.SceneId == scene.SceneId &&
                                 x.ExpectedSpeechHash == expectedSpeechHash &&
                                 x.SourceMediaAssetId == verificationSourceAssetId &&
                                 (!canonicalNarration ||
                                  x.MediaSha256 == scene.ApprovedVoiceGeneration!.OutputMediaAsset!.Sha256) &&
                                 (x.Status == SpeechVerificationStatuses.Passed ||
                                  x.Status == SpeechVerificationStatuses.NeedsReview && x.ReviewApproved),
                            cancellationToken);
                    var canonicalSnapshotValid = !canonicalNarration ||
                        (scene.ApprovedVoiceGeneration is not null &&
                         scene.ApprovedVoiceGeneration.NarrationHash == expectedSpeechHash &&
                         (ReadStringProperty(asset?.MetadataJson, "audioSyncPolicyVersion") == LegacyApprovedSceneAudioSyncPolicyVersion ||
                          ReadStringProperty(asset?.MetadataJson, "rawVideoMediaAssetId") == generation?.OutputMediaAssetId?.ToString("D")) &&
                         string.Equals(
                             ReadStringProperty(asset?.MetadataJson, "voiceGenerationId"),
                             scene.ApprovedVoiceGeneration.VoiceGenerationId.ToString("D"),
                             StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(
                             ReadStringProperty(asset?.MetadataJson, "voiceSnapshotHash"),
                             scene.ApprovedVoiceGeneration.VoiceSnapshotHash,
                             StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(
                             ReadStringProperty(asset?.MetadataJson, "speechHash"),
                             expectedSpeechHash,
                             StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(
                             ReadStringProperty(asset?.MetadataJson, "mixStrategy"),
                             SpeechMixStrategies.ReplaceAllNativeAudio,
                             StringComparison.Ordinal) &&
                         IsApprovedAudioSyncPolicyCompatible(
                             ReadStringProperty(asset?.MetadataJson, "audioSyncPolicyVersion")));
                    var assetTypeValid = canonicalNarration
                        ? asset?.AssetType == "SceneVideoNarrated"
                        : asset?.AssetType == "SceneVideo" || localVoiceRequired && asset?.AssetType == LocalVoicePolicies.AssetType;
                    var audioAudible = canonicalNarration
                        ? ReadBooleanProperty(asset?.MetadataJson, "canonicalVoiceAudible")
                        : ReadBooleanProperty(asset?.MetadataJson, "nativeAudioAudible");
                    if (scene.Status != "Approved" ||
                        scene.ApprovedGenerationId is null ||
                        generation is null ||
                        generation.VideoGenerationId != scene.ApprovedGenerationId ||
                        generation.Status != "Approved" ||
                        asset is null ||
                         !assetTypeValid ||
                         !canonicalVoiceApproved ||
                         !speechVerified ||
                         !canonicalSnapshotValid ||
                        asset.Status != "Ready" ||
                        asset.DeletedAtUtc is not null ||
                        ((canonicalNarration || !silentOutput) && !audioAudible))
                    {
                        throw new ArgumentException(
                            canonicalNarration
                                ? $"Cảnh {scene.SequenceNumber} chưa có clip Canonical Voice đã duyệt và còn khớp phiên bản lời đọc."
                                : silentOutput
                                ? $"Cảnh {scene.SequenceNumber} chưa có clip video không âm thanh đã duyệt hợp lệ."
                                : $"Cảnh {scene.SequenceNumber} chưa có video và lời nói đã kiểm tra/duyệt hợp lệ.");
                    }

                    var sourcePath = workspaceService.Resolve(NormalizeRelativePath(Path.Combine(
                        project.WorkspaceRelativePath,
                        asset.RelativePath)));
                    if (!File.Exists(sourcePath))
                    {
                        throw new FileNotFoundException(
                            $"Không tìm thấy clip đã duyệt của cảnh {scene.SequenceNumber}.",
                            sourcePath);
                    }
                    var actualHash = await ComputeFileSha256Async(sourcePath, cancellationToken);
                    if (!actualHash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"Clip đã duyệt của cảnh {scene.SequenceNumber} đã thay đổi trong workspace.");
                    }

                    sources.Add(new RenderSource(
                        scene.SceneId,
                        scene.SequenceNumber,
                        generation.VideoGenerationId,
                        asset.MediaAssetId,
                        asset.RelativePath,
                        sourcePath,
                        asset.Sha256,
                        asset.DurationMs ?? generation.ActualDurationMs ?? generation.RequestedDurationMs,
                        asset.AssetType,
                        canonicalNarration || !silentOutput,
                        localVoiceRequired,
                        localVoiceRequired ? localVoice!.GetRenderApprovalFingerprint(projectId, scene.SceneId, asset.MediaAssetId) : null));
                }

                if (sources.Count == 0) throw new ArgumentException("Chưa có cảnh đủ điều kiện dựng. Hãy duyệt kết quả giọng local hoặc xác nhận ngoại lệ native.");
                var version = (await dbContext.RenderJobs
                    .Where(x => x.ProjectId == projectId)
                    .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
                input = new RenderInput(
                    project.ProjectId,
                    project.WorkspaceRelativePath,
                    scenePlanVersion,
                    version,
                    project.OutputWidth,
                    project.OutputHeight,
                    project.OutputFrameRate,
                    project.SpeechProductionPolicy,
                    project.LocalVoicePolicyVersion,
                    sources.Any(source => source.AudioEnabled),
                    sources);
            }

            var outputRelativePath = NormalizeRelativePath(Path.Combine(
                input.WorkspaceRelativePath,
                "final",
                $"video-v{input.Version}.mp4"));
            var outputPath = workspaceService.Resolve(outputRelativePath);
            var workingDirectory = workspaceService.Resolve(NormalizeRelativePath(Path.Combine(
                input.WorkspaceRelativePath,
                "render",
                $"v{input.Version}")));
            var manifestJson = JsonSerializer.Serialize(new
            {
                audioStrategy = input.SpeechProductionPolicy == SpeechProductionPolicies.CanonicalVoice
                    ? "CanonicalVoice"
                    : input.AudioEnabled ? "ProviderNative" : "SilentOutput",
                input.SpeechProductionPolicy,
                input.LocalVoicePolicyVersion,
                input.ProjectId,
                input.ScenePlanVersion,
                input.Version,
                outputRelativePath,
                width = input.Width,
                height = input.Height,
                framesPerSecond = input.FramesPerSecond,
                scenes = input.Sources.Select(source => new
                {
                    source.SceneId,
                    source.SequenceNumber,
                    source.VideoGenerationId,
                    source.MediaAssetId,
                    source.RelativePath,
                    source.Sha256,
                    source.DurationMs,
                    source.AssetType,
                    source.AudioEnabled,
                    source.LocalVoiceApprovalFingerprint
                })
            }, JsonOptions);
            var manifestHash = Sha256Hex(manifestJson);
            var renderJobId = Guid.NewGuid();
            var startedAtUtc = DateTime.UtcNow;

            await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
            {
                dbContext.RenderJobs.Add(new RenderJob
                {
                    RenderJobId = renderJobId,
                    ProjectId = input.ProjectId,
                    Version = input.Version,
                    Status = "Rendering",
                    ManifestJson = manifestJson,
                    ManifestHash = manifestHash,
                    FfmpegVersion = toolStatus.FfmpegVersion,
                    ProgressPercent = 10,
                    CreatedAtUtc = startedAtUtc,
                    StartedAtUtc = startedAtUtc,
                    RowVersion = new byte[8]
                });
                var project = await dbContext.Projects.SingleAsync(
                    x => x.ProjectId == input.ProjectId,
                    cancellationToken);
                project.Status = "Rendering";
                project.LastErrorCode = null;
                project.LastErrorMessage = null;
                project.UpdatedAtUtc = startedAtUtc;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            try
            {
                await renderer.RenderAsync(
                    new FinalRenderManifest(
                        input.Sources.Select(x => x.AbsolutePath).ToArray(),
                        outputPath,
                        workingDirectory,
                        input.Width,
                        input.Height,
                        input.FramesPerSecond,
                        TargetSceneLoudnessLufs: targetSceneLoudnessLufs,
                        SceneAudioEnabled: input.Sources.Select(x => x.AudioEnabled).ToArray(),
                        OutputAudioEnabled: input.AudioEnabled),
                    cancellationToken);
                await MarkValidatingOutputAsync(renderJobId, cancellationToken);

                var inspection = await outputInspector.InspectAsync(outputPath, cancellationToken);
                ValidateOutput(input, inspection);
                var outputHash = await ComputeFileSha256Async(outputPath, cancellationToken);
                var outputInfo = new FileInfo(outputPath);
                var durationMs = checked((long)Math.Round(inspection.Probe.DurationSeconds * 1000m));
                var completedAtUtc = DateTime.UtcNow;
                var technicalReportJson = JsonSerializer.Serialize(new
                {
                    audioStrategy = input.SpeechProductionPolicy == SpeechProductionPolicies.CanonicalVoice
                        ? "CanonicalVoice"
                        : input.AudioEnabled ? "ProviderNative" : "SilentOutput",
                    input.SpeechProductionPolicy,
                    sourceAssetTypes = input.Sources
                        .Select(x => x.AssetType)
                        .Distinct()
                        .ToArray(),
                    mixedSceneAudio = input.Sources.Select(x => x.AudioEnabled).Distinct().Count() > 1,
                    inspection.Probe,
                    inspection.AudioQuality,
                    expectedSceneCount = input.Sources.Count,
                    input.LocalVoicePolicyVersion,
                    expectedDurationMs = input.Sources.Sum(x => x.DurationMs)
                }, JsonOptions);
                var mediaAssetId = Guid.NewGuid();
                var finalVideoId = Guid.NewGuid();

                await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var renderJob = await dbContext.RenderJobs.SingleAsync(
                    x => x.RenderJobId == renderJobId,
                    cancellationToken);
                var project = await dbContext.Projects.SingleAsync(
                    x => x.ProjectId == input.ProjectId,
                    cancellationToken);
                if (project.LocalVoicePolicyVersion != input.LocalVoicePolicyVersion ||
                    project.CurrentScenePlanVersion != input.ScenePlanVersion || project.DeletedAtUtc is not null)
                    throw new InvalidDataException("Project đã thay đổi trong khi dựng video. Hãy dựng lại từ phiên bản hiện hành.");
                foreach (var source in input.Sources.Where(x => x.LocalVoiceRequired))
                {
                    var scene = await dbContext.Scenes.AsNoTracking().SingleAsync(x => x.SceneId == source.SceneId, cancellationToken);
                    var currentAsset = localVoice is null ? null : await localVoice.ResolveRenderAsync(dbContext, project, scene, cancellationToken);
                    if (currentAsset?.MediaAssetId != source.MediaAssetId || currentAsset.Sha256 != source.Sha256 ||
                        source.LocalVoiceApprovalFingerprint != localVoice!.GetRenderApprovalFingerprint(input.ProjectId, source.SceneId, currentAsset.MediaAssetId))
                        throw new InvalidDataException("Nguồn hoặc mẫu giọng đã thay đổi khi dựng. Bản dựng không được công nhận.");
                }
                foreach (var source in input.Sources)
                {
                    var scene = await dbContext.Scenes.AsNoTracking().Include(x => x.ApprovedGeneration).SingleAsync(x => x.SceneId == source.SceneId, cancellationToken);
                    if (!ShortVideoWorkflowService.RequiresImageReview(scene.RequiredCapabilitiesJson)) continue;
                    if (scene.Status != "Approved" || scene.ApprovedGenerationId != source.VideoGenerationId || scene.ApprovedRenderMediaAssetId != source.MediaAssetId)
                        throw new InvalidDataException("Ảnh hoặc video đã đổi trong khi dựng.");
                    await (shortVideoOutfit ?? throw new InvalidDataException("Thiếu kiểm tra ảnh phối đồ."))
                        .ValidateLineageAsync(project, scene, scene.ApprovedGeneration?.ProviderRequestId, cancellationToken);
                }
                dbContext.MediaAssets.Add(new MediaAsset
                {
                    MediaAssetId = mediaAssetId,
                    ProjectId = input.ProjectId,
                    AssetType = "FinalVideo",
                    DisplayName = $"Video hoàn chỉnh v{input.Version}",
                    RelativePath = outputRelativePath,
                    MimeType = "video/mp4",
                    SizeBytes = outputInfo.Length,
                    Sha256 = outputHash,
                    Width = inspection.Probe.Width,
                    Height = inspection.Probe.Height,
                    FrameRate = inspection.Probe.FramesPerSecond,
                    DurationMs = durationMs,
                    AudioSampleRate = inspection.Probe.AudioSampleRate,
                    Status = "Ready",
                    SourceType = "Rendered",
                    MetadataJson = technicalReportJson,
                    CreatedAtUtc = completedAtUtc,
                    VerifiedAtUtc = completedAtUtc,
                    RowVersion = new byte[8]
                });
                dbContext.FinalVideos.Add(new FinalVideo
                {
                    FinalVideoId = finalVideoId,
                    ProjectId = input.ProjectId,
                    RenderJobId = renderJobId,
                    MediaAssetId = mediaAssetId,
                    Version = input.Version,
                    Status = "AwaitingApproval",
                    QualityScore = 100,
                    QualityReportJson = technicalReportJson,
                    CreatedAtUtc = completedAtUtc,
                    RowVersion = new byte[8]
                });
                renderJob.Status = "Completed";
                renderJob.ProgressPercent = 100;
                renderJob.OutputMediaAssetId = mediaAssetId;
                renderJob.TechnicalReportJson = technicalReportJson;
                renderJob.CompletedAtUtc = completedAtUtc;
                renderJob.ErrorCode = null;
                renderJob.ErrorMessage = null;
                project.Status = "AwaitingFinalApproval";
                project.LastErrorCode = null;
                project.LastErrorMessage = null;
                project.UpdatedAtUtc = completedAtUtc;
                await dbContext.SaveChangesAsync(cancellationToken);

                return new FinalRenderResult(
                    renderJobId,
                    finalVideoId,
                    mediaAssetId,
                    input.Version,
                    outputRelativePath,
                    durationMs);
            }
            catch (Exception exception)
            {
                await MarkFailedAsync(renderJobId, input.ProjectId, exception, CancellationToken.None);
                throw;
            }
        }
        finally
        {
            _renderLock.Release();
        }
    }

    public async Task<FinalVideoExportResult> ExportFinalVideoAsync(
        Guid projectId,
        string remoteUserId,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Hãy chọn vị trí lưu video MP4.", nameof(destinationPath));
        }

        string normalizedDestinationPath;
        try
        {
            normalizedDestinationPath = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(destinationPath.Trim()));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("Đường dẫn xuất video không hợp lệ.", nameof(destinationPath), exception);
        }

        if (!string.Equals(Path.GetExtension(normalizedDestinationPath), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Video hoàn chỉnh phải được xuất dưới định dạng MP4.", nameof(destinationPath));
        }
        if (normalizedDestinationPath.Length > 1000)
        {
            throw new ArgumentException("Đường dẫn xuất video quá dài.", nameof(destinationPath));
        }

        var destinationDirectory = Path.GetDirectoryName(normalizedDestinationPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
        {
            throw new ArgumentException("Thư mục lưu video không tồn tại.", nameof(destinationPath));
        }

        await _renderLock.WaitAsync(cancellationToken);
        try
        {
            FinalVideoExportInput input;
            await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
            {
                var projectExists = await dbContext.Projects
                    .AsNoTracking()
                    .AnyAsync(
                        x => x.ProjectId == projectId &&
                             x.RemoteUserId == remoteUserId &&
                             x.DeletedAtUtc == null,
                        cancellationToken);
                if (!projectExists)
                {
                    throw new ArgumentException("Không tìm thấy dự án hoặc bạn không có quyền xuất video.");
                }

                var finalVideo = await dbContext.FinalVideos
                    .AsNoTracking()
                    .Include(x => x.RenderJob)
                    .Include(x => x.MediaAsset)
                    .Where(x =>
                        x.ProjectId == projectId &&
                        x.RenderJob.Status == "Completed" &&
                        x.MediaAsset.AssetType == "FinalVideo" &&
                        x.MediaAsset.Status == "Ready" &&
                        x.MediaAsset.DeletedAtUtc == null &&
                        x.Status != "Rejected" &&
                        x.Status != "Invalid")
                    .OrderByDescending(x => x.Version)
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? throw new ArgumentException("Dự án chưa có video hoàn chỉnh đã dựng để xuất.");

                input = new FinalVideoExportInput(
                    finalVideo.FinalVideoId,
                    finalVideo.Version,
                    finalVideo.MediaAsset.RelativePath,
                    finalVideo.MediaAsset.Sha256);
            }

            var sourcePath = workspaceService.Resolve(input.RelativePath);
            if (!File.Exists(sourcePath))
            {
                throw new ArgumentException("Không tìm thấy file video hoàn chỉnh trong workspace.");
            }
            if (string.Equals(
                    Path.GetFullPath(sourcePath),
                    normalizedDestinationPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Hãy chọn vị trí khác với file video gốc trong workspace.");
            }

            var sourceHash = await ComputeFileSha256Async(sourcePath, cancellationToken);
            if (!sourceHash.Equals(input.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Video hoàn chỉnh đã thay đổi trong workspace. Hãy dựng lại trước khi xuất.");
            }

            var temporaryPath = Path.Combine(
                destinationDirectory,
                $".{Path.GetFileName(normalizedDestinationPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var source = new FileStream(
                                 sourcePath,
                                 FileMode.Open,
                                 FileAccess.Read,
                                 FileShare.Read,
                                 81920,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var destination = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 81920,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(destination, cancellationToken);
                    await destination.FlushAsync(cancellationToken);
                }

                var exportedHash = await ComputeFileSha256Async(temporaryPath, cancellationToken);
                if (!exportedHash.Equals(input.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("File MP4 vừa xuất không còn khớp với bản dựng đã kiểm tra.");
                }

                await ValidateShortVideoExportAsync(projectId, remoteUserId, input.FinalVideoId, cancellationToken);
                File.Move(temporaryPath, normalizedDestinationPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            var exportedAtUtc = DateTime.UtcNow;
            await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
            {
                var finalVideo = await dbContext.FinalVideos.SingleAsync(
                    x => x.FinalVideoId == input.FinalVideoId,
                    cancellationToken);
                var project = await dbContext.Projects.SingleAsync(
                    x => x.ProjectId == projectId,
                    cancellationToken);
                finalVideo.Status = "Exported";
                finalVideo.ApprovedAtUtc ??= exportedAtUtc;
                finalVideo.ExportedPath = normalizedDestinationPath;
                finalVideo.ExportedAtUtc = exportedAtUtc;
                project.Status = "Completed";
                project.LastErrorCode = null;
                project.LastErrorMessage = null;
                project.UpdatedAtUtc = exportedAtUtc;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return new FinalVideoExportResult(
                input.FinalVideoId,
                input.Version,
                Path.GetFileName(normalizedDestinationPath),
                new FileInfo(normalizedDestinationPath).Length);
        }
        finally
        {
            _renderLock.Release();
        }
    }

    private async Task ValidateShortVideoExportAsync(Guid projectId, string user, Guid finalId, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.ProjectId == projectId && x.RemoteUserId == user && x.DeletedAtUtc == null, ct);
        var scenes = await db.Scenes.AsNoTracking().Include(x => x.ApprovedGeneration).Where(x => x.ProjectId == projectId && x.ScenePlanVersion == project.CurrentScenePlanVersion).ToListAsync(ct);
        if (!scenes.Any(x => ShortVideoWorkflowService.RequiresImageReview(x.RequiredCapabilitiesJson))) return;
        var final = await db.FinalVideos.AsNoTracking().Include(x => x.RenderJob).SingleAsync(x => x.FinalVideoId == finalId && x.ProjectId == projectId, ct);
        using var manifest = JsonDocument.Parse(final.RenderJob.ManifestJson);
        foreach (var scene in scenes)
        {
            var source = manifest.RootElement.GetProperty("scenes").EnumerateArray().Single(x => x.GetProperty("sceneId").GetGuid() == scene.SceneId);
            if (scene.Status != "Approved" || scene.ApprovedGenerationId != source.GetProperty("videoGenerationId").GetGuid() || scene.ApprovedRenderMediaAssetId != source.GetProperty("mediaAssetId").GetGuid())
                throw new InvalidDataException("Bản dựng không còn khớp video đã duyệt. Hãy dựng lại.");
            await (shortVideoOutfit ?? throw new InvalidDataException("Thiếu kiểm tra ảnh phối đồ."))
                .ValidateLineageAsync(project, scene, scene.ApprovedGeneration?.ProviderRequestId, ct);
        }
    }

    private async Task MarkValidatingOutputAsync(Guid renderJobId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var renderJob = await dbContext.RenderJobs.SingleAsync(x => x.RenderJobId == renderJobId, cancellationToken);
        renderJob.Status = "ValidatingOutput";
        renderJob.ProgressPercent = 85;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(
        Guid renderJobId,
        Guid projectId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var renderJob = await dbContext.RenderJobs.SingleOrDefaultAsync(
                x => x.RenderJobId == renderJobId,
                cancellationToken);
            var project = await dbContext.Projects.SingleOrDefaultAsync(
                x => x.ProjectId == projectId,
                cancellationToken);
            var errorCode = exception is MediaToolUnavailableException mediaError
                ? mediaError.Code
                : exception is InvalidDataException
                    ? "final_output_invalid"
                    : "final_render_failed";
            var message = SafeMessage(exception.Message);
            if (renderJob is not null)
            {
                renderJob.Status = "Failed";
                renderJob.ErrorCode = errorCode;
                renderJob.ErrorMessage = message;
                renderJob.CompletedAtUtc = DateTime.UtcNow;
            }
            if (project is not null)
            {
                project.Status = "ReadyToRender";
                project.LastErrorCode = errorCode;
                project.LastErrorMessage = message;
                project.UpdatedAtUtc = DateTime.UtcNow;
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Preserve the original render/validation error.
        }
    }

    private static void ValidateOutput(RenderInput input, FinalOutputInspection inspection)
    {
        if (!inspection.Probe.HasVideo)
        {
            throw new InvalidDataException("Video cuối không có luồng hình ảnh hợp lệ.");
        }
        if (input.AudioEnabled && !inspection.Probe.HasAudio)
        {
            throw new InvalidDataException("Video cuối phải có Native Audio.");
        }
        if (input.AudioEnabled && !inspection.AudioQuality.IsAudible)
        {
            throw new InvalidDataException(
                $"Âm thanh video cuối không nghe được ({inspection.AudioQuality.FailureCode}).");
        }
        if (!input.AudioEnabled && inspection.Probe.HasAudio)
        {
            throw new InvalidDataException("Video cuối đã tắt âm thanh nhưng vẫn còn audio stream.");
        }
        if (inspection.Probe.Width != input.Width || inspection.Probe.Height != input.Height)
        {
            throw new InvalidDataException("Kích thước video cuối không đúng cấu hình dự án.");
        }

        var expectedSeconds = input.Sources.Sum(x => x.DurationMs) / 1000m;
        var tolerance = Math.Max(2m, expectedSeconds * 0.05m);
        if (inspection.Probe.DurationSeconds <= 0 ||
            Math.Abs(inspection.Probe.DurationSeconds - expectedSeconds) > tolerance)
        {
            throw new InvalidDataException("Thời lượng video cuối lệch quá giới hạn so với các clip đã duyệt.");
        }
    }

    private static bool ReadBooleanProperty(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadStringProperty(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string> ComputeFileSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string NormalizeNarration(string? value) =>
        SpeechTextNormalization.Normalize(value);

    private static bool IsApprovedAudioSyncPolicyCompatible(string? policyVersion) =>
        string.Equals(policyVersion, SceneAudioSyncPolicyVersion, StringComparison.Ordinal) ||
        string.Equals(policyVersion, LegacyApprovedSceneAudioSyncPolicyVersion, StringComparison.Ordinal);

    private static string NormalizeRelativePath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/');

    private static string SafeMessage(string message) =>
        message.Length <= 4000 ? message : message[..4000];

    private sealed record RenderInput(
        Guid ProjectId,
        string WorkspaceRelativePath,
        int ScenePlanVersion,
        int Version,
        int Width,
        int Height,
        decimal FramesPerSecond,
        string SpeechProductionPolicy,
        string? LocalVoicePolicyVersion,
        bool AudioEnabled,
        IReadOnlyList<RenderSource> Sources);

    private sealed record FinalVideoExportInput(
        Guid FinalVideoId,
        int Version,
        string RelativePath,
        string Sha256);

    private sealed record RenderSource(
        Guid SceneId,
        int SequenceNumber,
        Guid VideoGenerationId,
        Guid MediaAssetId,
        string RelativePath,
        string AbsolutePath,
        string Sha256,
        long DurationMs,
        string AssetType,
        bool AudioEnabled,
        bool LocalVoiceRequired,
        string? LocalVoiceApprovalFingerprint);
}

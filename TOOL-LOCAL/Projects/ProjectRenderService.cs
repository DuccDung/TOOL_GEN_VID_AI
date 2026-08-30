using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Storage;

namespace TOOL_LOCAL.Projects;

internal sealed record FinalRenderResult(
    Guid RenderJobId,
    Guid FinalVideoId,
    Guid MediaAssetId,
    int Version,
    string RelativePath,
    long DurationMs);

internal interface IProjectRenderService
{
    Task<FinalRenderResult> RenderFinalVideoAsync(
        Guid projectId,
        string remoteUserId,
        CancellationToken cancellationToken);
}

internal sealed class ProjectRenderService(
    IDbContextFactory<VideoFactoryDbContext> dbContextFactory,
    ProjectWorkspaceService workspaceService,
    IMediaToolPreflightService mediaToolPreflight,
    IFinalMediaRenderer renderer,
    IFinalOutputInspector outputInspector) : IProjectRenderService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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

                var scenes = await dbContext.Scenes
                    .AsNoTracking()
                    .Include(x => x.ApprovedGeneration)
                    .ThenInclude(x => x!.OutputMediaAsset)
                    .Where(x => x.ProjectId == projectId && x.ScenePlanVersion == scenePlanVersion)
                    .OrderBy(x => x.SequenceNumber)
                    .ToListAsync(cancellationToken);
                if (scenes.Count == 0)
                {
                    throw new ArgumentException("Dự án chưa có cảnh để dựng video.");
                }

                var sources = new List<RenderSource>(scenes.Count);
                foreach (var scene in scenes)
                {
                    var generation = scene.ApprovedGeneration;
                    var asset = generation?.OutputMediaAsset;
                    if (scene.Status != "Approved" ||
                        scene.ApprovedGenerationId is null ||
                        generation is null ||
                        generation.VideoGenerationId != scene.ApprovedGenerationId ||
                        generation.Status != "Approved" ||
                        asset is null ||
                        asset.AssetType != "SceneVideo" ||
                        asset.Status != "Ready" ||
                        asset.DeletedAtUtc is not null ||
                        !ReadBooleanProperty(asset.MetadataJson, "nativeAudioAudible"))
                    {
                        throw new ArgumentException(
                            $"Cảnh {scene.SequenceNumber} chưa có clip video Native Audio đã duyệt hợp lệ.");
                    }

                    var sourcePath = workspaceService.Resolve(asset.RelativePath);
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
                        asset.DurationMs ?? generation.ActualDurationMs ?? generation.RequestedDurationMs));
                }

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
                audioStrategy = "ProviderNative",
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
                    source.DurationMs
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
                        input.FramesPerSecond),
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
                    audioStrategy = "ProviderNative",
                    sourceAssetType = "SceneVideo",
                    inspection.Probe,
                    inspection.AudioQuality,
                    expectedSceneCount = input.Sources.Count,
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
        if (!inspection.Probe.HasVideo || !inspection.Probe.HasAudio)
        {
            throw new InvalidDataException("Video cuối phải có cả hình ảnh và Native Audio.");
        }
        if (!inspection.AudioQuality.IsAudible)
        {
            throw new InvalidDataException(
                $"Âm thanh video cuối không nghe được ({inspection.AudioQuality.FailureCode}).");
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
        IReadOnlyList<RenderSource> Sources);

    private sealed record RenderSource(
        Guid SceneId,
        int SequenceNumber,
        Guid VideoGenerationId,
        Guid MediaAssetId,
        string RelativePath,
        string AbsolutePath,
        string Sha256,
        long DurationMs);
}

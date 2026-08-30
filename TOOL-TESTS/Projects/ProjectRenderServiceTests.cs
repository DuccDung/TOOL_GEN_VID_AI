using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Projects;
using TOOL_LOCAL.Storage;

namespace TOOL_TESTS.Projects;

public sealed class ProjectRenderServiceTests
{
    [Fact]
    public async Task RenderFinalVideo_UsesOnlyApprovedSceneVideoAndPersistsValidatedOutput()
    {
        await using var fixture = await RenderFixture.CreateAsync("SceneVideo", nativeAudioAudible: true);

        var result = await fixture.Service.RenderFinalVideoAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);

        Assert.NotNull(fixture.Renderer.Manifest);
        Assert.Single(fixture.Renderer.Manifest!.ScenePaths);
        Assert.Null(fixture.Renderer.Manifest.VoicePath);
        Assert.Null(fixture.Renderer.Manifest.MusicPath);
        await using var dbContext = fixture.Factory.CreateDbContext();
        var renderJob = await dbContext.RenderJobs.SingleAsync();
        var finalVideo = await dbContext.FinalVideos.SingleAsync();
        var output = await dbContext.MediaAssets.SingleAsync(x => x.MediaAssetId == result.MediaAssetId);
        var project = await dbContext.Projects.SingleAsync();
        Assert.Equal("Completed", renderJob.Status);
        Assert.Equal("FinalVideo", output.AssetType);
        Assert.Equal("Rendered", output.SourceType);
        Assert.Contains("\"audioStrategy\":\"ProviderNative\"", renderJob.ManifestJson);
        Assert.DoesNotContain("SceneVideoNarrated", renderJob.ManifestJson, StringComparison.Ordinal);
        Assert.Equal("AwaitingApproval", finalVideo.Status);
        Assert.Equal("AwaitingFinalApproval", project.Status);
        Assert.True(File.Exists(fixture.Workspace.Resolve(result.RelativePath)));
    }

    [Fact]
    public async Task RenderFinalVideo_RejectsNarratedOrUnapprovedSourceBeforeFfmpeg()
    {
        await using var fixture = await RenderFixture.CreateAsync(
            "SceneVideoNarrated",
            nativeAudioAudible: true);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.RenderFinalVideoAsync(
                fixture.ProjectId,
                fixture.UserId,
                CancellationToken.None));

        Assert.Contains("clip video Native Audio đã duyệt", exception.Message);
        Assert.Equal(0, fixture.Renderer.CallCount);
        await using var dbContext = fixture.Factory.CreateDbContext();
        Assert.Empty(await dbContext.RenderJobs.ToListAsync());
    }

    [Fact]
    public async Task RenderFinalVideo_InvalidFinalAudioMarksLocalRenderFailedWithoutNewProviderRequest()
    {
        await using var fixture = await RenderFixture.CreateAsync(
            "SceneVideo",
            nativeAudioAudible: true,
            outputAudible: false);
        await using (var before = fixture.Factory.CreateDbContext())
        {
            Assert.Empty(await before.ProviderRequests.ToListAsync());
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Service.RenderFinalVideoAsync(
                fixture.ProjectId,
                fixture.UserId,
                CancellationToken.None));

        await using var dbContext = fixture.Factory.CreateDbContext();
        Assert.Equal("Failed", (await dbContext.RenderJobs.SingleAsync()).Status);
        Assert.Equal("ReadyToRender", (await dbContext.Projects.SingleAsync()).Status);
        Assert.Empty(await dbContext.ProviderRequests.ToListAsync());
    }

    private sealed class RenderFixture : IAsyncDisposable
    {
        private RenderFixture(
            string root,
            TestDbContextFactory factory,
            ProjectWorkspaceService workspace,
            CaptureRenderer renderer,
            ProjectRenderService service,
            Guid projectId,
            string userId)
        {
            Root = root;
            Factory = factory;
            Workspace = workspace;
            Renderer = renderer;
            Service = service;
            ProjectId = projectId;
            UserId = userId;
        }

        public string Root { get; }
        public TestDbContextFactory Factory { get; }
        public ProjectWorkspaceService Workspace { get; }
        public CaptureRenderer Renderer { get; }
        public ProjectRenderService Service { get; }
        public Guid ProjectId { get; }
        public string UserId { get; }

        public static async Task<RenderFixture> CreateAsync(
            string assetType,
            bool nativeAudioAudible,
            bool outputAudible = true)
        {
            var root = Path.Combine(Path.GetTempPath(), $"videomaker-render-{Guid.NewGuid():N}");
            var workspace = new ProjectWorkspaceService(root);
            var projectId = Guid.NewGuid();
            const string userId = "render-user";
            var projectRelativePath = workspace.Create(projectId);
            var sourceRelativePath = Path.Combine(projectRelativePath, "scenes", "scene-001.mp4")
                .Replace(Path.DirectorySeparatorChar, '/');
            var sourcePath = workspace.Resolve(sourceRelativePath);
            var sourceBytes = "approved-kling-native-audio"u8.ToArray();
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();

            var options = new DbContextOptionsBuilder<VideoFactoryDbContext>()
                .UseInMemoryDatabase($"project-render-{Guid.NewGuid():N}")
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var factory = new TestDbContextFactory(options);
            var sceneId = Guid.NewGuid();
            var generationId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            await using (var dbContext = factory.CreateDbContext())
            {
                dbContext.Projects.Add(new Project
                {
                    ProjectId = projectId,
                    RemoteUserId = userId,
                    Name = "Render test",
                    Topic = "Test",
                    LanguageCode = "vi-VN",
                    Platform = "YouTube",
                    AspectRatio = "16:9",
                    TargetDurationSeconds = 5,
                    OutputWidth = 1280,
                    OutputHeight = 720,
                    OutputFrameRate = 25,
                    Status = "ReadyToRender",
                    CurrentScenePlanVersion = 1,
                    CurrencyCode = "USD",
                    WorkspaceRelativePath = projectRelativePath,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    RowVersion = new byte[8]
                });
                dbContext.MediaAssets.Add(new MediaAsset
                {
                    MediaAssetId = assetId,
                    ProjectId = projectId,
                    SceneId = sceneId,
                    AssetType = assetType,
                    RelativePath = sourceRelativePath,
                    MimeType = "video/mp4",
                    SizeBytes = sourceBytes.Length,
                    Sha256 = sourceHash,
                    Width = 1280,
                    Height = 720,
                    FrameRate = 25,
                    DurationMs = 5000,
                    AudioSampleRate = 48000,
                    Status = "Ready",
                    SourceType = "Generated",
                    MetadataJson = $"{{\"nativeAudioAudible\":{nativeAudioAudible.ToString().ToLowerInvariant()}}}",
                    CreatedAtUtc = now,
                    VerifiedAtUtc = now,
                    RowVersion = new byte[8]
                });
                dbContext.VideoGenerations.Add(new VideoGeneration
                {
                    VideoGenerationId = generationId,
                    SceneId = sceneId,
                    ScenePromptId = Guid.NewGuid(),
                    ProviderRequestId = Guid.NewGuid(),
                    AttemptNumber = 1,
                    Status = "Approved",
                    RequestedDurationMs = 5000,
                    ActualDurationMs = 5000,
                    OutputMediaAssetId = assetId,
                    CreatedAtUtc = now,
                    CompletedAtUtc = now,
                    RowVersion = new byte[8]
                });
                dbContext.Scenes.Add(new Scene
                {
                    SceneId = sceneId,
                    ProjectId = projectId,
                    ScriptId = Guid.NewGuid(),
                    StyleProfileId = Guid.NewGuid(),
                    ScenePlanVersion = 1,
                    SequenceNumber = 1,
                    StoryPurpose = "Test",
                    VisualDescription = "Test",
                    ContentDurationMs = 5000,
                    GenerationDurationMs = 5000,
                    TimelineEndMs = 5000,
                    EntryStateJson = "{}",
                    ExitStateJson = "{}",
                    Status = "Approved",
                    ApprovedGenerationId = generationId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    RowVersion = new byte[8]
                });
                await dbContext.SaveChangesAsync();
            }

            var renderer = new CaptureRenderer();
            var inspector = new StubOutputInspector(outputAudible);
            var service = new ProjectRenderService(
                factory,
                workspace,
                new ReadyMediaToolPreflight(),
                renderer,
                inspector);
            return new RenderFixture(root, factory, workspace, renderer, service, projectId, userId);
        }

        public ValueTask DisposeAsync()
        {
            var resolvedRoot = Path.GetFullPath(Root);
            var tempRoot = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (resolvedRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(resolvedRoot).StartsWith("videomaker-render-", StringComparison.Ordinal) &&
                Directory.Exists(resolvedRoot))
            {
                Directory.Delete(resolvedRoot, recursive: true);
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CaptureRenderer : IFinalMediaRenderer
    {
        public int CallCount { get; private set; }
        public FinalRenderManifest? Manifest { get; private set; }

        public async Task RenderAsync(
            FinalRenderManifest manifest,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Manifest = manifest;
            Directory.CreateDirectory(Path.GetDirectoryName(manifest.OutputPath)!);
            await File.WriteAllBytesAsync(manifest.OutputPath, "valid-final-video"u8.ToArray(), cancellationToken);
        }
    }

    private sealed class StubOutputInspector(bool audible) : IFinalOutputInspector
    {
        public Task<FinalOutputInspection> InspectAsync(
            string outputPath,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FinalOutputInspection(
                new MediaProbeResult(5m, 1280, 720, 25, "h264", "aac", 48000, true, true),
                new AudioQualityResult(
                    true,
                    audible,
                    audible ? -20 : -80,
                    audible ? -3 : -70,
                    audible ? 0 : 1,
                    audible ? null : "audio_effectively_silent",
                    audible ? null : "Audio im lặng")));
    }

    private sealed class ReadyMediaToolPreflight : IMediaToolPreflightService
    {
        private static readonly MediaToolStatusSummary Ready = new(
            true,
            null,
            "Ready",
            "ffmpeg version test",
            "ffprobe version test",
            DateTime.UtcNow);

        public Task<MediaToolStatusSummary> GetStatusAsync(bool force, CancellationToken cancellationToken) =>
            Task.FromResult(Ready);

        public Task<MediaToolStatusSummary> RequireReadyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Ready);
    }

    internal sealed class TestDbContextFactory(DbContextOptions<VideoFactoryDbContext> options)
        : IDbContextFactory<VideoFactoryDbContext>
    {
        public VideoFactoryDbContext CreateDbContext() => new(options);
    }
}

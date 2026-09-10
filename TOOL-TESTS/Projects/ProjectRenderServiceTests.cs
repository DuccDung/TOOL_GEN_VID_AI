using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Projects;
using TOOL_LOCAL.Storage;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Projects;

public sealed class ProjectRenderServiceTests
{
    [Fact]
    public async Task LocalVoicePolicy_WithoutGuard_DoesNotFallBackToNativeApproval()
    {
        await using var f = await RenderFixture.CreateAsync("SceneVideo", true);
        await using (var db = f.Factory.CreateDbContext())
        {
            (await db.Projects.SingleAsync()).LocalVoicePolicyVersion = LocalVoicePolicies.VeoLocalVoiceConsistency;
            (await db.Scenes.SingleAsync()).Dialogue = "Test dialogue";
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.RenderFinalVideoAsync(f.ProjectId, f.UserId, default));
        Assert.Equal(0, f.Renderer.CallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalVoicePolicy_UsesOnlyExplicitlyApprovedCurrentResult(bool nativeException)
    {
        await using var f = await TOOL_TESTS.LocalVoice.LocalVoiceServiceTests.Fixture.CreateAsync();
        await f.EnableAsync();
        await using (var setup = f.Factory.CreateDbContext())
        {
            var project = await setup.Projects.SingleAsync(); project.OutputWidth = 1280; project.OutputHeight = 720;
            await setup.SaveChangesAsync();
        }
        if (nativeException) await f.Service.UseNativeAsync(f.ProjectId, f.User, f.Org, f.SceneId, true, "Reviewed native exception", default);
        else
        {
            var anchor = await f.ReviewableAsync(true); await f.ApproveAsync(anchor);
            var conversion = await f.ReviewableAsync(false, f.Store.Read(f.ProjectId, anchor.Id)); await f.ApproveAsync(conversion);
        }
        var renderer = new CaptureRenderer();
        var service = new ProjectRenderService(f.Factory, new ProjectWorkspaceService(f.Root), new ReadyMediaToolPreflight(),
            renderer, new StubOutputInspector(true, durationSeconds: 4), localVoice: f.Service);
        var result = await service.RenderFinalVideoAsync(f.ProjectId, f.User, default);
        Assert.NotEqual(Guid.Empty, result.FinalVideoId);
        Assert.EndsWith(nativeException ? "native.mp4" : "converted.mp4", renderer.Manifest!.ScenePaths.Single());
    }

    [Fact]
    public async Task LocalVoicePolicy_ChangedDuringRender_DoesNotPublishFinalVideo()
    {
        await using var f = await TOOL_TESTS.LocalVoice.LocalVoiceServiceTests.Fixture.CreateAsync(); await f.EnableAsync();
        await using (var setup = f.Factory.CreateDbContext())
        {
            var project = await setup.Projects.SingleAsync(); project.OutputWidth = 1280; project.OutputHeight = 720;
            await setup.SaveChangesAsync();
        }
        await f.Service.UseNativeAsync(f.ProjectId, f.User, f.Org, f.SceneId, true, "Reviewed exception", default);
        var renderer = new CaptureRenderer { BeforeReturn = async () => {
            await using var db = f.Factory.CreateDbContext(); (await db.Scenes.SingleAsync()).Dialogue = "Changed during render";
            await db.SaveChangesAsync();
        } };
        var service = new ProjectRenderService(f.Factory, new ProjectWorkspaceService(f.Root), new ReadyMediaToolPreflight(),
            renderer, new StubOutputInspector(true, durationSeconds: 4), localVoice: f.Service);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RenderFinalVideoAsync(f.ProjectId, f.User, default));
        await using var verify = f.Factory.CreateDbContext();
        Assert.Empty(await verify.FinalVideos.ToListAsync());
        Assert.Equal("Failed", (await verify.RenderJobs.SingleAsync()).Status);
    }

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
    public async Task RenderFinalVideo_UsesApprovedSubsetAndSkipsUnapprovedScenes()
    {
        await using var fixture = await RenderFixture.CreateAsync("SceneVideo", nativeAudioAudible: true);
        await using (var setupContext = fixture.Factory.CreateDbContext())
        {
            var now = DateTime.UtcNow;
            setupContext.Scenes.Add(new Scene
            {
                SceneId = Guid.NewGuid(),
                ProjectId = fixture.ProjectId,
                ScriptId = Guid.NewGuid(),
                StyleProfileId = Guid.NewGuid(),
                ScenePlanVersion = 1,
                SequenceNumber = 2,
                StoryPurpose = "Pending scene",
                VisualDescription = "Not approved yet",
                ContentDurationMs = 5_000,
                GenerationDurationMs = 5_000,
                TimelineStartMs = 5_000,
                TimelineEndMs = 10_000,
                EntryStateJson = "{}",
                ExitStateJson = "{}",
                Status = "PromptReady",
                SpeechStatus = SceneSpeechStatuses.SpeechNotRequired,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = new byte[8]
            });
            await setupContext.SaveChangesAsync();
        }

        var result = await fixture.Service.RenderFinalVideoAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.FinalVideoId);
        Assert.NotNull(fixture.Renderer.Manifest);
        Assert.Single(fixture.Renderer.Manifest!.ScenePaths);
        Assert.EndsWith("scene-001.mp4", fixture.Renderer.Manifest.ScenePaths[0], StringComparison.Ordinal);
        await using var dbContext = fixture.Factory.CreateDbContext();
        var renderJob = await dbContext.RenderJobs.SingleAsync();
        Assert.Contains("\"expectedSceneCount\":1", renderJob.TechnicalReportJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderFinalVideo_RejectsWhenNoSceneIsApproved()
    {
        await using var fixture = await RenderFixture.CreateAsync("SceneVideo", nativeAudioAudible: true);
        await using (var setupContext = fixture.Factory.CreateDbContext())
        {
            var scene = await setupContext.Scenes.SingleAsync();
            scene.Status = "PromptReady";
            scene.ApprovedGenerationId = null;
            scene.ApprovedRenderMediaAssetId = null;
            await setupContext.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.RenderFinalVideoAsync(
                fixture.ProjectId,
                fixture.UserId,
                CancellationToken.None));

        Assert.Contains("chưa có cảnh đã duyệt", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.Renderer.CallCount);
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

        Assert.Contains("video và lời nói đã kiểm tra/duyệt", exception.Message);
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

    [Fact]
    public async Task RenderFinalVideo_SilentSceneAcceptsOutputWithoutAudioStream()
    {
        await using var fixture = await RenderFixture.CreateAsync(
            "SceneVideo",
            nativeAudioAudible: false,
            silentOutput: true);

        var result = await fixture.Service.RenderFinalVideoAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);

        await using var dbContext = fixture.Factory.CreateDbContext();
        var renderJob = await dbContext.RenderJobs.SingleAsync();
        var output = await dbContext.MediaAssets.SingleAsync(x => x.MediaAssetId == result.MediaAssetId);
        Assert.Equal("Completed", renderJob.Status);
        Assert.Contains("\"audioStrategy\":\"SilentOutput\"", renderJob.ManifestJson);
        Assert.Contains("\"audioStrategy\":\"SilentOutput\"", output.MetadataJson);
        Assert.Null(output.AudioSampleRate);
    }

    [Fact]
    public async Task RenderFinalVideo_MixedAudioAndSilentScenesPassesAlignedAudioTimeline()
    {
        await using var fixture = await RenderFixture.CreateAsync(
            "SceneVideo",
            nativeAudioAudible: true,
            mixedSilentScene: true);

        await fixture.Service.RenderFinalVideoAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);

        Assert.NotNull(fixture.Renderer.Manifest);
        Assert.Equal(2, fixture.Renderer.Manifest!.ScenePaths.Count);
        Assert.Equal([true, false], fixture.Renderer.Manifest.SceneAudioEnabled);
        Assert.True(fixture.Renderer.Manifest.OutputAudioEnabled);
        await using var dbContext = fixture.Factory.CreateDbContext();
        var renderJob = await dbContext.RenderJobs.SingleAsync();
        Assert.Contains("\"mixedSceneAudio\":true", renderJob.TechnicalReportJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenderFinalVideo_CanonicalSpeechUsesApprovedMixedAsset(bool onCamera)
    {
        await using var fixture = await RenderFixture.CreateCanonicalAsync();
        if (onCamera)
        {
            await using var db = fixture.Factory.CreateDbContext();
            var scene = await db.Scenes.SingleAsync();
            scene.Dialogue = scene.Narration;
            scene.Narration = null;
            await db.SaveChangesAsync();
        }

        var result = await fixture.Service.RenderFinalVideoAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);

        Assert.NotNull(fixture.Renderer.Manifest);
        Assert.Single(fixture.Renderer.Manifest!.ScenePaths);
        Assert.EndsWith("scene-001-narrated.mp4", fixture.Renderer.Manifest.ScenePaths[0], StringComparison.Ordinal);
        await using var dbContext = fixture.Factory.CreateDbContext();
        var renderJob = await dbContext.RenderJobs.SingleAsync();
        Assert.Contains("\"audioStrategy\":\"CanonicalVoice\"", renderJob.ManifestJson, StringComparison.Ordinal);
        Assert.Empty(dbContext.SpeechVerificationReports);
        Assert.True(File.Exists(fixture.Workspace.Resolve(result.RelativePath)));
    }

    [Theory]
    [InlineData("voiceApproval")]
    [InlineData("speech")]
    [InlineData("sourceVideo")]
    public async Task RenderFinalVideo_CanonicalDialogueRejectsStaleSourcesBeforeFfmpeg(string change)
    {
        await using var fixture = await RenderFixture.CreateCanonicalAsync();
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var scene = await db.Scenes.SingleAsync();
            scene.Dialogue = scene.Narration;
            scene.Narration = null;
            if (change == "voiceApproval") (await db.VoiceGenerations.SingleAsync()).ApprovedAtUtc = null;
            if (change == "speech") scene.Dialogue = "Lời thoại mới khác bản đã duyệt.";
            if (change == "sourceVideo") (await db.VideoGenerations.SingleAsync()).OutputMediaAssetId = null;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.RenderFinalVideoAsync(fixture.ProjectId, fixture.UserId, default));
        Assert.Equal(0, fixture.Renderer.CallCount);
    }

    [Fact]
    public async Task RenderFinalVideo_CanonicalVoiceOverAcceptsExplicitlyApprovedLegacyV2Asset()
    {
        await using var fixture = await RenderFixture.CreateCanonicalAsync("scene-audio-sync-v2");

        var result = await fixture.Service.RenderFinalVideoAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.FinalVideoId);
        Assert.Equal(1, fixture.Renderer.CallCount);
        Assert.NotNull(fixture.Renderer.Manifest);
        Assert.Single(fixture.Renderer.Manifest!.ScenePaths);
        Assert.EndsWith("scene-001-narrated.mp4", fixture.Renderer.Manifest.ScenePaths[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderFinalVideo_CanonicalVoiceOverRejectsUnsupportedAudioSyncPolicy()
    {
        await using var fixture = await RenderFixture.CreateCanonicalAsync("scene-audio-sync-v1");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.RenderFinalVideoAsync(
                fixture.ProjectId,
                fixture.UserId,
                CancellationToken.None));

        Assert.Contains("Canonical Voice", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Renderer.CallCount);
    }

    [Fact]
    public async Task RenderFinalVideo_FeatureDisabled_AllowsLegacyNativeSpeechWithoutAsrReport()
    {
        await using var fixture = await RenderFixture.CreateAsync(
            "SceneVideo",
            nativeAudioAudible: true,
            speechVerificationEnabled: false,
            narration: "Xin chào bạn.");

        var result = await fixture.Service.RenderFinalVideoAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.FinalVideoId);
        Assert.Equal(1, fixture.Renderer.CallCount);
    }

    [Fact]
    public async Task RenderFinalVideo_LongFormFeatureEnabled_AllowsNativeSpeechWithoutAsrReport()
    {
        await using var fixture = await RenderFixture.CreateAsync(
            "SceneVideo",
            nativeAudioAudible: true,
            speechVerificationEnabled: true,
            narration: "Xin chào bạn.",
            workflowStructureType: "OpenAiStructuredPlan");

        var result = await fixture.Service.RenderFinalVideoAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.FinalVideoId);
        Assert.Equal(1, fixture.Renderer.CallCount);
    }

    [Fact]
    public async Task RenderFinalVideo_FeatureEnabled_RejectsNativeSpeechWithoutAsrReport()
    {
        await using var fixture = await RenderFixture.CreateAsync(
            "SceneVideo",
            nativeAudioAudible: true,
            speechVerificationEnabled: true,
            narration: "Xin chào bạn.",
            workflowStructureType: "DirectShortVideo");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.RenderFinalVideoAsync(
                fixture.ProjectId,
                fixture.UserId,
                CancellationToken.None));

        Assert.Contains("lời nói đã kiểm tra/duyệt", exception.Message);
        Assert.Equal(0, fixture.Renderer.CallCount);
    }

    [Fact]
    public async Task ExportFinalVideo_CopiesLatestValidatedRenderAndPersistsExportState()
    {
        await using var fixture = await RenderFixture.CreateAsync("SceneVideo", nativeAudioAudible: true);
        var render = await fixture.Service.RenderFinalVideoAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);
        var exportDirectory = Path.Combine(fixture.Root, "exports");
        Directory.CreateDirectory(exportDirectory);
        var destinationPath = Path.Combine(exportDirectory, "video-test.mp4");
        await File.WriteAllTextAsync(destinationPath, "old-export");

        var result = await fixture.Service.ExportFinalVideoAsync(
            fixture.ProjectId,
            fixture.UserId,
            destinationPath,
            CancellationToken.None);

        Assert.Equal(render.FinalVideoId, result.FinalVideoId);
        Assert.Equal(render.Version, result.Version);
        Assert.Equal("video-test.mp4", result.FileName);
        Assert.Equal(new FileInfo(destinationPath).Length, result.SizeBytes);
        Assert.Equal(
            await File.ReadAllBytesAsync(fixture.Workspace.Resolve(render.RelativePath)),
            await File.ReadAllBytesAsync(destinationPath));
        Assert.Empty(Directory.GetFiles(exportDirectory, "*.tmp"));
        await using var dbContext = fixture.Factory.CreateDbContext();
        var finalVideo = await dbContext.FinalVideos.SingleAsync();
        var project = await dbContext.Projects.SingleAsync();
        Assert.Equal("Exported", finalVideo.Status);
        Assert.Equal(Path.GetFullPath(destinationPath), finalVideo.ExportedPath);
        Assert.NotNull(finalVideo.ApprovedAtUtc);
        Assert.NotNull(finalVideo.ExportedAtUtc);
        Assert.Equal("Completed", project.Status);
        Assert.Empty(await dbContext.ProviderRequests.ToListAsync());
    }

    [Fact]
    public async Task ExportFinalVideo_RejectsChangedWorkspaceFileAndPreservesExistingDestination()
    {
        await using var fixture = await RenderFixture.CreateAsync("SceneVideo", nativeAudioAudible: true);
        var render = await fixture.Service.RenderFinalVideoAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);
        await File.WriteAllTextAsync(
            fixture.Workspace.Resolve(render.RelativePath),
            "tampered-final-video");
        var exportDirectory = Path.Combine(fixture.Root, "exports");
        Directory.CreateDirectory(exportDirectory);
        var destinationPath = Path.Combine(exportDirectory, "existing.mp4");
        await File.WriteAllTextAsync(destinationPath, "keep-me");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.ExportFinalVideoAsync(
                fixture.ProjectId,
                fixture.UserId,
                destinationPath,
                CancellationToken.None));

        Assert.Contains("đã thay đổi trong workspace", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("keep-me", await File.ReadAllTextAsync(destinationPath));
        await using var dbContext = fixture.Factory.CreateDbContext();
        Assert.Equal("AwaitingApproval", (await dbContext.FinalVideos.SingleAsync()).Status);
        Assert.Null((await dbContext.FinalVideos.SingleAsync()).ExportedPath);
    }

    [Fact]
    public async Task ExportFinalVideo_RejectsDifferentProjectOwner()
    {
        await using var fixture = await RenderFixture.CreateAsync("SceneVideo", nativeAudioAudible: true);
        await fixture.Service.RenderFinalVideoAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);
        var destinationPath = Path.Combine(fixture.Root, "unauthorized.mp4");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.ExportFinalVideoAsync(
                fixture.ProjectId,
                "another-user",
                destinationPath,
                CancellationToken.None));

        Assert.Contains("không có quyền", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destinationPath));
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
            bool outputAudible = true,
            bool silentOutput = false,
            bool speechVerificationEnabled = true,
            string? narration = null,
            bool mixedSilentScene = false,
            string workflowStructureType = "OpenAiStructuredPlan")
        {
            var root = Path.Combine(Path.GetTempPath(), $"videomaker-render-{Guid.NewGuid():N}");
            var workspace = new ProjectWorkspaceService(root);
            var projectId = Guid.NewGuid();
            const string userId = "render-user";
            var projectRelativePath = workspace.Create(projectId);
            const string sourceRelativePath = "scenes/scene-001.mp4";
            var sourcePath = workspace.Resolve(Path.Combine(projectRelativePath, sourceRelativePath));
            var sourceBytes = "approved-kling-native-audio"u8.ToArray();
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
            const string silentSourceRelativePath = "scenes/scene-002.mp4";
            var silentSourceBytes = "approved-kling-silent-video"u8.ToArray();
            if (mixedSilentScene)
            {
                var silentSourcePath = workspace.Resolve(Path.Combine(projectRelativePath, silentSourceRelativePath));
                await File.WriteAllBytesAsync(silentSourcePath, silentSourceBytes);
            }

            var options = new DbContextOptionsBuilder<VideoFactoryDbContext>()
                .UseInMemoryDatabase($"project-render-{Guid.NewGuid():N}")
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var factory = new TestDbContextFactory(options);
            var sceneId = Guid.NewGuid();
            var generationId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var scriptId = Guid.NewGuid();
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
                    CurrentScriptVersion = 1,
                    CurrentScenePlanVersion = 1,
                    CurrencyCode = "USD",
                    WorkspaceRelativePath = projectRelativePath,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    RowVersion = new byte[8]
                });
                dbContext.Scripts.Add(new Script
                {
                    ScriptId = scriptId,
                    ProjectId = projectId,
                    Version = 1,
                    StructureType = workflowStructureType,
                    FullText = narration ?? string.Empty,
                    StoryBeatsJson = "[]",
                    Status = "Approved",
                    CreatedAtUtc = now,
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
                    MetadataJson = silentOutput
                        ? "{\"nativeAudioAudible\":false,\"audioStrategy\":\"SilentOutput\"}"
                        : $"{{\"nativeAudioAudible\":{nativeAudioAudible.ToString().ToLowerInvariant()}}}",
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
                    ScriptId = scriptId,
                    StyleProfileId = Guid.NewGuid(),
                    ScenePlanVersion = 1,
                    SequenceNumber = 1,
                    StoryPurpose = "Test",
                    Narration = narration,
                    VisualDescription = "Test",
                    ContentDurationMs = 5000,
                    GenerationDurationMs = 5000,
                    TimelineEndMs = 5000,
                    EntryStateJson = "{}",
                    ExitStateJson = "{}",
                    Status = "Approved",
                    SpeechStatus = narration is null
                        ? SceneSpeechStatuses.SpeechNotRequired
                        : SceneSpeechStatuses.SpeechApproved,
                    ApprovedGenerationId = generationId,
                    ApprovedRenderMediaAssetId = assetId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    RowVersion = new byte[8]
                });
                if (mixedSilentScene)
                {
                    var silentSceneId = Guid.NewGuid();
                    var silentGenerationId = Guid.NewGuid();
                    var silentAssetId = Guid.NewGuid();
                    dbContext.MediaAssets.Add(new MediaAsset
                    {
                        MediaAssetId = silentAssetId,
                        ProjectId = projectId,
                        SceneId = silentSceneId,
                        AssetType = "SceneVideo",
                        RelativePath = silentSourceRelativePath,
                        MimeType = "video/mp4",
                        SizeBytes = silentSourceBytes.Length,
                        Sha256 = Convert.ToHexString(SHA256.HashData(silentSourceBytes)).ToLowerInvariant(),
                        Width = 1280,
                        Height = 720,
                        FrameRate = 25,
                        DurationMs = 5_000,
                        Status = "Ready",
                        SourceType = "Generated",
                        MetadataJson = "{\"nativeAudioAudible\":false,\"audioStrategy\":\"SilentOutput\"}",
                        CreatedAtUtc = now,
                        VerifiedAtUtc = now,
                        RowVersion = new byte[8]
                    });
                    dbContext.VideoGenerations.Add(new VideoGeneration
                    {
                        VideoGenerationId = silentGenerationId,
                        SceneId = silentSceneId,
                        ScenePromptId = Guid.NewGuid(),
                        ProviderRequestId = Guid.NewGuid(),
                        AttemptNumber = 1,
                        Status = "Approved",
                        RequestedDurationMs = 5_000,
                        ActualDurationMs = 5_000,
                        OutputMediaAssetId = silentAssetId,
                        CreatedAtUtc = now,
                        CompletedAtUtc = now,
                        RowVersion = new byte[8]
                    });
                    dbContext.Scenes.Add(new Scene
                    {
                        SceneId = silentSceneId,
                        ProjectId = projectId,
                        ScriptId = scriptId,
                        StyleProfileId = Guid.NewGuid(),
                        ScenePlanVersion = 1,
                        SequenceNumber = 2,
                        StoryPurpose = "Silent cutaway",
                        VisualDescription = "Silent cutaway",
                        ContentDurationMs = 5_000,
                        GenerationDurationMs = 5_000,
                        TimelineStartMs = 5_000,
                        TimelineEndMs = 10_000,
                        EntryStateJson = "{}",
                        ExitStateJson = "{}",
                        Status = "Approved",
                        SpeechStatus = SceneSpeechStatuses.SpeechNotRequired,
                        ApprovedGenerationId = silentGenerationId,
                        ApprovedRenderMediaAssetId = silentAssetId,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now,
                        RowVersion = new byte[8]
                    });
                }
                await dbContext.SaveChangesAsync();
            }

            var renderer = new CaptureRenderer();
            var inspector = new StubOutputInspector(
                outputAudible,
                hasAudio: !silentOutput,
                durationSeconds: mixedSilentScene ? 10m : 5m);
            var service = new ProjectRenderService(
                factory,
                workspace,
                new ReadyMediaToolPreflight(),
                renderer,
                inspector,
                speechVerificationEnabled);
            return new RenderFixture(root, factory, workspace, renderer, service, projectId, userId);
        }

        public static async Task<RenderFixture> CreateCanonicalAsync(
            string audioSyncPolicyVersion = "scene-audio-sync-v3")
        {
            var fixture = await CreateAsync("SceneVideo", nativeAudioAudible: false, outputAudible: true);
            const string spokenText = "Xin chào Việt Nam.";
            var speechHash = Convert.ToHexString(
                SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(SpeechTextNormalization.Normalize(spokenText))))
                .ToLowerInvariant();
            var voiceGenerationId = Guid.NewGuid();
            var voiceAssetId = Guid.NewGuid();
            var narratedAssetId = Guid.NewGuid();
            var snapshotHash = new string('c', 64);
            var narratedRelativePath = "scenes/scene-001-narrated.mp4";
            var narratedPath = fixture.Workspace.Resolve(Path.Combine(
                fixture.Workspace.Create(fixture.ProjectId),
                narratedRelativePath));
            var narratedBytes = "approved-canonical-narrated-video"u8.ToArray();
            await File.WriteAllBytesAsync(narratedPath, narratedBytes);

            await using var dbContext = fixture.Factory.CreateDbContext();
            var project = await dbContext.Projects.SingleAsync();
            var scene = await dbContext.Scenes.SingleAsync();
            var generation = await dbContext.VideoGenerations.SingleAsync();
            var rawVideoAsset = await dbContext.MediaAssets.SingleAsync(
                x => x.MediaAssetId == generation.OutputMediaAssetId);
            project.SpeechProductionPolicy = SpeechProductionPolicies.CanonicalVoice;
            rawVideoAsset.MetadataJson = "{\"nativeAudioAudible\":false,\"audioStrategy\":\"SilentOutput\"}";
            scene.Narration = spokenText;
            scene.SpeechStatus = SceneSpeechStatuses.SpeechApproved;
            scene.ApprovedVoiceGenerationId = voiceGenerationId;
            scene.ApprovedRenderMediaAssetId = narratedAssetId;
            dbContext.MediaAssets.AddRange(
                new MediaAsset
                {
                    MediaAssetId = voiceAssetId,
                    ProjectId = fixture.ProjectId,
                    SceneId = scene.SceneId,
                    AssetType = "SceneVoice",
                    RelativePath = "voice/scene-001.wav",
                    MimeType = "audio/wav",
                    SizeBytes = 1_024,
                    Sha256 = new string('b', 64),
                    DurationMs = 4_500,
                    AudioSampleRate = 24_000,
                    Status = "Ready",
                    SourceType = "Generated",
                    CreatedAtUtc = DateTime.UtcNow,
                    RowVersion = new byte[8]
                },
                new MediaAsset
                {
                    MediaAssetId = narratedAssetId,
                    ProjectId = fixture.ProjectId,
                    SceneId = scene.SceneId,
                    AssetType = "SceneVideoNarrated",
                    RelativePath = narratedRelativePath,
                    MimeType = "video/mp4",
                    SizeBytes = narratedBytes.Length,
                    Sha256 = Convert.ToHexString(SHA256.HashData(narratedBytes)).ToLowerInvariant(),
                    Width = 1280,
                    Height = 720,
                    FrameRate = 25,
                    DurationMs = 5_000,
                    AudioSampleRate = 48_000,
                    Status = "Ready",
                    SourceType = "Generated",
                    MetadataJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        rawVideoMediaAssetId = rawVideoAsset.MediaAssetId,
                        voiceGenerationId,
                        voiceSnapshotHash = snapshotHash,
                        speechHash,
                        mixStrategy = SpeechMixStrategies.ReplaceAllNativeAudio,
                        audioSyncPolicyVersion,
                        canonicalVoiceAudible = true
                    }),
                    CreatedAtUtc = DateTime.UtcNow,
                    VerifiedAtUtc = DateTime.UtcNow,
                    RowVersion = new byte[8]
                });
            dbContext.VoiceGenerations.Add(new VoiceGeneration
            {
                VoiceGenerationId = voiceGenerationId,
                ProjectId = fixture.ProjectId,
                ScriptId = scene.ScriptId,
                SceneId = scene.SceneId,
                ScenePlanVersion = scene.ScenePlanVersion,
                ProviderRequestId = Guid.NewGuid(),
                Version = 1,
                VoiceCode = "female-sweet",
                NarrationHash = speechHash,
                VoiceSnapshotHash = snapshotHash,
                VerificationStatus = SpeechVerificationStatuses.NotRequested,
                LanguageCode = "vi-VN",
                SpeakingRate = 1m,
                Status = "Approved",
                DurationMs = 4_500,
                OutputMediaAssetId = voiceAssetId,
                CreatedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow,
                ApprovedAtUtc = DateTime.UtcNow,
                RowVersion = new byte[8]
            });
            generation.Status = "Approved";
            await dbContext.SaveChangesAsync();
            return fixture;
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
        public Func<Task>? BeforeReturn { get; init; }
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
            if (BeforeReturn is not null) await BeforeReturn();
        }
    }

    private sealed class StubOutputInspector(
        bool audible,
        bool hasAudio = true,
        decimal durationSeconds = 5m) : IFinalOutputInspector
    {
        public Task<FinalOutputInspection> InspectAsync(
            string outputPath,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FinalOutputInspection(
                new MediaProbeResult(
                    durationSeconds,
                    1280,
                    720,
                    25,
                    "h264",
                    hasAudio ? "aac" : null,
                    hasAudio ? 48000 : null,
                    true,
                    hasAudio),
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

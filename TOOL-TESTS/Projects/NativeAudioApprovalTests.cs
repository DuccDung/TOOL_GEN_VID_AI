using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Projects;
using TOOL_LOCAL.Storage;

namespace TOOL_TESTS.Projects;

public sealed class NativeAudioApprovalTests
{
    [Fact]
    public async Task ApproveSceneNativeAudio_MarksGenerationAndProjectReadyToRender()
    {
        var fixture = await CreateFixtureAsync(nativeAudioAudible: true);
        try
        {
            await fixture.Service.ApproveSceneNativeAudioAsync(
                fixture.ProjectId,
                fixture.UserId,
                fixture.SceneId,
                true,
                CancellationToken.None);

            await using var verification = fixture.Factory.CreateDbContext();
            var scene = await verification.Scenes.SingleAsync(x => x.SceneId == fixture.SceneId);
            var generation = await verification.VideoGenerations.SingleAsync();
            var project = await verification.Projects.SingleAsync(x => x.ProjectId == fixture.ProjectId);
            Assert.Equal(generation.VideoGenerationId, scene.ApprovedGenerationId);
            Assert.Equal("Approved", scene.Status);
            Assert.Equal("Approved", generation.Status);
            Assert.Equal("ReadyToRender", project.Status);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task ApproveSceneNativeAudio_RejectsInaudibleClip()
    {
        var fixture = await CreateFixtureAsync(nativeAudioAudible: false);
        try
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                fixture.Service.ApproveSceneNativeAudioAsync(
                    fixture.ProjectId,
                    fixture.UserId,
                    fixture.SceneId,
                    true,
                    CancellationToken.None));

            Assert.Contains("chưa có Native Audio nghe được", exception.Message);
            await using var verification = fixture.Factory.CreateDbContext();
            var scene = await verification.Scenes.SingleAsync(x => x.SceneId == fixture.SceneId);
            Assert.Null(scene.ApprovedGenerationId);
            Assert.Equal("AudioReviewRequired", scene.Status);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task ApproveSceneNativeAudio_RejectsWhenPreviewWasNotPlayed()
    {
        var fixture = await CreateFixtureAsync(nativeAudioAudible: true);
        try
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                fixture.Service.ApproveSceneNativeAudioAsync(
                    fixture.ProjectId,
                    fixture.UserId,
                    fixture.SceneId,
                    false,
                    CancellationToken.None));

            Assert.Contains("phát và nghe clip", exception.Message);
            await using var verification = fixture.Factory.CreateDbContext();
            Assert.Null((await verification.Scenes.SingleAsync()).ApprovedGenerationId);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static async Task<Fixture> CreateFixtureAsync(bool nativeAudioAudible)
    {
        var databaseName = $"native-audio-approval-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var factory = new TestDbContextFactory(options);
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var scriptId = Guid.NewGuid();
        var styleId = Guid.NewGuid();
        var scenePromptId = Guid.NewGuid();
        var providerRequestId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        const string userId = "native-audio-user";
        var now = DateTime.UtcNow;

        await using (var dbContext = factory.CreateDbContext())
        {
            dbContext.Projects.Add(new Project
            {
                ProjectId = projectId,
                OrganizationId = Guid.NewGuid(),
                CreatedByUserId = userId,
                RemoteUserId = userId,
                Name = "Native audio project",
                Topic = "Test",
                LanguageCode = "vi-VN",
                Platform = "YouTube",
                AspectRatio = "16:9",
                TargetDurationSeconds = 5,
                OutputWidth = 1280,
                OutputHeight = 720,
                OutputFrameRate = 25,
                Status = "ScenePlanning",
                CurrentScriptVersion = 1,
                CurrentStyleVersion = 1,
                CurrentScenePlanVersion = 1,
                CurrencyCode = "USD",
                WorkspaceRelativePath = $"projects/{projectId:N}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = new byte[8]
            });
            dbContext.Scripts.Add(new Script
            {
                ScriptId = scriptId,
                ProjectId = projectId,
                Version = 1,
                StructureType = "Test",
                FullText = "Test",
                StoryBeatsJson = "[]",
                Status = "Approved",
                CreatedAtUtc = now,
                RowVersion = new byte[8]
            });
            dbContext.StyleProfiles.Add(new StyleProfile
            {
                StyleProfileId = styleId,
                ProjectId = projectId,
                Version = 1,
                Name = "Test",
                VisualStyleJson = "{}",
                Status = "Approved",
                CreatedAtUtc = now,
                RowVersion = new byte[8]
            });
            dbContext.Scenes.Add(new Scene
            {
                SceneId = sceneId,
                ProjectId = projectId,
                ScriptId = scriptId,
                StyleProfileId = styleId,
                ScenePlanVersion = 1,
                SequenceNumber = 1,
                StoryPurpose = "Test native speech",
                Dialogue = "Xin chào bạn.",
                VisualDescription = "Presenter speaks to camera.",
                ContentDurationMs = 5000,
                GenerationDurationMs = 5000,
                TimelineEndMs = 5000,
                EntryStateJson = "{}",
                ExitStateJson = "{}",
                Status = "AudioReviewRequired",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = new byte[8]
            });
            dbContext.ScenePrompts.Add(new ScenePrompt
            {
                ScenePromptId = scenePromptId,
                SceneId = sceneId,
                Version = 1,
                PromptTemplateName = "Test",
                PromptTemplateVersion = "1",
                CanonicalInputJson = "{}",
                FinalPrompt = "Presenter speaks to camera.",
                PromptHash = new string('a', 64),
                Status = "Approved",
                CreatedAtUtc = now,
                RowVersion = new byte[8]
            });
            dbContext.ProviderRequests.Add(new ProviderRequest
            {
                ProviderRequestId = providerRequestId,
                ProjectId = projectId,
                SceneId = sceneId,
                RequestKind = "Video",
                ProviderCode = "kling",
                ModelCode = "kling-3.0",
                IdempotencyKey = "native-audio-test",
                Status = "Completed",
                RequestJson = "{}",
                CurrencyCode = "USD",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = new byte[8]
            });
            dbContext.MediaAssets.Add(new MediaAsset
            {
                MediaAssetId = assetId,
                ProjectId = projectId,
                SceneId = sceneId,
                AssetType = "SceneVideo",
                RelativePath = "scenes/scene-001.mp4",
                MimeType = "video/mp4",
                SizeBytes = 1024,
                Sha256 = new string('b', 64),
                Status = "Ready",
                SourceType = "Generated",
                MetadataJson = $"{{\"nativeAudioAudible\":{nativeAudioAudible.ToString().ToLowerInvariant()}}}",
                CreatedAtUtc = now,
                VerifiedAtUtc = now,
                RowVersion = new byte[8]
            });
            dbContext.VideoGenerations.Add(new VideoGeneration
            {
                VideoGenerationId = Guid.NewGuid(),
                SceneId = sceneId,
                ScenePromptId = scenePromptId,
                ProviderRequestId = providerRequestId,
                AttemptNumber = 1,
                Status = "AudioReviewRequired",
                RequestedDurationMs = 5000,
                ActualDurationMs = 5000,
                OutputMediaAssetId = assetId,
                CreatedAtUtc = now,
                CompletedAtUtc = now,
                RowVersion = new byte[8]
            });
            await dbContext.SaveChangesAsync();
        }

        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"videomaker-native-audio-{Guid.NewGuid():N}");
        var service = new ProjectService(factory, new ProjectWorkspaceService(workspaceRoot));
        return new Fixture(factory, service, projectId, sceneId, userId, workspaceRoot);
    }

    private sealed record Fixture(
        TestDbContextFactory Factory,
        ProjectService Service,
        Guid ProjectId,
        Guid SceneId,
        string UserId,
        string WorkspaceRoot) : IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(WorkspaceRoot))
            {
                Directory.Delete(WorkspaceRoot, recursive: true);
            }
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<VideoFactoryDbContext> options)
        : IDbContextFactory<VideoFactoryDbContext>
    {
        public VideoFactoryDbContext CreateDbContext() => new(options);

        public Task<VideoFactoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}

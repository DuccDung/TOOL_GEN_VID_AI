using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Projects;
using TOOL_LOCAL.Storage;

namespace TOOL_TESTS.Projects;

public sealed class SceneManualUpdateTests
{
    [Fact]
    public async Task UpdateScene_AfterSpeechBudgetFailure_SavesShortenedNarrationAndNewPromptVersion()
    {
        using var fixture = await CreateFixtureAsync();
        var shortenedNarration = Words(28);

        await fixture.Service.UpdateSceneAsync(
            fixture.ProjectId,
            fixture.UserId,
            new UpdateSceneCommand(
                fixture.SceneId,
                shortenedNarration,
                "Mô tả hình ảnh đã chỉnh sửa.",
                "Prompt video đã chỉnh sửa.",
                "NativeVoiceOver",
                "tự nhiên, rõ ràng",
                "không khí trong nhà",
                "tiếng giấy nhẹ"));

        await using var verification = fixture.Factory.CreateDbContext();
        var project = await verification.Projects.SingleAsync();
        var scene = await verification.Scenes.SingleAsync();
        var prompts = await verification.ScenePrompts
            .OrderBy(x => x.Version)
            .ToListAsync();

        Assert.Equal("ScenePlanning", project.Status);
        Assert.Equal("PromptReady", scene.Status);
        Assert.Equal(shortenedNarration, scene.Narration);
        Assert.Null(scene.Dialogue);
        Assert.Null(scene.LastErrorCode);
        Assert.Null(scene.LastErrorMessage);
        Assert.Equal(2, prompts.Count);
        Assert.Equal("Superseded", prompts[0].Status);
        Assert.Equal("Approved", prompts[1].Status);
        Assert.Equal(2, prompts[1].Version);
        Assert.Equal("manual-storyboard-edit", prompts[1].PromptTemplateName);
    }

    [Fact]
    public async Task UpdateScene_WhenNarrationStillExceedsBudget_ReturnsSpecificValidationAndDoesNotWrite()
    {
        using var fixture = await CreateFixtureAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.UpdateSceneAsync(
                fixture.ProjectId,
                fixture.UserId,
                new UpdateSceneCommand(
                    fixture.SceneId,
                    Words(29),
                    "Mô tả hình ảnh.",
                    "Prompt video.",
                    "NativeVoiceOver")));

        Assert.Contains("29 từ, vượt mức 28 từ", exception.Message);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Equal("PromptInvalid", (await verification.Scenes.SingleAsync()).Status);
        Assert.Single(await verification.ScenePrompts.ToListAsync());
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var options = new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"scene-manual-update-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        const string userId = "scene-editor";
        var now = DateTime.UtcNow;

        await using (var dbContext = factory.CreateDbContext())
        {
            dbContext.Projects.Add(new Project
            {
                ProjectId = projectId,
                OrganizationId = Guid.NewGuid(),
                CreatedByUserId = userId,
                RemoteUserId = userId,
                Name = "Project with long narration",
                Topic = "Lịch sử",
                LanguageCode = "vi-VN",
                Platform = "YouTube",
                AspectRatio = "16:9",
                TargetDurationSeconds = 15,
                OutputWidth = 1280,
                OutputHeight = 720,
                OutputFrameRate = 25,
                Status = "GeneratingScenes",
                CurrentScriptVersion = 1,
                CurrentStyleVersion = 1,
                CurrentScenePlanVersion = 1,
                CurrencyCode = "USD",
                WorkspaceRelativePath = $"projects/{projectId:N}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = new byte[8]
            });
            dbContext.Scenes.Add(new Scene
            {
                SceneId = sceneId,
                ProjectId = projectId,
                ScriptId = Guid.NewGuid(),
                StyleProfileId = Guid.NewGuid(),
                ScenePlanVersion = 1,
                SequenceNumber = 2,
                StoryPurpose = "Cảnh cần rút ngắn lời dẫn",
                Narration = Words(52),
                VisualDescription = "Mô tả hình ảnh ban đầu.",
                ContentDurationMs = 15000,
                GenerationDurationMs = 15000,
                TimelineStartMs = 15000,
                TimelineEndMs = 30000,
                EntryStateJson = "{}",
                ExitStateJson = "{}",
                RequiredCapabilitiesJson = "{\"nativeAudio\":true,\"speechMode\":\"NativeVoiceOver\"}",
                Status = "PromptInvalid",
                LastErrorCode = "kling_spoken_text_too_long",
                LastErrorMessage = "Lời cảnh vượt ngân sách từ.",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = new byte[8]
            });
            dbContext.ScenePrompts.Add(new ScenePrompt
            {
                ScenePromptId = Guid.NewGuid(),
                SceneId = sceneId,
                Version = 1,
                PromptTemplateName = "openai-content-plan",
                PromptTemplateVersion = "2",
                CanonicalInputJson = "{}",
                FinalPrompt = "Prompt video ban đầu.",
                NegativePrompt = "Không chữ, không watermark.",
                PromptHash = new string('a', 64),
                Status = "Approved",
                CreatedAtUtc = now,
                ApprovedAtUtc = now
            });
            await dbContext.SaveChangesAsync();
        }

        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"videomaker-scene-update-{Guid.NewGuid():N}");
        return new Fixture(
            factory,
            new ProjectService(factory, new ProjectWorkspaceService(workspaceRoot)),
            projectId,
            sceneId,
            userId,
            workspaceRoot);
    }

    private static string Words(int count) =>
        string.Join(' ', Enumerable.Range(1, count).Select(index => $"từ{index}"));

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
        public VideoFactoryDbContext CreateDbContext() => new TestVideoFactoryDbContext(options);

        public Task<VideoFactoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class TestVideoFactoryDbContext(DbContextOptions<VideoFactoryDbContext> options)
        : VideoFactoryDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Project>()
                .Property(x => x.RowVersion)
                .ValueGeneratedNever()
                .IsConcurrencyToken(false);
            modelBuilder.Entity<Scene>()
                .Property(x => x.RowVersion)
                .ValueGeneratedNever()
                .IsConcurrencyToken(false);
            modelBuilder.Entity<ScenePrompt>()
                .Property(x => x.RowVersion)
                .ValueGeneratedNever()
                .IsConcurrencyToken(false);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries<ScenePrompt>().Where(x => x.State == EntityState.Added))
            {
                entry.Entity.RowVersion ??= new byte[8];
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}

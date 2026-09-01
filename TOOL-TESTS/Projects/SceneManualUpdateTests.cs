using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Projects;
using TOOL_LOCAL.Storage;

namespace TOOL_TESTS.Projects;

public sealed class SceneManualUpdateTests
{
    [Fact]
    public async Task UpdateScene_AfterLegacySpeechBudgetFailure_SavesLongNarrationAndNewPromptVersion()
    {
        using var fixture = await CreateFixtureAsync();
        var longNarration = Words(52);

        await fixture.Service.UpdateSceneAsync(
            fixture.ProjectId,
            fixture.UserId,
            new UpdateSceneCommand(
                fixture.SceneId,
                longNarration,
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
        Assert.Equal(longNarration, scene.Narration);
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
    public async Task UpdateScene_WhenNarrationExceedsLegacyBudget_SavesWithoutWordCountValidation()
    {
        using var fixture = await CreateFixtureAsync();
        var narration = Words(29);

        await fixture.Service.UpdateSceneAsync(
            fixture.ProjectId,
            fixture.UserId,
            new UpdateSceneCommand(
                fixture.SceneId,
                narration,
                "Mô tả hình ảnh.",
                "Prompt video.",
                "NativeVoiceOver"));

        await using var verification = fixture.Factory.CreateDbContext();
        var scene = await verification.Scenes.SingleAsync();
        Assert.Equal("PromptReady", scene.Status);
        Assert.Equal(narration, scene.Narration);
        Assert.Null(scene.LastErrorCode);
        Assert.Equal(2, await verification.ScenePrompts.CountAsync());
    }

    [Fact]
    public async Task UpdateScene_KlingLongFormRejectsEnglishManualContent()
    {
        using var fixture = await CreateFixtureAsync("OpenAiStructuredPlan", klingSnapshot: true);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.UpdateSceneAsync(
            fixture.ProjectId,
            fixture.UserId,
            new UpdateSceneCommand(
                fixture.SceneId,
                "Start with one simple action.",
                "A presenter stands in a bright studio.",
                "A presenter faces the camera in a bright studio.",
                "NativeVoiceOver",
                "warm and clear",
                "quiet room tone",
                "subtle movement")));

        Assert.Contains("phải bằng tiếng Việt", exception.Message, StringComparison.Ordinal);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Single(await verification.ScenePrompts.ToListAsync());
    }

    [Fact]
    public async Task UpdateScene_KlingDirectShortVideoKeepsVietnamesePromptAllowed()
    {
        using var fixture = await CreateFixtureAsync("DirectShortVideo", klingSnapshot: true);

        await fixture.Service.UpdateSceneAsync(
            fixture.ProjectId,
            fixture.UserId,
            new UpdateSceneCommand(
                fixture.SceneId,
                null,
                "Một cô gái đang đi bộ trên phố cổ.",
                "Một cô gái đang đi bộ trên phố cổ.",
                "None"));

        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Equal(2, await verification.ScenePrompts.CountAsync());
    }

    [Fact]
    public async Task UpdateScene_KlingLongFormRejectsVoiceOverWhenSceneStillHasCharacter()
    {
        using var fixture = await CreateFixtureAsync("OpenAiStructuredPlan", klingSnapshot: true);
        await using (var dbContext = fixture.Factory.CreateDbContext())
        {
            var scene = await dbContext.Scenes.SingleAsync();
            scene.CharacterIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { Guid.NewGuid() });
            await dbContext.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.UpdateSceneAsync(
            fixture.ProjectId,
            fixture.UserId,
            new UpdateSceneCommand(
                fixture.SceneId,
                "Hãy bắt đầu bằng một hành động nhỏ.",
                "Người dẫn đứng trong một studio sáng sủa.",
                "Người dẫn đứng trong một studio sáng sủa.",
                "NativeVoiceOver",
                "ấm áp và rõ ràng",
                "âm nền căn phòng yên tĩnh",
                "tiếng cử động nhẹ")));

        Assert.Contains("B-roll không gắn nhân vật", exception.Message, StringComparison.Ordinal);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Single(await verification.ScenePrompts.ToListAsync());
    }

    [Fact]
    public async Task UpdateScene_KlingLongFormOnCameraStoresDialogueOnlyAndKeepsModeConsistent()
    {
        using var fixture = await CreateFixtureAsync("OpenAiStructuredPlan", klingSnapshot: true);
        await using (var dbContext = fixture.Factory.CreateDbContext())
        {
            var scene = await dbContext.Scenes.SingleAsync();
            scene.CharacterIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { Guid.NewGuid() });
            await dbContext.SaveChangesAsync();
        }

        const string dialogue = "Hãy bắt đầu bằng một hành động nhỏ.";
        await fixture.Service.UpdateSceneAsync(
            fixture.ProjectId,
            fixture.UserId,
            new UpdateSceneCommand(
                fixture.SceneId,
                dialogue,
                "Người dẫn nói với khuôn mặt và miệng hiện rõ.",
                "Người dẫn nói trực tiếp với máy quay, khuôn mặt và miệng hiện rõ.",
                "OnCameraDialogue",
                "ấm áp và rõ ràng",
                "âm nền căn phòng yên tĩnh",
                "tiếng cử động nhẹ"));

        await using var verification = fixture.Factory.CreateDbContext();
        var updated = await verification.Scenes.SingleAsync();
        Assert.Equal(dialogue, updated.Dialogue);
        Assert.Null(updated.Narration);
        Assert.Contains("\"speechMode\":\"OnCameraDialogue\"", updated.RequiredCapabilitiesJson, StringComparison.Ordinal);
        Assert.Equal(2, await verification.ScenePrompts.CountAsync());
    }

    private static async Task<Fixture> CreateFixtureAsync(
        string structureType = "OpenAiStructuredPlan",
        bool klingSnapshot = false)
    {
        var options = new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"scene-manual-update-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var scriptId = Guid.NewGuid();
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
                VideoProviderCode = klingSnapshot ? "kling" : null,
                VideoModelCode = klingSnapshot ? "kling-3.0" : null,
                VideoPolicyVersion = klingSnapshot ? 1 : null,
                VideoResolution = klingSnapshot ? "720p" : null,
                VideoNativeAudio = klingSnapshot,
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
                StructureType = structureType,
                FullText = "Initial script",
                StoryBeatsJson = "[]",
                Status = "Approved",
                CreatedAtUtc = now,
                RowVersion = new byte[8]
            });
            dbContext.Scenes.Add(new Scene
            {
                SceneId = sceneId,
                ProjectId = projectId,
                ScriptId = scriptId,
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
                NegativePrompt = klingSnapshot ? "không phụ đề, không logo, không watermark" : "Không chữ, không watermark.",
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

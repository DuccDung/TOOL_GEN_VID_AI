using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Projects;
using TOOL_LOCAL.Storage;
using TOOL_SHARED.Contracts.Authentication;

namespace TOOL_TESTS.Projects;

public sealed class ShortVideoWorkflowTests
{
    [Fact]
    public async Task CreateShortVideoAsync_PersistsOnePromptReadySceneWithoutSpeech()
    {
        var databaseName = $"short-video-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var factory = new TestDbContextFactory(options);
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"videomaker-short-video-{Guid.NewGuid():N}");
        var service = new ProjectService(factory, new ProjectWorkspaceService(workspaceRoot));
        var organizationId = Guid.NewGuid();
        const string content = "Một con thuyền gỗ lướt qua vịnh Hạ Long lúc bình minh, máy quay điện ảnh chuyển động chậm.";

        try
        {
            var result = await service.CreateShortVideoAsync(
                new CreateShortVideoCommand(content, "9:16", 5, false, organizationId),
                new UserProfileResponse("short-user", "short@example.com", "Short User", "Active", ["User"]),
                Guid.NewGuid(),
                CancellationToken.None);

            await using var dbContext = factory.CreateDbContext();
            var project = await dbContext.Projects.SingleAsync(x => x.ProjectId == result.Project.ProjectId);
            var scene = await dbContext.Scenes.SingleAsync(x => x.SceneId == result.SceneId);
            var prompt = await dbContext.ScenePrompts.SingleAsync(x => x.SceneId == result.SceneId);

            Assert.Equal(organizationId, project.OrganizationId);
            Assert.Equal("9:16", project.AspectRatio);
            Assert.Equal(5, project.TargetDurationSeconds);
            Assert.Equal(1080, project.OutputWidth);
            Assert.Equal(1920, project.OutputHeight);
            Assert.Equal(1, project.CurrentScenePlanVersion);
            Assert.Equal("ScenePlanning", project.Status);
            Assert.Equal(5000, scene.ContentDurationMs);
            Assert.Equal(5000, scene.GenerationDurationMs);
            Assert.Equal(0, scene.TailTrimMs);
            Assert.Equal(5000, scene.TimelineEndMs);
            Assert.Null(scene.Narration);
            Assert.Null(scene.Dialogue);
            Assert.Equal("[]", scene.CharacterIdsJson);
            Assert.Equal("PromptReady", scene.Status);
            Assert.Equal(content, scene.VisualDescription);
            Assert.Equal(content, prompt.FinalPrompt);
            Assert.Equal("Approved", prompt.Status);
            Assert.Equal("manual-short-video", prompt.PromptTemplateName);

            using var capabilities = JsonDocument.Parse(scene.RequiredCapabilitiesJson!);
            Assert.True(capabilities.RootElement.GetProperty("nativeAudio").GetBoolean());
            Assert.False(capabilities.RootElement.GetProperty("outputAudioEnabled").GetBoolean());
            Assert.True(capabilities.RootElement.GetProperty("muteOutputAudio").GetBoolean());
            Assert.Equal("None", capabilities.RootElement.GetProperty("speechMode").GetString());
            Assert.True(capabilities.RootElement.GetProperty("textToVideo").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(16)]
    public async Task CreateShortVideoAsync_RejectsDurationOutsideFiveToFifteenSeconds(int durationSeconds)
    {
        var options = new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"short-video-invalid-duration-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var service = new ProjectService(
            new TestDbContextFactory(options),
            new ProjectWorkspaceService(Path.Combine(Path.GetTempPath(), $"videomaker-short-invalid-{Guid.NewGuid():N}")));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShortVideoAsync(
            new CreateShortVideoCommand("Cảnh biển lúc bình minh.", "9:16", durationSeconds, true, Guid.NewGuid()),
            new UserProfileResponse("short-user", "short@example.com", null, "Active", ["User"]),
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Contains("5–15 giây", exception.Message);
    }

    [Fact]
    public async Task CreateShortVideoAsync_UsesSquareOutputForSquareAspectRatio()
    {
        var options = new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"short-video-square-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var factory = new TestDbContextFactory(options);
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"videomaker-short-square-{Guid.NewGuid():N}");
        var service = new ProjectService(factory, new ProjectWorkspaceService(workspaceRoot));

        try
        {
            var result = await service.CreateShortVideoAsync(
                new CreateShortVideoCommand("Cảnh quảng trường về đêm với chuyển động máy quay mượt.", "1:1", 8, true, Guid.NewGuid()),
                new UserProfileResponse("short-user", "short@example.com", null, "Active", ["User"]),
                Guid.NewGuid(),
                CancellationToken.None);

            await using var dbContext = factory.CreateDbContext();
            var project = await dbContext.Projects.SingleAsync(x => x.ProjectId == result.Project.ProjectId);
            Assert.Equal(1080, project.OutputWidth);
            Assert.Equal(1080, project.OutputHeight);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
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
        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries().Where(x => x.State == EntityState.Added))
            {
                var rowVersion = entry.Metadata.FindProperty("RowVersion");
                if (rowVersion is not null)
                {
                    entry.Property("RowVersion").CurrentValue = new byte[8];
                }
            }

            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }
}

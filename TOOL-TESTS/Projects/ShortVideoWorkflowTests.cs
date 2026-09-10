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
    public async Task DraftCreationRetryReusesProjectAndRejectsChangedScopeOrSettings()
    {
        var options = new DbContextOptionsBuilder<VideoFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        var factory = new TestDbContextFactory(options);
        var root = Path.Combine(Path.GetTempPath(), "videomaker-short-retry-" + Guid.NewGuid().ToString("N"));
        var service = new ProjectService(factory, new ProjectWorkspaceService(root));
        var user = new UserProfileResponse("short-user", "short@example.test", "Short", "Active", ["User"]);
        var command = new CreateShortVideoCommand("Studio", "9:16", 8, true, Guid.NewGuid(), "CharacterOutfit", Guid.NewGuid());
        try
        {
            var first = await service.CreateShortVideoAsync(command, user, Guid.NewGuid());
            var retry = await service.CreateShortVideoAsync(command, user, Guid.NewGuid());
            Assert.Equal(first, retry);
            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShortVideoAsync(command with { AudioEnabled = false }, user, Guid.NewGuid()));
            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShortVideoAsync(command with { Content = "Different" }, user, Guid.NewGuid()));
            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShortVideoAsync(command with { OrganizationId = Guid.NewGuid() }, user, Guid.NewGuid()));
            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShortVideoAsync(command, user with { UserId = "other" }, Guid.NewGuid()));
            await using var db = factory.CreateDbContext(); Assert.Single(db.Projects); Assert.Single(db.Scenes); Assert.Single(db.ScenePrompts);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("TextOnly")]
    [InlineData("CharacterOutfit")]
    public async Task CreateShortVideoAsync_PersistsOnePromptReadySceneWithoutSpeech(string mode)
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
                new CreateShortVideoCommand(content, "9:16", 6, false, organizationId, mode),
                new UserProfileResponse("short-user", "short@example.com", "Short User", "Active", ["User"]),
                Guid.NewGuid(),
                CancellationToken.None);

            await using var dbContext = factory.CreateDbContext();
            var project = await dbContext.Projects.SingleAsync(x => x.ProjectId == result.Project.ProjectId);
            var scene = await dbContext.Scenes.SingleAsync(x => x.SceneId == result.SceneId);
            var prompt = await dbContext.ScenePrompts.SingleAsync(x => x.SceneId == result.SceneId);

            Assert.Equal(organizationId, project.OrganizationId);
            Assert.Equal("9:16", project.AspectRatio);
            Assert.Equal(6, project.TargetDurationSeconds);
            Assert.Equal(1080, project.OutputWidth);
            Assert.Equal(1920, project.OutputHeight);
            Assert.Equal(1, project.CurrentScenePlanVersion);
            Assert.Equal("ScenePlanning", project.Status);
            Assert.Equal(6000, scene.ContentDurationMs);
            Assert.Equal(6000, scene.GenerationDurationMs);
            Assert.Equal(0, scene.TailTrimMs);
            Assert.Equal(6000, scene.TimelineEndMs);
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
            Assert.False(capabilities.RootElement.GetProperty("textToVideo").GetBoolean());
            Assert.True(capabilities.RootElement.GetProperty("requiresFirstFrame").GetBoolean());
            Assert.Equal(mode, capabilities.RootElement.GetProperty("shortVideoMode").GetString());
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
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(15)]
    [InlineData(16)]
    public async Task CreateShortVideoAsync_RejectsDurationsUnsupportedByVeo(int durationSeconds)
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

        Assert.Contains("4, 6 hoặc 8 giây", exception.Message);
    }

    [Fact]
    public async Task CreateShortVideoAsync_RejectsSquareAspectRatioBeforeCreatingProject()
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
            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShortVideoAsync(
                new CreateShortVideoCommand("Cảnh quảng trường về đêm với chuyển động máy quay mượt.", "1:1", 8, true, Guid.NewGuid()),
                new UserProfileResponse("short-user", "short@example.com", null, "Active", ["User"]),
                Guid.NewGuid(),
                CancellationToken.None));

            await using var dbContext = factory.CreateDbContext();
            Assert.Empty(dbContext.Projects);
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

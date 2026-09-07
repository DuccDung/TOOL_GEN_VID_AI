using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Projects;
using TOOL_LOCAL.Storage;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Projects;

public sealed class VoiceCatalogPreviewContextTests
{
    [Fact]
    public async Task ProjectQueries_HideVoiceCatalogPreviewContext()
    {
        var options = new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"voice-catalog-context-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);
        var userId = "voice-user";
        var visible = CreateProject(userId, $"projects/{Guid.NewGuid():N}", "Visible project");
        var hidden = CreateProject(
            userId,
            $"{VoiceCatalogPreviewContexts.WorkspacePrefix}{Guid.NewGuid():N}",
            VoiceCatalogPreviewContexts.ProjectName);
        await using (var dbContext = factory.CreateDbContext())
        {
            dbContext.Projects.AddRange(visible, hidden);
            await dbContext.SaveChangesAsync();
        }
        var workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"videomaker-voice-context-{Guid.NewGuid():N}");
        try
        {
            var service = new ProjectService(factory, new ProjectWorkspaceService(workspaceRoot));

            var projects = await service.ListAsync(userId);
            var hiddenDashboard = await service.GetDashboardAsync(hidden.ProjectId, userId);

            Assert.Collection(projects, project => Assert.Equal(visible.ProjectId, project.ProjectId));
            Assert.Null(hiddenDashboard);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    private static Project CreateProject(string userId, string workspace, string name)
    {
        var now = DateTime.UtcNow;
        return new Project
        {
            ProjectId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            CreatedByUserId = userId,
            RemoteUserId = userId,
            Name = name,
            Topic = name,
            LanguageCode = "vi-VN",
            SpeechProductionPolicy = SpeechProductionPolicies.ProviderNativeVerified,
            Platform = "YouTube",
            AspectRatio = "16:9",
            TargetDurationSeconds = 5,
            OutputWidth = 1920,
            OutputHeight = 1080,
            OutputFrameRate = 30,
            Status = "Draft",
            CurrencyCode = "USD",
            WorkspaceRelativePath = workspace,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
    }

    private sealed class TestDbContextFactory(DbContextOptions<VideoFactoryDbContext> options)
        : IDbContextFactory<VideoFactoryDbContext>
    {
        public VideoFactoryDbContext CreateDbContext() => new TestVideoFactoryDbContext(options);

        public Task<VideoFactoryDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class TestVideoFactoryDbContext(DbContextOptions<VideoFactoryDbContext> options)
        : VideoFactoryDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Project>().Property(x => x.RowVersion).ValueGeneratedNever();
        }
    }
}

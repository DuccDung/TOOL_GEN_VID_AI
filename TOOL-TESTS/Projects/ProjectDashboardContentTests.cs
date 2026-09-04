using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Projects;
using TOOL_LOCAL.Storage;

namespace TOOL_TESTS.Projects;

public sealed class ProjectDashboardContentTests
{
    [Fact]
    public async Task GetDashboardAsync_ReturnsPersistedContentWhenProjectHasNoScenes()
    {
        await using var fixture = await CreateFixtureAsync(currentScriptVersion: 1);

        var dashboard = await fixture.Service.GetDashboardAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);

        Assert.NotNull(dashboard);
        Assert.Equal(0, dashboard.TotalScenes);
        Assert.NotNull(dashboard.Content);
        Assert.Equal(1, dashboard.Content.ScriptVersion);
        Assert.Equal("Hành trình của cô gái", dashboard.Content.Title);
        Assert.Equal("Toàn bộ kịch bản đã được lưu.", dashboard.Content.ScriptFullText);
        Assert.Equal("Một lời hứa thay đổi tất cả", dashboard.Content.Hook);
        Assert.Equal("Kể chuyện điện ảnh", dashboard.Content.Angle);
        Assert.Equal("Người xem yêu truyện cổ tích", dashboard.Content.Audience);
        Assert.Equal("Theo dõi phần tiếp theo", dashboard.Content.CallToAction);
    }

    [Fact]
    public async Task GetDashboardAsync_FallsBackToLatestApprovedScriptWhenCurrentVersionIsMissing()
    {
        await using var fixture = await CreateFixtureAsync(currentScriptVersion: null, includeNewerSupersededScript: true);

        var dashboard = await fixture.Service.GetDashboardAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);

        Assert.NotNull(dashboard);
        Assert.NotNull(dashboard.Content);
        Assert.Equal(1, dashboard.Content.ScriptVersion);
        Assert.Equal("Toàn bộ kịch bản đã được lưu.", dashboard.Content.ScriptFullText);
    }

    private static async Task<Fixture> CreateFixtureAsync(
        int? currentScriptVersion,
        bool includeNewerSupersededScript = false)
    {
        var options = new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"project-dashboard-content-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);
        var projectId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        const string userId = "content-owner";

        await using (var dbContext = factory.CreateDbContext())
        {
            dbContext.Projects.Add(new Project
            {
                ProjectId = projectId,
                RemoteUserId = userId,
                Name = "Dự án nội dung cũ",
                Topic = "Cô gái quàng khăn đỏ",
                LanguageCode = "vi-VN",
                Platform = "YouTube",
                AspectRatio = "16:9",
                TargetDurationSeconds = 75,
                OutputWidth = 1920,
                OutputHeight = 1080,
                OutputFrameRate = 30,
                Status = "ScenePlanning",
                CurrentConceptVersion = 1,
                CurrentScriptVersion = currentScriptVersion,
                RequireContentApproval = true,
                RequireStoryboardApproval = true,
                CurrencyCode = "USD",
                WorkspaceRelativePath = $"projects/{projectId:N}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = new byte[8]
            });
            dbContext.Concepts.Add(new Concept
            {
                ConceptId = conceptId,
                ProjectId = projectId,
                Version = 1,
                Title = "Hành trình của cô gái",
                SelectedHook = "Một lời hứa thay đổi tất cả",
                Angle = "Kể chuyện điện ảnh",
                Audience = "Người xem yêu truyện cổ tích",
                CallToAction = "Theo dõi phần tiếp theo",
                Status = "Approved",
                CreatedAtUtc = now,
                ApprovedAtUtc = now,
                RowVersion = new byte[8]
            });
            dbContext.Scripts.Add(new Script
            {
                ScriptId = Guid.NewGuid(),
                ProjectId = projectId,
                ConceptId = conceptId,
                Version = 1,
                StructureType = "OpenAiStructuredPlan",
                Title = "Hành trình của cô gái",
                FullText = "Toàn bộ kịch bản đã được lưu.",
                StoryBeatsJson = "[]",
                Status = "Approved",
                CreatedAtUtc = now,
                ApprovedAtUtc = now,
                RowVersion = new byte[8]
            });

            if (includeNewerSupersededScript)
            {
                dbContext.Scripts.Add(new Script
                {
                    ScriptId = Guid.NewGuid(),
                    ProjectId = projectId,
                    ConceptId = conceptId,
                    Version = 2,
                    StructureType = "OpenAiStructuredPlan",
                    Title = "Bản không còn hiệu lực",
                    FullText = "Không được hiển thị.",
                    StoryBeatsJson = "[]",
                    Status = "Superseded",
                    CreatedAtUtc = now.AddMinutes(1),
                    RowVersion = new byte[8]
                });
            }

            await dbContext.SaveChangesAsync();
        }

        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"videomaker-dashboard-content-{Guid.NewGuid():N}");
        return new Fixture(
            new ProjectService(factory, new ProjectWorkspaceService(workspaceRoot)),
            projectId,
            userId,
            workspaceRoot);
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
            modelBuilder.Entity<Project>().Property(x => x.RowVersion).ValueGeneratedNever();
            modelBuilder.Entity<Concept>().Property(x => x.RowVersion).ValueGeneratedNever();
            modelBuilder.Entity<Script>().Property(x => x.RowVersion).ValueGeneratedNever();
        }
    }

    private sealed record Fixture(
        ProjectService Service,
        Guid ProjectId,
        string UserId,
        string WorkspaceRoot) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(WorkspaceRoot))
            {
                Directory.Delete(WorkspaceRoot, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}

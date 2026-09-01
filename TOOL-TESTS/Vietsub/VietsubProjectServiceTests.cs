using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Vietsub;
using TOOL_SERVER.Vietsub.Data;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubProjectServiceTests
{
    [Fact]
    public async Task Owner_CreateListRenameArchive_WritesMetadataOnlyAudit()
    {
        await using var fixture = CreateFixture(OrganizationMemberRoles.Owner);
        var projectId = Guid.NewGuid();
        var created = await fixture.Service.CreateAsync(
            new CreateVietsubProjectRequest(
                projectId,
                fixture.OrganizationId,
                "  Dự án tiếng Việt  ",
                "en",
                "vi"),
            fixture.Context,
            CancellationToken.None);
        var listed = await fixture.Service.ListAsync(
            fixture.OrganizationId,
            fixture.Context,
            CancellationToken.None);
        var renamed = await fixture.Service.RenameAsync(
            projectId,
            new RenameVietsubProjectRequest(fixture.OrganizationId, "Tên mới"),
            fixture.Context,
            CancellationToken.None);
        var archived = await fixture.Service.ArchiveAsync(
            projectId,
            fixture.OrganizationId,
            fixture.Context,
            CancellationToken.None);

        Assert.Equal("Dự án tiếng Việt", created.Name);
        Assert.Equal("en", created.SourceLanguageCode);
        Assert.Equal("vi", created.TargetLanguageCode);
        Assert.Single(listed);
        Assert.Equal("Tên mới", renamed.Name);
        Assert.True(archived.IsArchived);
        Assert.Empty(await fixture.Service.ListAsync(
            fixture.OrganizationId,
            fixture.Context,
            CancellationToken.None));
        var audits = await fixture.Db.OrganizationAuditLogs.OrderBy(item => item.OccurredAtUtc).ToArrayAsync();
        Assert.Equal(3, audits.Length);
        Assert.Equal(
            ["VietsubProjectCreated", "VietsubProjectRenamed", "VietsubProjectArchived"],
            audits.Select(item => item.EventType));
        Assert.All(audits, audit =>
        {
            Assert.DoesNotContain("subtitle", audit.DataJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("path", audit.DataJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Viewer_CanReadOwnedMetadata_ButCannotCreateOrRename()
    {
        await using var fixture = CreateFixture(OrganizationMemberRoles.Viewer);
        fixture.Db.Projects.Add(new TOOL_SERVER.Vietsub.Domain.VietsubProject
        {
            ProjectId = Guid.NewGuid(),
            OrganizationId = fixture.OrganizationId,
            CreatedByUserId = fixture.Context.UserId,
            Name = "Được xem",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var projects = await fixture.Service.ListAsync(
            fixture.OrganizationId,
            fixture.Context,
            CancellationToken.None);
        var createError = await Assert.ThrowsAsync<AccountApiException>(() =>
            fixture.Service.CreateAsync(
                new CreateVietsubProjectRequest(
                    Guid.NewGuid(),
                    fixture.OrganizationId,
                    "Không được tạo"),
                fixture.Context,
                CancellationToken.None));
        var renameError = await Assert.ThrowsAsync<AccountApiException>(() =>
            fixture.Service.RenameAsync(
                projects[0].ProjectId,
                new RenameVietsubProjectRequest(fixture.OrganizationId, "Không được đổi"),
                fixture.Context,
                CancellationToken.None));

        Assert.Single(projects);
        Assert.Equal("vietsub_write_denied", createError.Code);
        Assert.Equal("vietsub_write_denied", renameError.Code);
        Assert.Empty(fixture.Db.OrganizationAuditLogs);
    }

    [Fact]
    public async Task ListAndGet_HideCrossUserCrossOrganizationAndArchivedProjects()
    {
        await using var fixture = CreateFixture(OrganizationMemberRoles.Member);
        var ownedId = Guid.NewGuid();
        fixture.Db.Projects.AddRange(
            Project(ownedId, fixture.OrganizationId, fixture.Context.UserId, "Owned"),
            Project(Guid.NewGuid(), fixture.OrganizationId, "other-user", "Other user"),
            Project(Guid.NewGuid(), Guid.NewGuid(), fixture.Context.UserId, "Other org"),
            Project(Guid.NewGuid(), fixture.OrganizationId, fixture.Context.UserId, "Archived", archived: true));
        await fixture.Db.SaveChangesAsync();

        var projects = await fixture.Service.ListAsync(
            fixture.OrganizationId,
            fixture.Context,
            CancellationToken.None);
        var crossUser = await Assert.ThrowsAsync<AccountApiException>(() =>
            fixture.Service.GetAsync(
                fixture.Db.Projects.Single(item => item.Name == "Other user").ProjectId,
                fixture.OrganizationId,
                fixture.Context,
                CancellationToken.None));
        var archived = await Assert.ThrowsAsync<AccountApiException>(() =>
            fixture.Service.GetAsync(
                fixture.Db.Projects.Single(item => item.Name == "Archived").ProjectId,
                fixture.OrganizationId,
                fixture.Context,
                CancellationToken.None));

        var project = Assert.Single(projects);
        Assert.Equal(ownedId, project.ProjectId);
        Assert.Equal("vietsub_project_not_found", crossUser.Code);
        Assert.Equal("vietsub_project_not_found", archived.Code);
    }

    [Fact]
    public async Task Create_IsIdempotentForSameMetadata_AndConflictsForDifferentPayload()
    {
        await using var fixture = CreateFixture(OrganizationMemberRoles.Member);
        var request = new CreateVietsubProjectRequest(
            Guid.NewGuid(),
            fixture.OrganizationId,
            "Idempotent",
            "zh",
            "vi");

        var first = await fixture.Service.CreateAsync(request, fixture.Context, CancellationToken.None);
        var replay = await fixture.Service.CreateAsync(request, fixture.Context, CancellationToken.None);
        var conflict = await Assert.ThrowsAsync<AccountApiException>(() =>
            fixture.Service.CreateAsync(
                request with { Name = "Payload khác" },
                fixture.Context,
                CancellationToken.None));

        Assert.Equal(first, replay);
        Assert.Equal("vietsub_project_id_conflict", conflict.Code);
        Assert.Single(fixture.Db.Projects);
        Assert.Single(fixture.Db.OrganizationAuditLogs);
    }

    [Fact]
    public async Task EveryOperation_ForwardsUserDeviceAndOrganizationToTrustedAccessService()
    {
        await using var fixture = CreateFixture(OrganizationMemberRoles.Member);
        await fixture.Service.ListAsync(fixture.OrganizationId, fixture.Context, CancellationToken.None);

        var call = Assert.Single(fixture.Access.Calls);
        Assert.Equal(fixture.Context.UserId, call.UserId);
        Assert.Equal(fixture.Context.DeviceId, call.DeviceId);
        Assert.Equal(fixture.OrganizationId, call.OrganizationId);
        Assert.Null(call.ProjectId);
    }

    [Fact]
    public void Migration_IsIdempotentMetadataOnlyAndDoesNotGrantDesktopSchemaAccess()
    {
        var migration = ReadRepositoryFile("database", "VideoFactory.4.1.0.VietsubProjectRegistry.sql");
        var leastPrivilege = ReadRepositoryFile("database", "VideoFactory.DesktopLeastPrivilege.sql");
        var controller = ReadRepositoryFile("TOOL-SERVER", "Controllers", "VietsubProjectsController.cs");
        var client = ReadRepositoryFile(
            "TOOL-LOCAL", "Vietsub", "Api", "VietsubProjectRegistryClient.cs");

        Assert.Contains("IF SCHEMA_ID(N'vs') IS NULL", migration);
        Assert.Contains("IF OBJECT_ID(N'[vs].[Projects]', N'U') IS NULL", migration);
        Assert.Contains("IF NOT EXISTS", migration);
        Assert.Contains("SET XACT_ABORT ON", migration);
        Assert.Contains("BEGIN TRY", migration);
        Assert.Contains("BEGIN TRANSACTION", migration);
        Assert.Contains("[OrganizationId]", migration);
        Assert.Contains("[CreatedByUserId]", migration);
        Assert.DoesNotContain("SubtitleText", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LocalPath", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GRANT", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[vs]", leastPrivilege, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[Authorize]", controller);
        Assert.Contains("AuthClaimTypes.DeviceId", controller);
        Assert.Contains("api/vietsub/projects", controller);
        Assert.Contains("GetValidAccessTokenAsync", client);
        Assert.Contains("EnsureAccessAsync", client);
        Assert.DoesNotContain("ApiKey", client, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SubtitleText", client, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceFixture CreateFixture(string role)
    {
        var options = new DbContextOptionsBuilder<VietsubDbContext>()
            .UseInMemoryDatabase($"vietsub-registry-{Guid.NewGuid():N}")
            .Options;
        var db = new VietsubDbContext(options);
        var organizationId = Guid.NewGuid();
        var access = new FakeGenerationAccessService(role);
        var service = new VietsubProjectService(db, access, TimeProvider.System);
        var context = new VietsubProjectRequestContext(
            "user-1",
            Guid.NewGuid(),
            "127.0.0.1",
            "test-agent",
            "test-correlation");
        return new ServiceFixture(db, access, service, organizationId, context);
    }

    private static TOOL_SERVER.Vietsub.Domain.VietsubProject Project(
        Guid projectId,
        Guid organizationId,
        string userId,
        string name,
        bool archived = false) =>
        new()
        {
            ProjectId = projectId,
            OrganizationId = organizationId,
            CreatedByUserId = userId,
            Name = name,
            IsArchived = archived,
            ArchivedAtUtc = archived ? DateTime.UtcNow : null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Cannot locate repository file: {Path.Combine(relativeParts)}");
    }

    private sealed class FakeGenerationAccessService(string role) : IGenerationAccessService
    {
        public List<AccessCall> Calls { get; } = [];

        public Task<GenerationAccessContext> RequireAsync(
            string userId,
            Guid deviceId,
            Guid? requestedOrganizationId,
            Guid? projectId,
            CancellationToken cancellationToken)
        {
            Calls.Add(new AccessCall(userId, deviceId, requestedOrganizationId, projectId));
            return Task.FromResult(new GenerationAccessContext(
                requestedOrganizationId ?? throw new InvalidOperationException(),
                "Test organization",
                role,
                null));
        }
    }

    private sealed record AccessCall(
        string UserId,
        Guid DeviceId,
        Guid? OrganizationId,
        Guid? ProjectId);

    private sealed record ServiceFixture(
        VietsubDbContext Db,
        FakeGenerationAccessService Access,
        VietsubProjectService Service,
        Guid OrganizationId,
        VietsubProjectRequestContext Context) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}

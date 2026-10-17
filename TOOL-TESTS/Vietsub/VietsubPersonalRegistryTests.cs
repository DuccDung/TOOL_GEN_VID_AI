using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Vietsub;
using TOOL_SERVER.Vietsub.Data;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubPersonalRegistryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersonalRegistryUsesNullableForeignKeyAndCommitsProjectWithAccountAuditAtomically(bool failAudit)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new VietsubDbContext(new DbContextOptionsBuilder<VietsubDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE Organizations (OrganizationId TEXT PRIMARY KEY);
            CREATE TABLE Users (Id TEXT PRIMARY KEY);
            INSERT INTO Users VALUES ('owner');
            CREATE TABLE Projects (
                ProjectId TEXT PRIMARY KEY, OrganizationId TEXT NULL REFERENCES Organizations(OrganizationId),
                CreatedByUserId TEXT NOT NULL REFERENCES Users(Id), Name TEXT NOT NULL, Status TEXT NOT NULL,
                SourceLanguageCode TEXT NOT NULL, TargetLanguageCode TEXT NOT NULL,
                IsArchived INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL, ArchivedAtUtc TEXT,
                RowVersion BLOB NOT NULL DEFAULT X'01');
            CREATE TABLE AccountAuditLogs (
                AccountAuditLogId INTEGER PRIMARY KEY AUTOINCREMENT, UserId TEXT, EventType TEXT NOT NULL,
                Succeeded INTEGER NOT NULL, IpAddress TEXT, UserAgent TEXT, CorrelationId TEXT, DetailsJson TEXT, OccurredAtUtc TEXT NOT NULL);
            """);
        if (failAudit) await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER FailAudit BEFORE INSERT ON AccountAuditLogs BEGIN SELECT RAISE(ABORT, 'audit unavailable'); END;
            """);
        var service = new VietsubProjectService(db, new PersonalAccess(), TimeProvider.System);
        var context = new VietsubProjectRequestContext("owner", Guid.NewGuid(), null, null, "test");
        var request = new CreateVietsubProjectRequest(Guid.NewGuid(), Guid.Empty, "Personal Local");
        if (failAudit)
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(request, context, default));
            db.ChangeTracker.Clear();
            Assert.False(await db.Projects.AnyAsync());
            Assert.False(await db.AccountAuditLogs.AnyAsync());
        }
        else
        {
            await service.CreateAsync(request, context, default);
            db.ChangeTracker.Clear();
            Assert.Null((await db.Projects.SingleAsync()).OrganizationId);
            Assert.Equal("owner", (await db.AccountAuditLogs.SingleAsync()).UserId);
            Assert.Single(await service.ListAsync(Guid.Empty, context, default));
            Assert.Empty(await service.ListAsync(Guid.Empty, context with { UserId = "other" }, default));
            db.Projects.Add(new() { ProjectId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), CreatedByUserId = "owner", Name = "Unknown org" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    private sealed class PersonalAccess : IGenerationAccessService
    {
        public Task RequirePersonalVietsubAccessAsync(string userId, Guid deviceId, CancellationToken ct) => Task.CompletedTask;
        public Task<GenerationAccessContext> RequireAsync(string userId, Guid deviceId, Guid? org, Guid? project, CancellationToken ct) =>
            throw new InvalidOperationException("Personal registry must not request organization access");
    }
}

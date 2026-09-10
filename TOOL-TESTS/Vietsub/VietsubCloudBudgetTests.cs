using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Organizations;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubCloudBudgetTests
{
    [Fact]
    public async Task VideoAndVietsubShareBudget_ButKeepDistinctProjectsAndIdempotentLedger()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AiGovernanceDbContext(new DbContextOptionsBuilder<AiGovernanceDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript()
            .Replace("DEFAULT (NEWSEQUENTIALID())", "")
            .Replace("\"RowVersion\" BLOB NOT NULL", "\"RowVersion\" BLOB NOT NULL DEFAULT X''"));
        var org = Guid.NewGuid(); var video = Guid.NewGuid(); var vietsub = Guid.NewGuid(); var cloudRequest = Guid.NewGuid();
        db.Organizations.Add(new() { OrganizationId = org, Code = "test", Name = "test", MonthlyBudgetLimit = 10, CreatedByUserId = "owner" });
        db.OrganizationMembers.Add(new() { OrganizationId = org, UserId = "owner", Role = "Owner" }); await db.SaveChangesAsync();
        var service = new AiBudgetService(db, TimeProvider.System);
        await service.ReserveAsync(org, "owner", video, Guid.NewGuid(), "video-1", "openai", "fixture", 3m, default);
        var cloud = await service.ReserveVietsubAsync(org, "owner", vietsub, cloudRequest, "cloud-1", "openai", "fixture", 4m, default);
        var replay = await service.ReserveVietsubAsync(org, "owner", vietsub, cloudRequest, "cloud-1", "openai", "fixture", 4m, default);
        Assert.Equal(cloud.ReservationId, replay.ReservationId);
        Assert.Equal(7m, (await service.GetSnapshotAsync(org, default)).ReservedCost);
        Assert.Equal("idempotency_key_conflict", (await Assert.ThrowsAsync<AccountApiException>(() =>
            service.ReserveAsync(org, "owner", vietsub, cloudRequest, "cloud-1", "openai", "fixture", 4m, default))).Code);
        // A different worker settles while the original scope still tracks Reserved and the old budget period.
        await using (var otherDb = new AiGovernanceDbContext(new DbContextOptionsBuilder<AiGovernanceDbContext>().UseSqlite(connection).Options))
            await new AiBudgetService(otherDb, TimeProvider.System).SettleAsync(cloud.ReservationId, 2m, null,
                new { inputTokens = 10 }, new[] { new { unitPrice = 1 } }, default);
        await service.SettleAsync(cloud.ReservationId, 2m, null, null, null, default);
        var balance = await service.GetSnapshotAsync(org, default);
        Assert.Equal(3m, balance.ReservedCost); Assert.Equal(2m, balance.ActualCost); Assert.Equal(5m, balance.RemainingBudget);
        var entries = await db.AiUsageLedger.Where(x => x.ProviderRequestId == cloudRequest).ToArrayAsync();
        Assert.Equal(3, entries.Length); Assert.Single(entries, x => x.EntryKind == UsageLedgerEntryKinds.Actual);
        Assert.All(entries, x => { Assert.Null(x.ProjectId); Assert.Equal(vietsub, x.VietsubProjectId); });
        Assert.Equal("organization_budget_exceeded", (await Assert.ThrowsAsync<AccountApiException>(() =>
            service.ReserveVietsubAsync(org, "owner", vietsub, Guid.NewGuid(), "cloud-too-much", "openai", "fixture", 6m, default))).Code);
    }
}

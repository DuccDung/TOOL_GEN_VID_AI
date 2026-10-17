using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Updates;
using Xunit;

namespace TOOL_TESTS.Updates;

public sealed class DesktopInspectionScratchCleanerTests
{
    [Fact]
    public async Task Cleanup_removes_only_expired_owned_directories_and_preserves_active_leases()
    {
        using var f = new Fixture();
        var activeId = Guid.NewGuid();
        var active = f.DirectoryFor(activeId, 2, old: true);
        var olderGeneration = f.DirectoryFor(activeId, 1, old: true);
        var expired = f.DirectoryFor(Guid.NewGuid(), 1, old: true);
        var recent = f.DirectoryFor(Guid.NewGuid(), 1, old: false);
        var unrelated = Path.Combine(f.Root, "user-files"); Directory.CreateDirectory(unrelated);
        Directory.SetCreationTimeUtc(unrelated, f.Now.AddDays(-1));
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO DesktopReleasePreparations(PreparationId,Generation,LeaseOwner,LeaseExpiresAtUtc) VALUES({activeId},{2},{Guid.NewGuid()},{f.Now.AddMinutes(2)})");
        var removed = await DesktopReleaseInspectionScratchCleaner.CleanupAsync(f.Db, f.Root, f.Now, TimeSpan.FromHours(1), default);
        Assert.Equal(2, removed);
        Assert.True(Directory.Exists(active)); Assert.True(Directory.Exists(recent)); Assert.True(Directory.Exists(unrelated));
        Assert.False(Directory.Exists(olderGeneration)); Assert.False(Directory.Exists(expired));
    }

    [Fact]
    public async Task A_failed_lease_read_does_not_treat_unknown_lease_state_as_expired()
    {
        using var f = new Fixture(); var directory = f.DirectoryFor(Guid.NewGuid(), 1, old: true);
        await f.Db.Database.ExecuteSqlRawAsync("DROP TABLE DesktopReleasePreparations");
        await Assert.ThrowsAsync<SqliteException>(() => DesktopReleaseInspectionScratchCleaner.CleanupAsync(f.Db, f.Root, f.Now, TimeSpan.FromHours(1), default));
        Assert.True(Directory.Exists(directory));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "VM-InspectionClean-" + Guid.NewGuid().ToString("N")));
        public DateTime Now { get; } = DateTime.UtcNow;
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public AccountDbContext Db { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root); _connection.Open();
            Db = new AccountDbContext(new DbContextOptionsBuilder<AccountDbContext>().UseSqlite(_connection).Options);
            Db.Database.ExecuteSqlRaw("CREATE TABLE DesktopReleasePreparations(PreparationId TEXT PRIMARY KEY,Generation INTEGER NOT NULL,LeaseOwner TEXT,LeaseExpiresAtUtc TEXT)");
        }
        public string DirectoryFor(Guid id, int generation, bool old)
        {
            var directory = Path.Combine(Root, $"{id:N}-{generation}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "package.zip.part"), "fixture");
            if (old) Directory.SetCreationTimeUtc(directory, Now.AddDays(-1));
            return directory;
        }
        public void Dispose()
        {
            Db.Dispose(); _connection.Dispose();
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Root.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test root.");
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}

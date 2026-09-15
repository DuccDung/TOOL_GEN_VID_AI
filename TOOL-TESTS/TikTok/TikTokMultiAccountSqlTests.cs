using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.TikTok.Data;

namespace TOOL_TESTS.TikTok;

public sealed partial class TikTokServiceTests
{
    [TikTokLocalSqlFact]
    public async Task MultiAccount_SqlServer_RehearsesMigrationAndDistributedIdempotency()
    {
        // No configurable connection string: this test owns a brand-new private LocalDB instance.
        var instance = "VideoMakerTikTokTest_" + Guid.NewGuid().ToString("N");
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), instance));
        Directory.CreateDirectory(root);
        var localDb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft SQL Server", "150", "Tools", "Binn", "SqlLocalDB.exe");
        await RunLocalDb(localDb, "create", instance, "-s");
        try
        {
            var masterString = new SqlConnectionStringBuilder { DataSource = $"(localdb)\\{instance}",
                InitialCatalog = "master", IntegratedSecurity = true, TrustServerCertificate = true, Pooling = false }.ConnectionString;
            await using var master = new SqlConnection(masterString);
            await master.OpenAsync();
            // Fixed database name is needed by the actual migration USE statements.
            await Sql(master, $"CREATE DATABASE [VideoFactory] ON (NAME=N'VideoFactory', FILENAME=N'{Literal(Path.Combine(root, "data.mdf"))}') LOG ON (NAME=N'VideoFactory_log', FILENAME=N'{Literal(Path.Combine(root, "log.ldf"))}');");
            var connectionString = new SqlConnectionStringBuilder(masterString) { InitialCatalog = "VideoFactory" }.ConnectionString;
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await Sql(connection, """
                CREATE SCHEMA auth;
                GO
                CREATE SCHEMA ai;
                GO
                CREATE TABLE dbo.AspNetUsers (Id nvarchar(450) NOT NULL PRIMARY KEY);
                CREATE TABLE auth.RegisteredDevices (DeviceId uniqueidentifier NOT NULL PRIMARY KEY);
                CREATE TABLE auth.AccountAuditLogs (Id int NOT NULL PRIMARY KEY);
                CREATE TABLE ai.SchemaVersions (Version varchar(100) NOT NULL PRIMARY KEY, Description nvarchar(1000));
                INSERT dbo.AspNetUsers VALUES (N'user-a');
                """);
            await Sql(connection, Migration("VideoFactory.4.1.6.TikTokPublishing.sql"));
            await Sql(connection, Migration("VideoFactory.4.1.7.TikTokAdminCredentials.sql"));
            var id = Guid.NewGuid();
            var oldJobId = Guid.NewGuid();
            await Sql(connection, $"""
                INSERT social.TikTokConnections(TikTokConnectionId,UserId,OpenId,Scopes,ProtectedAccessToken,ProtectedRefreshToken,AccessTokenExpiresAtUtc,RefreshTokenExpiresAtUtc,CreatedAtUtc,UpdatedAtUtc)
                VALUES ('{id}',N'user-a','open-a','video.publish',N'legacy-protected-access',N'legacy-protected-refresh',DATEADD(hour,1,SYSUTCDATETIME()),DATEADD(day,30,SYSUTCDATETIME()),SYSUTCDATETIME(),SYSUTCDATETIME());
                INSERT social.TikTokPublishJobs(TikTokPublishJobId,TikTokConnectionId,UserId,ClientRequestId,TikTokPublishId,UploadUrlExpiresAtUtc,VideoSizeBytes,ChunkSizeBytes,TotalChunkCount,Status,CreatedAtUtc,UpdatedAtUtc)
                VALUES ('{oldJobId}','{id}',N'user-a',NEWID(),'historical-publish',SYSUTCDATETIME(),128,128,1,'PUBLISH_COMPLETE',SYSUTCDATETIME(),SYSUTCDATETIME());
                """);
            var backup = Literal(Path.Combine(root, "before-4.1.8.bak"));
            await Sql(master, $"BACKUP DATABASE [VideoFactory] TO DISK=N'{backup}' WITH INIT, CHECKSUM; RESTORE VERIFYONLY FROM DISK=N'{backup}' WITH CHECKSUM;");
            await Sql(connection, Migration("VideoFactory.4.1.8.TikTokMultiAccount.sql"));
            await Sql(connection, Migration("VideoFactory.4.1.8.TikTokMultiAccount.sql"));
            var options = new DbContextOptionsBuilder<TikTokDbContext>().UseSqlServer(connectionString).Options;
            await using var db = new TikTokDbContext(options);
            var legacy = await db.Connections.SingleAsync();
            Assert.Equal(id, legacy.TikTokConnectionId); Assert.Null(legacy.AppKeyHash);
            Assert.Equal("legacy-protected-access", legacy.ProtectedAccessToken);
            Assert.Equal("legacy-protected-refresh", legacy.ProtectedRefreshToken);
            var historical = await db.PublishJobs.SingleAsync();
            Assert.Equal(oldJobId, historical.TikTokPublishJobId); Assert.Equal(id, historical.TikTokConnectionId);
            Assert.Null(historical.CreatorUsernameSnapshot);
            var b = Account("open-b"); db.Connections.Add(b); await db.SaveChangesAsync();
            var duplicate = Account(); db.Connections.Add(duplicate);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.Entry(duplicate).State = EntityState.Detached;

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var api = new FakeTikTokApiClient { BeforeInitialize = async () => {
                await using var observer = new TikTokDbContext(options);
                Assert.Equal("Initializing", (await observer.PublishAttempts.SingleAsync()).Status);
                entered.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            } };
            await using var other = new TikTokDbContext(options);
            var request = PublishRequest(id);
            var first = CreateService(db, api, multiAccount: true).InitializePublishAsync("user-a", request, default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var second = CreateService(other, api, multiAccount: true).InitializePublishAsync("user-a", request, default);
            release.SetResult();
            var results = await Task.WhenAll(first, second);
            Assert.Equal(results[0].PublishJobId, results[1].PublishJobId); Assert.Equal(1, api.InitializeCalls);
            Assert.Equal(2, await db.PublishJobs.CountAsync()); Assert.Equal(1, await db.PublishAttempts.CountAsync());
            // A polling claim updates RowVersion, and another worker cannot claim before expiry.
            var jobId = results[0].PublishJobId;
            var now = DateTime.UtcNow;
            Assert.Equal(1, await db.PublishJobs.Where(j => j.TikTokPublishJobId == jobId && j.NextPollAtUtc == null)
                .ExecuteUpdateAsync(u => u.SetProperty(j => j.NextPollAtUtc, now.AddMinutes(5))));
            Assert.Equal(0, await other.PublishJobs.Where(j => j.TikTokPublishJobId == jobId && (j.NextPollAtUtc == null || j.NextPollAtUtc <= now))
                .ExecuteUpdateAsync(u => u.SetProperty(j => j.NextPollAtUtc, now.AddMinutes(5))));
        }
        finally
        {
            await RunLocalDb(localDb, "stop", instance, "-k");
            await RunLocalDb(localDb, "delete", instance);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Assert.StartsWith(tempRoot + "VideoMakerTikTokTest_", root, StringComparison.OrdinalIgnoreCase);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Literal(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static async Task Sql(SqlConnection connection, string sql)
    {
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            await using var command = new SqlCommand(batch, connection) { CommandTimeout = 60 };
            await command.ExecuteNonQueryAsync();
        }
    }

    private static string Migration(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "database", file))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "database", file));
    }

    private static async Task RunLocalDb(string exe, params string[] arguments)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}

internal sealed class TikTokLocalSqlFactAttribute : FactAttribute
{
    public TikTokLocalSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VIDEOMAKER_RUN_TIKTOK_SQL_TESTS") != "1")
            Skip = "Opt in with VIDEOMAKER_RUN_TIKTOK_SQL_TESTS=1; uses a private SQL Server 2019 LocalDB instance and synthetic data only.";
    }
}

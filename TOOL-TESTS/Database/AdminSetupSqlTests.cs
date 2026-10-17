using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Domain.Providers;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Payments;
using TOOL_SERVER.Providers;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_TESTS.Database;

public sealed class AdminSetupLocalDbFactAttribute : FactAttribute
{
    public AdminSetupLocalDbFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VIDEOMAKER_ADMIN_SETUP_LOCALDB") != "1")
            Skip = "Opt-in: set VIDEOMAKER_ADMIN_SETUP_LOCALDB=1. Creates/deletes only a private LocalDB instance; no application DB is used.";
    }
}

public sealed class AdminSetupSqlTests
{
    [AdminSetupLocalDbFact]
    public async Task ConcurrentPublishReplaysOnePlanAndOneMappingWithoutSeatReservation()
    {
        await using var f = await SqlFixture.CreateAsync();
        var d = await f.PrepareAsync();
        await using var checkDb = f.Db();
        var check = await f.Service(checkDb).ReadinessAsync(d.DraftId, "admin", default);
        var request = new AdminSetupPublishRequest(Guid.NewGuid(), d.Revision, true, check.Fingerprint);
        async Task<AdminSetupResult> Publish()
        { await using var db = f.Db(); return await f.Service(db).PublishAsync(d.DraftId, request, "admin", default); }
        var results = await Task.WhenAll(Publish(), Publish());
        Assert.Single(results, x => x.Operation.Replayed);
        Assert.Equal(results[0].Plan!.LicensePlanId, results[1].Plan!.LicensePlanId);
        await using var verify = f.Db();
        Assert.Single(await verify.LicensePlans.ToListAsync());
        Assert.Single(await verify.LicensePlanOrganizationPools.ToListAsync());
        Assert.Empty(await verify.OrganizationSeatAssignments.ToListAsync());
        Assert.Equal(0, (await verify.OrganizationPoolOrganizations.SingleAsync()).ReservedSeatCount);
    }

    [AdminSetupLocalDbFact]
    public async Task MigrationCanRerunAndDesktopCannotReadDurableDraftsOrReceipts()
    {
        await using var f = await SqlFixture.CreateAsync(); await f.PrepareAsync();
        await using var db = f.Db();
        var connection = (SqlConnection)db.Database.GetDbConnection(); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ai.SchemaVersions WHERE Version = '4.1.14-admin-setup-wizard'";
        Assert.Equal(1, (int)(await command.ExecuteScalarAsync())!);
        foreach (var table in new[] { "AdminSetupDrafts", "AdminSetupOperations" })
        {
            command.CommandText = $"EXECUTE AS USER = 'vmSetupDesktop'; SELECT * FROM auth.[{table}];";
            var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(229, error.Number);
            command.CommandText = "REVERT;"; await command.ExecuteNonQueryAsync();
        }
    }

    [AdminSetupLocalDbFact]
    public async Task SimpleMigrationPreservesSavedDraftsAndReceiptsWithoutAddingPermissions()
    {
        await using var f = await SqlFixture.CreateAsync(simpleMigration: true);
        var draft = await f.PrepareAsync();
        await using var db = f.Db();
        var operationCount = await db.AdminSetupOperations.CountAsync();
        Assert.True(operationCount > 0);

        await f.ApplyMigrationAsync(db, "VideoFactory.4.1.15.AdminSetupWizardSimple.sql");
        var savedDraft = await db.AdminSetupDrafts.AsNoTracking().SingleAsync();
        Assert.Equal(draft.DraftId, savedDraft.DraftId);
        Assert.Equal(draft.Revision, savedDraft.Revision);
        Assert.Equal(draft.State, savedDraft.State);
        Assert.Equal(operationCount, await db.AdminSetupOperations.CountAsync());

        var connection = (SqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ai.SchemaVersions WHERE Version IN ('4.1.14-admin-setup-wizard', '4.1.15-admin-setup-wizard-simple')";
        Assert.Equal(2, (int)(await command.ExecuteScalarAsync())!);
        command.CommandText = "SELECT COUNT(*) FROM sys.database_permissions WHERE grantee_principal_id = DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole')";
        Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
        command.CommandText = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID(N'auth.AdminSetupDrafts'), OBJECT_ID(N'auth.AdminSetupOperations')) AND is_disabled = 0 AND is_not_trusted = 0";
        Assert.Equal(5, (int)(await command.ExecuteScalarAsync())!);
        command.CommandText = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID(N'auth.AdminSetupDrafts'), OBJECT_ID(N'auth.AdminSetupOperations')) AND is_disabled = 0 AND is_not_trusted = 0";
        Assert.Equal(4, (int)(await command.ExecuteScalarAsync())!);
    }

    [AdminSetupLocalDbFact]
    public async Task SimpleMigrationPreservesPermissionsAlreadyAppliedByOriginalMigration()
    {
        await using var f = await SqlFixture.CreateAsync();
        await using var db = f.Db();
        await f.ApplyMigrationAsync(db, "VideoFactory.4.1.15.AdminSetupWizardSimple.sql");
        await f.ApplyMigrationAsync(db, "VideoFactory.4.1.15.AdminSetupWizardSimple.sql");

        var connection = (SqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sys.database_permissions
            WHERE grantee_principal_id = DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole')
              AND major_id IN (OBJECT_ID(N'auth.AdminSetupDrafts'), OBJECT_ID(N'auth.AdminSetupOperations'))
              AND state_desc = 'DENY' AND permission_name IN ('SELECT', 'INSERT', 'UPDATE', 'DELETE')
            """;
        Assert.Equal(8, (int)(await command.ExecuteScalarAsync())!);
    }

    [AdminSetupLocalDbFact]
    public async Task SameOperationDifferentPayloadConflictsAndStaleTabCannotOverwrite()
    {
        await using var f = await SqlFixture.CreateAsync(); var d = await f.PrepareAsync();
        await using var firstDb = f.Db(); var first = f.Service(firstDb);
        var op = new AdminSetupCommand(Guid.NewGuid(), d.Revision);
        await first.ConfigureAsync(d.DraftId, op, "admin", default);
        await using var secondDb = f.Db(); var second = f.Service(secondDb);
        var conflict = await Assert.ThrowsAsync<AccountApiException>(() => second.ConfigureAsync(d.DraftId, op with { Revision = op.Revision + 1 }, "admin", default));
        Assert.Equal("idempotency_key_conflict", conflict.Code);
        var stale = await Assert.ThrowsAsync<AccountApiException>(() => second.SaveDraftAsync(d.DraftId, new(d.Revision, 2, d.Form with { Capacity = 1 }), "admin", default));
        Assert.Equal("draft_changed", stale.Code);
        Assert.Equal(d.Form.Capacity, (await secondDb.OrganizationPoolOrganizations.AsNoTracking().SingleAsync()).SeatCapacity);
    }

    private sealed class SqlFixture : IAsyncDisposable
    {
        private readonly string instance = "VideoMakerAdminSetup_" + Guid.NewGuid().ToString("N");
        private readonly string database = "admin_setup_" + Guid.NewGuid().ToString("N");
        private string executable = null!, connectionString = null!, master = null!;
        private Guid video;
        public AccountDbContext Db() => new(new DbContextOptionsBuilder<AccountDbContext>().UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()).Options);
        public AdminSetupService Service(AccountDbContext db) => new(db,
            new AdminLicensePlanComposerService(db, new AdminLicenseService(db, TimeProvider.System), new Ready(), new Payment(), TimeProvider.System),
            new Tester(), new Protector(), new Payment(), TimeProvider.System);
        public static async Task<SqlFixture> CreateAsync(bool simpleMigration = false)
        {
            var f = new SqlFixture();
            f.executable = new[] { @"C:\Program Files\Microsoft SQL Server\170\Tools\Binn\SqlLocalDB.exe", @"C:\Program Files\Microsoft SQL Server\150\Tools\Binn\SqlLocalDB.exe" }.FirstOrDefault(File.Exists)
                ?? throw new InvalidOperationException("LocalDB CLI is required for this explicit opt-in test.");
            try
            {
                await f.CliAsync("create", f.instance); await f.CliAsync("start", f.instance);
                var builder = new SqlConnectionStringBuilder { DataSource = $"(localdb)\\{f.instance}", InitialCatalog = "master", IntegratedSecurity = true, Encrypt = SqlConnectionEncryptOption.Optional, ConnectTimeout = 30 };
                f.master = builder.ConnectionString; builder.InitialCatalog = f.database; f.connectionString = builder.ConnectionString;
                await using var db = f.Db(); await db.Database.EnsureCreatedAsync();
                // Empty private database only. Start from the predecessor schema to exercise CREATE and rerun paths.
                await db.Database.ExecuteSqlRawAsync("""
                    DROP TABLE auth.AdminSetupOperations; DROP TABLE auth.AdminSetupDrafts;
                    CREATE TABLE ai.SchemaVersions (SchemaVersionId int IDENTITY PRIMARY KEY, Version varchar(50) UNIQUE NOT NULL, Description nvarchar(1000) NOT NULL);
                    CREATE ROLE VideoMakerDesktopRole; CREATE USER vmSetupDesktop WITHOUT LOGIN;
                    ALTER ROLE VideoMakerDesktopRole ADD MEMBER vmSetupDesktop;
                    """);
                var migrationFile = simpleMigration ? "VideoFactory.4.1.15.AdminSetupWizardSimple.sql" : "VideoFactory.4.1.14.AdminSetupWizard.sql";
                await f.ApplyMigrationAsync(db, migrationFile);
                await f.ApplyMigrationAsync(db, migrationFile);
                db.Users.Add(new ApplicationUser { Id = "admin", UserName = "admin@example.test", Email = "admin@example.test", DisplayName = "SQL fixture" });
                var open = new AiProvider { ProviderId = Guid.NewGuid(), ProviderCode = "openai", DisplayName = "OpenAI", IsEnabled = true, BaseUrl = "https://api.openai.com/v1/" };
                var fal = new AiProvider { ProviderId = Guid.NewGuid(), ProviderCode = "fal", DisplayName = "Fal", IsEnabled = true, BaseUrl = "https://queue.fal.run/" };
                db.Providers.AddRange(open, fal);
                foreach (var (modality, code, provider) in new[] { ("Text", "fixture-text", open), ("Image", "fixture-image", open), ("Video", "fal-ai/veo3.1/fast/image-to-video", fal) })
                {
                    var model = new AiProviderModel { ProviderModelId = Guid.NewGuid(), ProviderId = provider.ProviderId, ModelCode = code, DisplayName = code, Modality = modality, IsEnabled = true, IsDefault = true };
                    db.ProviderModels.Add(model); if (modality == "Video") f.video = model.ProviderModelId;
                    foreach (var type in modality == "Video" ? new[] { "VideoSecond" } : new[] { "InputToken", "OutputToken" })
                        db.CostRates.Add(new() { CostRateId = Guid.NewGuid(), ProviderModelId = model.ProviderModelId, UsageType = type,
                            Unit = modality == "Video" ? "Second" : "MillionTokens", UnitPrice = .1m, CurrencyCode = "USD", IsActive = true,
                            EffectiveFromUtc = DateTime.UtcNow.AddDays(-1), MetadataJson = modality == "Video" ? JsonSerializer.Serialize(new { resolution = "720p", nativeAudio = true, endpointId = code }) : null });
                }
                await db.SaveChangesAsync(); return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        public async Task ApplyMigrationAsync(AccountDbContext db, string migrationFile)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "database", migrationFile))) directory = directory.Parent;
            if (directory is null) throw new InvalidOperationException("Admin setup migration source is required.");
            var migration = await File.ReadAllTextAsync(Path.Combine(directory.FullName, "database", migrationFile));
            await db.Database.ExecuteSqlRawAsync(migration);
        }
        public async Task<AdminSetupDraft> PrepareAsync()
        {
            await using var db = Db(); var service = Service(db);
            var d = await service.CreateDraftAsync(new(Guid.NewGuid()), "admin", default);
            d = await service.SaveDraftAsync(d.DraftId, new(d.Revision, 1, new("SQL fixture", video, 100, 10, 10, "Gói thử", 70000)), "admin", default);
            d = (await service.PrepareOrganizationAsync(d.DraftId, new(Guid.NewGuid(), d.Revision), "admin", default)).Draft;
            foreach (var provider in await db.Providers.ToListAsync())
                db.OrganizationProviderCredentials.Add(new() { OrganizationProviderCredentialId = Guid.NewGuid(), OrganizationId = d.OrganizationId!.Value,
                    ProviderId = provider.ProviderId, Version = 1, Name = "Fixture", EncryptedPayload = "synthetic-only", SecretHint = "••••test", CreatedByUserId = "admin" });
            await db.SaveChangesAsync();
            return (await service.ConfigureAsync(d.DraftId, new(Guid.NewGuid(), d.Revision), "admin", default)).Draft;
        }
        private async Task CliAsync(params string[] args)
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
            if (process.ExitCode != 0) throw new InvalidOperationException("Private LocalDB lifecycle command failed.");
        }
        public async ValueTask DisposeAsync()
        {
            // Only the random instance and database created by this fixture can be stopped/deleted.
            if (!instance.StartsWith("VideoMakerAdminSetup_", StringComparison.Ordinal) || !database.StartsWith("admin_setup_", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test database scope.");
            if (connectionString is not null) { using var pool = new SqlConnection(connectionString); SqlConnection.ClearPool(pool); }
            if (master is not null)
            {
                await using var connection = new SqlConnection(master); await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END";
                await command.ExecuteNonQueryAsync(); SqlConnection.ClearPool(connection);
            }
            if (executable is not null) { await CliAsync("stop", instance); await CliAsync("delete", instance); }
        }
    }
    private sealed class Ready : IOrganizationProvisioningReadinessEvaluator { public Task<OrganizationProvisioningReadiness> EvaluateAsync(Guid id, CancellationToken ct) => Task.FromResult(new OrganizationProvisioningReadiness(true, "Fixture")); }
    private sealed class Tester : IOrganizationProviderCredentialTester { public Task TestAsync(string p, string? u, string k, CancellationToken ct) => throw new InvalidOperationException("SQL tests must not contact providers."); }
    private sealed class Protector : IProviderCredentialProtector { public string Protect(string k) => "synthetic-only"; public string Unprotect(string k) => "synthetic-only"; }
    private sealed class Payment : ISepayPaymentConfiguration { public Task<SepayPaymentOptions> GetOptionsAsync(CancellationToken ct) => Task.FromResult(new SepayPaymentOptions { Enabled = true, ReceiverBankCode = "TEST", ReceiverAccountNumber = "11111111", ReceiverAccountName = "TEST" }); }
}

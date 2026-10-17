using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Controllers;
using TOOL_SERVER.Data;
using TOOL_SERVER.Models;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_TESTS.Updates;

// Real SQLite persistence/CAS with emulated rowversion generation. No production
// SQL, network requests, Setup execution or claim of SQL Server qualification.
public sealed class DesktopDownloadSettingsTests
{
    private const string Url = "https://103.179.190.182:8443/models/VideoMaker%20Setup.exe";
    private const string NextUrl = "https://103.179.190.182:8443/apps/videomaker/VideoMaker-Setup-1.2.1.exe";

    [Fact]
    public void Admin_settings_are_authorized_and_the_service_has_no_download_or_inspection_dependency()
    {
        var controller = typeof(AdminDesktopDownloadSettingsController);
        Assert.Contains(controller.GetCustomAttributes<AuthorizeAttribute>(true), x => x.Roles == "Admin");
        Assert.Empty(controller.GetMethods().SelectMany(x => x.GetCustomAttributes<AllowAnonymousAttribute>(true)));
        Assert.Contains(controller.GetMethods(), x => x.GetCustomAttribute<HttpGetAttribute>() is not null);
        Assert.Contains(controller.GetMethods(), x => x.GetCustomAttribute<HttpPutAttribute>() is not null);
        var parameters = typeof(DesktopDownloadSettingsService).GetConstructors().Single().GetParameters();
        Assert.DoesNotContain(parameters, x => x.ParameterType == typeof(HttpClient) || x.ParameterType == typeof(IHttpClientFactory)
            || x.ParameterType == typeof(IDesktopReleaseArchiveInspector) || x.ParameterType == typeof(IDesktopReleaseStorage));
    }

    [Fact]
    public async Task An_unset_GET_does_not_create_a_setting_or_claim_an_updated_revision()
    {
        using var f = new Fixture();
        var response = await f.Service.GetAsync(default);
        Assert.Null(response.SetupDownloadUrl); Assert.Null(response.Revision); Assert.Null(response.UpdatedAtUtc);
        Assert.Null(await f.Service.GetPublicDownloadUrlAsync(default));
        Assert.Empty(await f.Db.ServerSettings.ToListAsync());
    }

    [Fact]
    public async Task Saving_the_only_URL_persists_across_a_fresh_context_and_keeps_concurrency_metadata_hidden_from_the_value()
    {
        using var f = new Fixture();
        var saved = await f.Service.SaveAsync("admin", new(Url, null), default);
        Assert.Equal(Url, saved.SetupDownloadUrl); Assert.NotNull(saved.Revision);
        Assert.Equal(8, Convert.FromBase64String(saved.Revision!).Length);
        Assert.Equal(f.Clock.GetUtcNow().UtcDateTime, saved.UpdatedAtUtc);
        await using var restarted = f.NewContext();
        var fresh = f.NewService(restarted);
        var read = await fresh.GetAsync(default);
        Assert.Equal(saved, read);
        Assert.Equal(Url, await fresh.GetPublicDownloadUrlAsync(default));
        var row = await restarted.ServerSettings.SingleAsync();
        Assert.Equal("Desktop.SetupDownload", row.SettingKey);
        using var json = JsonDocument.Parse(row.ValueJson!);
        Assert.Equal(Url, json.RootElement.GetProperty("setupDownloadUrl").GetString());
        Assert.Single(json.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Updating_with_the_current_revision_changes_only_the_setting_and_rejects_an_older_editor()
    {
        using var f = new Fixture();
        var first = await f.Service.SaveAsync("admin", new(Url, null), default);
        await using var editorContext = f.NewContext();
        var editor = f.NewService(editorContext);
        var editorSnapshot = await editor.GetAsync(default);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        var changed = await f.Service.SaveAsync("admin", new(NextUrl, first.Revision), default);
        Assert.Equal(NextUrl, changed.SetupDownloadUrl); Assert.NotEqual(first.Revision, changed.Revision);
        Assert.True(changed.UpdatedAtUtc > first.UpdatedAtUtc);
        var error = await Assert.ThrowsAsync<AccountApiException>(() => editor.SaveAsync("admin", new(Url, editorSnapshot.Revision), default));
        Assert.Equal(409, error.StatusCode);
        await using var verify = f.NewContext();
        Assert.Equal(NextUrl, (await f.NewService(verify).GetAsync(default)).SetupDownloadUrl);
        Assert.Single(await verify.ServerSettings.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-base64")]
    [InlineData("AQIDBAUGBwg=")]
    public async Task Existing_settings_require_the_exact_current_revision(string? revision)
    {
        using var f = new Fixture();
        var saved = await f.Service.SaveAsync("admin", new(Url, null), default);
        var error = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.SaveAsync("admin", new(NextUrl, revision), default));
        Assert.Equal(409, error.StatusCode);
        await using var verify = f.NewContext();
        Assert.Equal(saved, await f.NewService(verify).GetAsync(default));
    }

    [Fact]
    public async Task A_database_write_race_returns_conflict_without_overwriting_the_winner()
    {
        using var f = new Fixture();
        var first = await f.Service.SaveAsync("admin", new(Url, null), default);
        await using var loserContext = f.NewContext();
        loserContext.BeforeSettingWrite = async () =>
        {
            await using var winnerContext = f.NewContext();
            await f.NewService(winnerContext).SaveAsync("admin", new(NextUrl, first.Revision), default);
        };
        var loserUrl = "https://103.179.190.182:8443/models/Another-Setup.exe";
        var error = await Assert.ThrowsAsync<AccountApiException>(() => f.NewService(loserContext).SaveAsync("admin", new(loserUrl, first.Revision), default));
        Assert.Equal(409, error.StatusCode);
        await using var verify = f.NewContext();
        Assert.Equal(NextUrl, (await f.NewService(verify).GetAsync(default)).SetupDownloadUrl);
    }

    [Fact]
    public async Task Saving_requires_an_actor_and_cannot_create_a_missing_row_with_a_stale_revision()
    {
        using var f = new Fixture();
        var forbidden = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.SaveAsync("", new(Url, null), default));
        Assert.Equal(403, forbidden.StatusCode);
        var changed = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.SaveAsync("admin", new(Url, "AQIDBAUGBwg="), default));
        Assert.Equal(409, changed.StatusCode);
        Assert.Empty(await f.Db.ServerSettings.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://103.179.190.182:8443/models/Setup.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://outside.invalid/models/Setup.exe")]
    [InlineData("https://103.179.190.182/models/Setup.exe")]
    [InlineData("https://103.179.190.182:8443/models-evil/Setup.exe")]
    [InlineData("https://user:password@103.179.190.182:8443/models/Setup.exe")]
    [InlineData("https://103.179.190.182:8443/models/Setup.exe?token=synthetic")]
    [InlineData("https://103.179.190.182:8443/models/Setup.exe#fragment")]
    [InlineData("https://103.179.190.182:8443/models/Setup.zip")]
    [InlineData("https://103.179.190.182:8443/models/../Setup.exe")]
    [InlineData("https://103.179.190.182:8443/models/%2e%2e/Setup.exe")]
    [InlineData("https://103.179.190.182:8443/models/%252e%252e/Setup.exe")]
    [InlineData("https://103.179.190.182:8443/models/folder%2fSetup.exe")]
    [InlineData("https://103.179.190.182:8443/models/folder%5cSetup.exe")]
    public async Task Unsafe_URLs_are_rejected_without_inserting_a_setting_or_replacing_the_current_value(string url)
    {
        using var f = new Fixture();
        var error = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.SaveAsync("admin", new(url, null), default));
        Assert.Equal(400, error.StatusCode);
        Assert.Empty(await f.Db.ServerSettings.ToListAsync());
        var saved = await f.Service.SaveAsync("admin", new(Url, null), default);
        error = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.SaveAsync("admin", new(url, saved.Revision), default));
        Assert.Equal(400, error.StatusCode);
        await using var verify = f.NewContext();
        Assert.Equal(saved, await f.NewService(verify).GetAsync(default));
    }

    [Fact]
    public async Task The_public_download_returns_the_exact_approved_URL_without_proxying_the_EXE_and_disables_cache()
    {
        using var f = new Fixture();
        await f.Service.SaveAsync("admin", new(Url, null), default);
        var controller = f.Controller();
        var result = Assert.IsType<RedirectResult>(await controller.DownloadLatestSetup());
        Assert.Equal(Url, result.Url); Assert.False(result.Permanent); Assert.False(result.PreserveMethod);
        Assert.Contains("no-store", controller.Response.Headers.CacheControl.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Single(await f.Db.ServerSettings.ToListAsync());
    }

    [Fact]
    public async Task The_public_download_returns_a_friendly_503_when_no_URL_has_been_configured()
    {
        using var f = new Fixture();
        var controller = f.Controller();
        var response = Assert.IsType<ContentResult>(await controller.DownloadLatestSetup());
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", response.ContentType);
        Assert.Equal("Bộ cài Windows chưa có link tải. Vui lòng thử lại sau.", response.Content);
        Assert.Contains("no-store", controller.Response.Headers.CacheControl.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await f.Db.ServerSettings.ToListAsync());
    }

    [Theory]
    [InlineData("Beta", "win-x64")]
    [InlineData("Stable", "linux-x64")]
    [InlineData("Stable", "win-arm64")]
    public async Task Unsupported_public_query_does_not_redirect_or_mutate_settings(string channel, string platform)
    {
        using var f = new Fixture();
        var saved = await f.Service.SaveAsync("admin", new(Url, null), default);
        var result = await f.Controller().DownloadLatestSetup(channel, platform);
        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(saved, await f.Service.GetAsync(default));
        Assert.Single(await f.Db.ServerSettings.ToListAsync());
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("outside")]
    [InlineData("duplicate")]
    [InlineData("oversized")]
    public async Task Unsafe_persisted_data_is_not_redirected_and_keeps_a_revision_for_admin_repair(string mutation)
    {
        using var f = new Fixture();
        await f.Service.SaveAsync("admin", new(Url, null), default);
        var row = await f.Db.ServerSettings.SingleAsync();
        row.ValueJson = mutation switch
        {
            "malformed" => "{not-json",
            "outside" => "{\"setupDownloadUrl\":\"https://outside.invalid/untrusted.exe\"}",
            "duplicate" => JsonSerializer.Serialize(new { setupDownloadUrl = Url })[..^1] + ",\"SetupDownloadUrl\":\"https://outside.invalid/untrusted.exe\"}",
            _ => new string('x', 16385)
        };
        await f.Db.SaveChangesAsync();
        var read = await f.Service.GetAsync();
        Assert.Null(read.SetupDownloadUrl); Assert.NotNull(read.Revision); Assert.NotNull(read.UpdatedAtUtc);
        Assert.Null(await f.Service.GetPublicDownloadUrlAsync());
        var controller = f.Controller();
        var response = Assert.IsType<ContentResult>(await controller.DownloadLatestSetup());
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("outside.invalid", response.Content!, StringComparison.Ordinal);
        Assert.Equal("Bộ cài Windows chưa có link tải. Vui lòng thử lại sau.", response.Content);
        Assert.Contains("no-store", controller.Response.Headers.CacheControl.ToString(), StringComparison.OrdinalIgnoreCase);
        var repaired = await f.Service.SaveAsync("admin", new(NextUrl, read.Revision));
        Assert.Equal(NextUrl, repaired.SetupDownloadUrl);
        Assert.Single(await f.Db.ServerSettings.ToListAsync());
    }

    [Fact]
    public async Task Removing_the_saved_source_from_the_allowlist_disables_the_public_redirect_without_erasing_admin_revision()
    {
        using var f = new Fixture();
        var saved = await f.Service.SaveAsync("admin", new(Url, null), default);
        var changedSettings = new DesktopReleaseOptions { AllowedExternalPrefixes = [DesktopArtifactDownloadPolicy.ApplicationPrefix] };
        var restricted = new DesktopDownloadSettingsService(f.Db, Options.Create(changedSettings), f.Clock);
        var current = await restricted.GetAsync();
        Assert.Null(current.SetupDownloadUrl); Assert.Equal(saved.Revision, current.Revision);
        var controller = new LauncherDistributionController(null!, null!, downloadSettings: restricted)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var response = Assert.IsType<ContentResult>(await controller.DownloadLatestSetup());
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal("Bộ cài Windows chưa có link tải. Vui lòng thử lại sau.", response.Content);
        Assert.Contains(Url, (await f.Db.ServerSettings.SingleAsync()).ValueJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_customer_facing_Setup_link_does_not_register_activate_or_authorize_a_direct_release()
    {
        using var f = new Fixture();
        await f.Service.SaveAsync("admin", new(Url, null), default);
        var options = new DbContextOptionsBuilder<AccountDbContext>().UseSqlite(f.ConnectionString).Options;
        await using var account = new AccountDbContext(options);
        await account.Database.ExecuteSqlRawAsync(CatalogSchema);
        var bootstrapper = new DesktopBootstrapperService(account, null!, null!, Options.Create(f.Settings), f.Clock);
        Assert.Null(await bootstrapper.ActiveAsync(null, default));
        Assert.Empty(await account.DesktopBootstrappers.ToListAsync());
        var catalog = new DesktopDirectCatalogService(account, bootstrapper, Options.Create(f.Settings), f.Clock);
        var package = DesktopArtifactDownloadPolicy.ModelHostPackagePrefix + "VideoMaker-1.0.2-3-win-x64-" + new string('a', 64) + ".zip";
        var draft = await catalog.CreateAsync("admin", new("1.0.2", package, Guid.NewGuid()), default);
        Assert.False(draft.CanPublish); Assert.Null(draft.Bootstrapper); Assert.False(draft.IsActive);
        var error = await Assert.ThrowsAsync<AccountApiException>(() => catalog.PublishAsync(draft.ReleaseId, draft.Revision, default));
        Assert.Equal(409, error.StatusCode);
        Assert.Empty(await account.DesktopBootstrappers.ToListAsync());
        Assert.Equal(Url, await f.Service.GetPublicDownloadUrlAsync(default));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-download-setting-" + Guid.NewGuid().ToString("N"));
        private readonly DbContextOptions<VideoFactoryDbContext> _options;
        public string ConnectionString { get; }
        public TestContext Db { get; }
        public Clock Clock { get; } = new();
        public DesktopReleaseOptions Settings { get; } = new()
        {
            PublicationMode = "DirectCatalog", SharedBootstrapperEnabled = true, ApiBaseUrl = "https://metadata.invalid/",
            Profile = "Production", AllowedExternalPrefixes = DesktopArtifactDownloadPolicy.ApprovedExternalPrefixes.ToArray()
        };
        public DesktopDownloadSettingsService Service { get; }
        public Fixture()
        {
            Directory.CreateDirectory(_root);
            ConnectionString = $"Data Source={Path.Combine(_root, "settings.db")};Pooling=False";
            _options = new DbContextOptionsBuilder<VideoFactoryDbContext>().UseSqlite(ConnectionString).Options;
            Db = NewContext();
            Db.Database.ExecuteSqlRaw("""
                CREATE TABLE ServerSettings(ServerSettingId TEXT PRIMARY KEY,SettingKey TEXT NOT NULL UNIQUE,ValueJson TEXT,Description TEXT,
                  UpdatedAtUtc TEXT NOT NULL,RowVersion BLOB NOT NULL);
                """);
            Service = NewService(Db);
        }
        public TestContext NewContext() => new(_options);
        public DesktopDownloadSettingsService NewService(VideoFactoryDbContext context) => new(context, Options.Create(Settings), Clock);
        public LauncherDistributionController Controller() => new(null!, null!, downloadSettings: Service)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        public void Dispose()
        {
            Db.Dispose();
            if (!Path.GetFullPath(_root).StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test root.");
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 17, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class TestContext(DbContextOptions<VideoFactoryDbContext> options) : VideoFactoryDbContext(options)
    {
        public Func<Task>? BeforeSettingWrite { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ServerSetting>().Property(x => x.RowVersion).ValueGeneratedNever().IsConcurrencyToken();
        }
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var beforeWrite = BeforeSettingWrite;
            BeforeSettingWrite = null;
            if (beforeWrite is not null) await beforeWrite();
            // SQLite does not implement SQL Server's timestamp. Regenerate only the
            // changed row's current value; retain its original concurrency token so
            // EF still uses a real conditional UPDATE and detects a stale editor.
            foreach (var entry in ChangeTracker.Entries<ServerSetting>().Where(x => x.State is EntityState.Added or EntityState.Modified))
                entry.Entity.RowVersion = RandomNumberGenerator.GetBytes(8);
            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    private const string CatalogSchema = """
        CREATE TABLE DesktopBootstrappers(BootstrapperId TEXT PRIMARY KEY,Version TEXT NOT NULL,Profile TEXT NOT NULL,Channel TEXT NOT NULL,Platform TEXT NOT NULL,
          ServerBaseUrl TEXT NOT NULL,ModelBundleId TEXT NOT NULL,DistributionProtocol INTEGER NOT NULL,FileName TEXT NOT NULL,RelativePath TEXT NOT NULL,
          SizeBytes INTEGER NOT NULL,Sha256 TEXT NOT NULL,EmbeddedMetadataJson TEXT NOT NULL,IsActive INTEGER NOT NULL,IsRevoked INTEGER NOT NULL,
          Revision TEXT NOT NULL,CreatedByUserId TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,ActivatedAtUtc TEXT);
        CREATE TABLE DesktopDirectReleases(DirectReleaseId TEXT PRIMARY KEY,Version TEXT NOT NULL,DownloadUrl TEXT NOT NULL,FileName TEXT NOT NULL,
          ExpectedSha256 TEXT NOT NULL,DeclaredBuildNumber INTEGER NOT NULL,Profile TEXT NOT NULL,Channel TEXT NOT NULL,Platform TEXT NOT NULL,
          CatalogProtocol INTEGER NOT NULL,ApplicationDistributionProtocol INTEGER NOT NULL,ModelBundleId TEXT NOT NULL,MinimumBootstrapperVersion TEXT NOT NULL,
          ReleaseNotes TEXT,IsMandatory INTEGER NOT NULL,IsActive INTEGER NOT NULL,Revision TEXT NOT NULL,ActorUserId TEXT NOT NULL,ClientRequestId TEXT NOT NULL,
          CreateRequestFingerprint TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,UpdatedAtUtc TEXT NOT NULL,PublishedAtUtc TEXT,FirstPublishedAtUtc TEXT);
        """;
}

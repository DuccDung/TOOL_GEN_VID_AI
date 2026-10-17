using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Controllers;
using TOOL_SERVER.Data;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;
using TOOL_SHARED.Distribution.StaticModels;

namespace TOOL_TESTS.Updates;

// SQLite exercises durable catalog/CAS operations. There is intentionally no
// DesktopBootstrappers table or Setup executable store in this fixture.
public sealed class DesktopStandaloneCatalogServerTests
{
    [Fact]
    public void Administrative_writes_still_require_Admin_and_do_not_depend_on_a_Setup_registry_or_network()
    {
        Assert.Contains(typeof(AdminDesktopStandaloneReleasesController).GetCustomAttributes<AuthorizeAttribute>(), x => x.Roles == "Admin");
        Assert.Empty(typeof(AdminDesktopStandaloneReleasesController).GetMethods().SelectMany(x => x.GetCustomAttributes<AllowAnonymousAttribute>()));
        Assert.Contains(typeof(DesktopStandaloneUpdatesController).GetCustomAttributes<AuthorizeAttribute>(), _ => true);
        Assert.DoesNotContain(typeof(DesktopStandaloneCatalogService).GetConstructors().Single().GetParameters(), x =>
            x.ParameterType == typeof(DesktopBootstrapperService) || x.ParameterType == typeof(HttpClient)
            || x.ParameterType == typeof(IHttpClientFactory) || x.ParameterType == typeof(IDesktopReleaseArchiveInspector)
            || x.ParameterType == typeof(IDesktopReleaseStorage));
        Assert.DoesNotContain(typeof(DesktopStandaloneReleaseResponse).GetProperties(), x => x.Name is
            "SizeBytes" or "SharedBootstrapper" or "VerifiedAtUtc" or "PackageMetadata");
    }

    [Theory]
    [InlineData("1.3.0", "3,4,5", true)]
    [InlineData("1.2.9", "3,4,5", false)]
    [InlineData("1.2.0", "3,4", true)]
    [InlineData("1.1.9", "3,4", false)]
    [InlineData("1.1.0", "3", true)]
    [InlineData("1.3.0", "5,4,3", false)]
    [InlineData("1.3.0", "3,4,5,5", false)]
    [InlineData("1.3.0", "3,4,6", false)]
    public void Registering_a_new_Setup_preserves_legacy_capabilities_and_enforces_each_minimum_version(string version, string capabilities, bool valid)
    {
        var options = new DesktopReleaseOptions { Profile = "Production", ApiBaseUrl = "https://metadata.invalid/" };
        var binding = new DesktopBootstrapperBindingInfo(1, version, options.ApiBaseUrl, options.Profile,
            "Stable", "win-x64", 3, ApprovedStaticModelBundle.Current.BundleId, capabilities);
        if (valid) DesktopSharedReleaseValidation.ValidateBinding(binding, options);
        else Assert.Equal("desktop_bootstrapper_binding_invalid", Assert.Throws<AccountApiException>(() => DesktopSharedReleaseValidation.ValidateBinding(binding, options)).Code);
    }

    [Fact]
    public async Task Actual_Admin_authorization_policy_rejects_anonymous_and_Viewer_for_both_write_surfaces()
    {
        using var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        var provider = services.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        foreach (var type in new[] { typeof(AdminDesktopStandaloneReleasesController), typeof(AdminDesktopDownloadSettingsController) })
        {
            var policy = await AuthorizationPolicy.CombineAsync(provider, type.GetCustomAttributes<AuthorizeAttribute>());
            Assert.NotNull(policy);
            Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, policy!)).Succeeded);
            Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Viewer")], "fixture")), null, policy!)).Succeeded);
            Assert.True((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "fixture")), null, policy!)).Succeeded);
        }
    }

    [Fact]
    public async Task An_unreachable_approved_ZIP_can_be_saved_published_and_loaded_without_any_Setup_URL_or_registry()
    {
        using var f = new Fixture();
        var request = f.Request();
        var draft = await f.Service.CreateAsync("admin", request, default);
        Assert.Equal("Draft", draft.Status); Assert.True(draft.CanPublish); Assert.False(draft.IsActive);
        Assert.Null(draft.ErrorCode); Assert.Null(draft.FirstPublishedAtUtc);
        Assert.Null(await f.DownloadSettings.GetPublicDownloadUrlAsync());
        Assert.Null(await f.Service.ExactAsync(draft.ReleaseId, "Production", default));
        var published = await f.Service.PublishAsync(draft.ReleaseId, draft.Revision, default);
        Assert.True(published.IsActive); Assert.Equal("Published", published.Status);
        await using var restarted = f.NewContext();
        var fresh = f.NewService(restarted);
        var visible = Assert.Single(await fresh.VisibleAsync("Production", "Stable", "win-x64", default));
        Assert.Equal(5, visible.CatalogProtocol); Assert.Equal(3, visible.ApplicationDistributionProtocol);
        Assert.Equal("1.3.0", visible.MinimumSetupVersion); Assert.Null(visible.Setup);
        Assert.Equal(request.DownloadUrl, visible.DownloadUrl); Assert.Equal(new string('a', 64), visible.ExpectedSha256);
        Assert.Equal(visible, await fresh.ExactAsync(visible.ReleaseId, "Production", default));
        Assert.Empty(await f.SettingsDb.ServerSettings.ToListAsync());
        Assert.Empty(await f.Db.AppReleases.ToListAsync());
        Assert.Single(await f.Db.DesktopDirectReleases.ToListAsync());
    }

    [Fact]
    public async Task Saving_is_idempotent_but_reusing_a_request_with_changed_content_or_a_duplicate_version_is_a_conflict()
    {
        using var f = new Fixture(); var request = f.Request();
        var first = await f.Service.CreateAsync("admin", request, default);
        var retry = await f.Service.CreateAsync("admin", request, default);
        Assert.Equal(first, retry);
        await Status(409, () => f.Service.CreateAsync("admin", request with { IsMandatory = true }, default));
        await Status(409, () => f.Service.CreateAsync("admin", f.Request(), default));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(first.ReleaseId, (await f.Db.DesktopDirectReleases.SingleAsync()).DirectReleaseId);
    }

    [Theory]
    [InlineData("http://103.179.190.182:8443/models/plain.zip")]
    [InlineData("https://outside.invalid/models/plain.zip")]
    [InlineData("https://103.179.190.182:8443/models/plain.zip")]
    [InlineData("https://103.179.190.182:8443/models/VideoMaker-1.0.2-2-win-x64-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.zip")]
    [InlineData("https://103.179.190.182:8443/models/VideoMaker-1.0.1-2-win-x64-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.zip?download=1")]
    public async Task Invalid_or_version_mismatched_links_do_not_create_a_release(string url)
    {
        using var f = new Fixture();
        await Status(400, () => f.Service.CreateAsync("admin", f.Request() with { DownloadUrl = url }, default));
        Assert.Empty(await f.Db.DesktopDirectReleases.ToListAsync());
    }

    [Fact]
    public async Task Stale_editors_cannot_publish_update_or_deactivate_a_new_revision()
    {
        using var f = new Fixture(); var request = f.Request();
        var draft = await f.Service.CreateAsync("admin", request, default);
        await using var editor = f.NewContext(); var oldEditor = f.NewService(editor);
        var snapshot = await oldEditor.GetAsync(draft.ReleaseId, default);
        var changed = await f.Service.UpdateAsync(draft.ReleaseId, "admin", request with
        { Revision = draft.Revision, ReleaseNotes = "current notes" }, default);
        Assert.NotEqual(draft.Revision, changed.Revision);
        await Status(409, () => oldEditor.PublishAsync(draft.ReleaseId, snapshot!.Revision, default));
        await Status(409, () => oldEditor.UpdateAsync(draft.ReleaseId, "admin", request with { Revision = draft.Revision }, default));
        var published = await f.Service.PublishAsync(draft.ReleaseId, changed.Revision, default);
        await Status(409, () => oldEditor.DeactivateAsync(draft.ReleaseId, changed.Revision, default));
        Assert.Equal(published.Revision, (await f.Service.GetAsync(draft.ReleaseId, default))!.Revision);
    }

    [Fact]
    public async Task A_published_identity_stays_immutable_after_deactivation_but_notes_can_change()
    {
        using var f = new Fixture(); var request = f.Request();
        var draft = await f.Service.CreateAsync("admin", request, default);
        var published = await f.Service.PublishAsync(draft.ReleaseId, draft.Revision, default);
        var edited = await f.Service.UpdateAsync(draft.ReleaseId, "admin", request with
        { Revision = published.Revision, ReleaseNotes = "release notes", IsMandatory = true }, default);
        Assert.Equal("release notes", edited.ReleaseNotes); Assert.True(edited.IsMandatory);
        var inactive = await f.Service.DeactivateAsync(draft.ReleaseId, edited.Revision, default);
        Assert.Null(await f.Service.ExactAsync(draft.ReleaseId, "Production", default));
        await Status(409, () => f.Service.UpdateAsync(draft.ReleaseId, "admin", f.Request("1.0.2") with { Revision = inactive.Revision }, default));
        await Status(409, () => f.Service.UpdateAsync(draft.ReleaseId, "admin", request with
        { Revision = inactive.Revision, DownloadUrl = request.DownloadUrl.Replace(new string('a', 64), new string('b', 64), StringComparison.Ordinal) }, default));
        var reactivated = await f.Service.PublishAsync(draft.ReleaseId, inactive.Revision, default);
        Assert.Equal(published.FirstPublishedAtUtc, reactivated.FirstPublishedAtUtc);
        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task Invalid_optional_Setup_link_does_not_remove_a_release_from_a_running_Setup_catalog()
    {
        using var f = new Fixture();
        var invalidSetting = "{\"setupDownloadUrl\":\"https://outside.invalid/Setup.exe\"}";
        await f.SettingsDb.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ServerSettings VALUES ('11111111-1111-1111-1111-111111111111','Desktop.SetupDownload',{invalidSetting},NULL,'2026-10-17 12:00:00',X'0102030405060708');");
        var draft = await f.Service.CreateAsync("admin", f.Request(), default);
        await f.Service.PublishAsync(draft.ReleaseId, draft.Revision, default);
        var visible = Assert.Single(await f.Service.VisibleAsync("Production", "Stable", "win-x64", default));
        Assert.Null(visible.Setup); Assert.Null(await f.DownloadSettings.GetPublicDownloadUrlAsync());
        Assert.NotNull(await f.Service.ExactAsync(draft.ReleaseId, "Production", default));
    }

    [Fact]
    public async Task A_canonical_Setup_URL_is_only_an_optional_declared_pointer_and_never_an_activation_requirement()
    {
        using var f = new Fixture();
        var url = DesktopArtifactDownloadPolicy.ModelHostPackagePrefix + "Setup-1.3.0-win-x64-" + new string('b', 64) + ".exe";
        var json = JsonSerializer.Serialize(new { setupDownloadUrl = url });
        await f.SettingsDb.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ServerSettings VALUES ('11111111-1111-1111-1111-111111111111','Desktop.SetupDownload',{json},NULL,'2026-10-17 12:00:00',X'0102030405060708');");
        var draft = await f.Service.CreateAsync("admin", f.Request(), default);
        await f.Service.PublishAsync(draft.ReleaseId, draft.Revision, default);
        var visible = Assert.Single(await f.Service.VisibleAsync("Production", "Stable", "win-x64", default));
        Assert.Equal(url, visible.Setup!.DownloadUrl); Assert.Equal("1.3.0", visible.Setup.Version);
        Assert.Equal(new string('b', 64), visible.Setup.Sha256);
    }

    [Fact]
    public async Task Catalog_scope_and_running_Setup_version_are_checked_without_reinterpreting_protocol_four()
    {
        using var f = new Fixture();
        var draft = await f.Service.CreateAsync("admin", f.Request(), default);
        await f.Service.PublishAsync(draft.ReleaseId, draft.Revision, default);
        Assert.Empty(await f.Service.VisibleAsync("Other", "Stable", "win-x64", default));
        Assert.Empty(await f.Service.VisibleAsync("Production", "Beta", "win-x64", default));
        Assert.Empty(await f.Service.VisibleAsync("Production", "Stable", "win-arm64", default));
        Assert.Empty(await f.Service.VisibleAsync("Production", "Stable", "win-x64", default, setupVersion: "1.2.0"));
        Assert.Single(await f.Service.VisibleAsync("Production", "Stable", "win-x64", default, setupVersion: "1.3.0"));
        Assert.Null(await f.Service.ExactAsync(draft.ReleaseId, "Other", default));
        var entity = await f.Db.DesktopDirectReleases.SingleAsync();
        entity.CatalogProtocol = 4; entity.MinimumBootstrapperVersion = "1.2.0"; await f.Db.SaveChangesAsync();
        Assert.Empty(await f.Service.VisibleAsync("Production", "Stable", "win-x64", default));
        Assert.Null(await f.Service.ExactAsync(draft.ReleaseId, "Production", default));
        Assert.Null(await f.Service.GetAsync(draft.ReleaseId, default));
        Assert.Equal(0, (await f.Service.PageAsync(1, 20, default)).TotalCount);
    }

    [Fact]
    public async Task Update_and_repair_keep_semantic_version_order_and_exact_build_identity()
    {
        using var f = new Fixture();
        foreach (var version in new[] { "1.9.0", "1.10.0" })
        { var draft = await f.Service.CreateAsync("admin", f.Request(version), default); await f.Service.PublishAsync(draft.ReleaseId, draft.Revision, default); }
        var controller = new DesktopStandaloneUpdatesController(f.Service);
        Assert.Equal("1.10.0", (await controller.Check("1.9.0")).Release!.Version);
        Assert.False((await controller.Check("1.10.0", buildNumber: 1)).IsUpdateAvailable);
        Assert.IsType<NotFoundResult>((await controller.Repair("1.10.0", 999)).Result);
        var repair = Assert.IsType<OkObjectResult>((await controller.Repair("1.10.0", 2)).Result);
        Assert.Equal("1.10.0", Assert.IsType<DesktopStandaloneReleaseResponse>(repair.Value).Version);
    }

    [Fact]
    public async Task Database_checksum_tampering_never_becomes_a_served_or_republished_release()
    {
        using var f = new Fixture(); var draft = await f.Service.CreateAsync("admin", f.Request(), default);
        var published = await f.Service.PublishAsync(draft.ReleaseId, draft.Revision, default);
        var entity = await f.Db.DesktopDirectReleases.SingleAsync(); entity.ExpectedSha256 = new string('c', 64); await f.Db.SaveChangesAsync();
        Assert.Empty(await f.Service.VisibleAsync("Production", "Stable", "win-x64", default));
        Assert.Null(await f.Service.ExactAsync(draft.ReleaseId, "Production", default));
        await Status(400, () => f.Service.PublishAsync(draft.ReleaseId, published.Revision, default));
    }

    [Fact]
    public void Setup_settings_accept_POST_and_keep_the_authorized_legacy_PUT_alias()
    {
        var controller = typeof(AdminDesktopDownloadSettingsController);
        Assert.Contains(controller.GetCustomAttributes<AuthorizeAttribute>(), x => x.Roles == "Admin");
        Assert.Contains(controller.GetMethods(), x => x.GetCustomAttribute<HttpPostAttribute>() is not null);
        Assert.Contains(controller.GetMethods(), x => x.GetCustomAttribute<HttpPutAttribute>() is not null);
        Assert.Empty(controller.GetMethods().SelectMany(x => x.GetCustomAttributes<AllowAnonymousAttribute>()));
    }

    private static async Task Status(int status, Func<Task> action) =>
        Assert.Equal(status, (await Assert.ThrowsAsync<AccountApiException>(action)).StatusCode);

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-standalone-server-" + Guid.NewGuid().ToString("N"));
        private readonly DbContextOptions<AccountDbContext> _options;
        public AccountDbContext Db { get; }
        public VideoFactoryDbContext SettingsDb { get; }
        public DesktopReleaseOptions Settings { get; } = new()
        {
            PublicationMode = "StandaloneCatalog", SharedBootstrapperEnabled = false,
            ApiBaseUrl = "https://metadata.invalid/", Profile = "Production",
            AllowedExternalPrefixes = DesktopArtifactDownloadPolicy.ApprovedExternalPrefixes.ToArray()
        };
        public DesktopDownloadSettingsService DownloadSettings { get; }
        public DesktopStandaloneCatalogService Service { get; }
        public Fixture()
        {
            Directory.CreateDirectory(_root);
            var connection = $"Data Source={Path.Combine(_root, "catalog.db")};Pooling=False";
            _options = new DbContextOptionsBuilder<AccountDbContext>().UseSqlite(connection).Options;
            Db = NewContext(); Db.Database.ExecuteSqlRaw(Schema);
            SettingsDb = new(new DbContextOptionsBuilder<VideoFactoryDbContext>().UseSqlite(connection).Options);
            DownloadSettings = new(SettingsDb, Options.Create(Settings), TimeProvider.System);
            Service = NewService(Db);
        }
        public AccountDbContext NewContext() => new(_options);
        public DesktopStandaloneCatalogService NewService(AccountDbContext db) => new(db, DownloadSettings, Options.Create(Settings), TimeProvider.System);
        public DesktopStandaloneReleaseRequest Request(string version = "1.0.1") => new(version,
            DesktopArtifactDownloadPolicy.ModelHostPackagePrefix + $"VideoMaker-{version}-2-win-x64-{new string('a', 64)}.zip", Guid.NewGuid());
        public void Dispose()
        {
            Db.Dispose(); SettingsDb.Dispose();
            if (!Path.GetFullPath(_root).StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture root.");
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        private const string Schema = """
            CREATE TABLE DesktopDirectReleases(DirectReleaseId TEXT PRIMARY KEY,Version TEXT NOT NULL,DownloadUrl TEXT NOT NULL,FileName TEXT NOT NULL,
              ExpectedSha256 TEXT NOT NULL,DeclaredBuildNumber INTEGER NOT NULL,Profile TEXT NOT NULL,Channel TEXT NOT NULL,Platform TEXT NOT NULL,
              CatalogProtocol INTEGER NOT NULL,ApplicationDistributionProtocol INTEGER NOT NULL,ModelBundleId TEXT NOT NULL,MinimumBootstrapperVersion TEXT NOT NULL,
              ReleaseNotes TEXT,IsMandatory INTEGER NOT NULL,IsActive INTEGER NOT NULL,PublishedAtUtc TEXT,FirstPublishedAtUtc TEXT,Revision TEXT NOT NULL,
              ActorUserId TEXT NOT NULL,ClientRequestId TEXT NOT NULL,CreateRequestFingerprint TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,UpdatedAtUtc TEXT NOT NULL,
              UNIQUE(Profile,Channel,Platform,Version),UNIQUE(ActorUserId,ClientRequestId));
            CREATE TABLE ServerSettings(ServerSettingId TEXT PRIMARY KEY,SettingKey TEXT NOT NULL UNIQUE,ValueJson TEXT,Description TEXT,
              UpdatedAtUtc TEXT NOT NULL,RowVersion BLOB NOT NULL);
            CREATE TABLE AppReleases(AppReleaseId TEXT PRIMARY KEY,Version TEXT NOT NULL,BuildNumber INTEGER NOT NULL,Channel TEXT NOT NULL,Platform TEXT NOT NULL,
              MinimumSupportedDesktopVersion TEXT,DownloadUrl TEXT,Sha256 TEXT,ReleaseNotes TEXT,IsMandatory INTEGER NOT NULL,IsActive INTEGER NOT NULL,
              PublishedAtUtc TEXT NOT NULL,FirstPublishedAtUtc TEXT,Revision TEXT NOT NULL);
            CREATE TABLE AppReleaseArtifacts(AppReleaseArtifactId TEXT PRIMARY KEY,AppReleaseId TEXT NOT NULL,Kind TEXT NOT NULL,FileName TEXT NOT NULL,RelativePath TEXT,
              SizeBytes INTEGER NOT NULL,Sha256 TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,SourceKind TEXT NOT NULL,ExternalDownloadUrl TEXT,InstallMode TEXT NOT NULL,
              DistributionProtocol INTEGER NOT NULL,RegistrationEvidenceJson TEXT,BoundPackageSha256 TEXT,ModelBundleId TEXT,VerifiedPreparationId TEXT);
            """;
    }
}

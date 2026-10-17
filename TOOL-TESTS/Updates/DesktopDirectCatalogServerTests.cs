using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Controllers;
using TOOL_SERVER.Data;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_TESTS.Updates;

// SQLite verifies real transactions/restart/CAS. The injected metadata reader simulates
// a new Setup capability resource; these tests do not certify a published Setup binary.
public sealed class DesktopDirectCatalogServerTests
{
    [Fact]
    public void Direct_catalog_is_an_explicit_mode_and_has_no_network_or_inspector_dependency()
    {
        Assert.Equal("ServerInspection", new DesktopReleaseOptions().PublicationMode);
        var parameters = typeof(DesktopDirectCatalogService).GetConstructors().Single().GetParameters();
        Assert.DoesNotContain(parameters, x => x.ParameterType == typeof(HttpClient) || x.ParameterType == typeof(IHttpClientFactory)
            || x.ParameterType == typeof(IDesktopReleaseArchiveInspector));
        Assert.Contains(typeof(AdminDesktopDirectReleasesController).GetCustomAttributes<AuthorizeAttribute>(), x => x.Roles == "Admin");
        Assert.Empty(typeof(AdminDesktopDirectReleasesController).GetMethods().SelectMany(x => x.GetCustomAttributes<AllowAnonymousAttribute>()));
        Assert.Contains(typeof(DesktopDirectUpdatesController).GetCustomAttributes<AuthorizeAttribute>(), _ => true);
    }

    [Fact]
    public async Task Save_is_an_immediate_draft_with_declared_checksum_and_no_measured_size_or_fake_legacy_release()
    {
        using var f = new Fixture();
        var saved = await f.Service.CreateAsync("admin", f.Request(), default);
        Assert.Equal("Draft", saved.Status); Assert.False(saved.IsActive); Assert.False(saved.CanPublish);
        Assert.Equal(2, saved.DeclaredBuildNumber); Assert.Equal(new string('a', 64), saved.ExpectedSha256);
        Assert.Null(saved.FirstPublishedAtUtc); Assert.Null(saved.Bootstrapper);
        Assert.Single(await f.Db.DesktopDirectReleases.ToListAsync());
        Assert.Empty(await f.Db.AppReleases.ToListAsync());
        Assert.Empty(await f.Db.DesktopReleasePreparations.ToListAsync());
        Assert.DoesNotContain(typeof(DesktopDirectReleaseResponse).GetProperties(), x => x.Name is "SizeBytes" or "VerifiedAtUtc" or "BuildNumber");
    }

    [Fact]
    public async Task Create_is_idempotent_and_rejects_request_reuse_with_other_content()
    {
        using var f = new Fixture(); var request = f.Request();
        var a = await f.Service.CreateAsync("admin", request, default);
        var b = await f.Service.CreateAsync("admin", request, default);
        Assert.Equal(a.ReleaseId, b.ReleaseId); Assert.Equal(a.Revision, b.Revision);
        await Code("desktop_direct_request_reused", () => f.Service.CreateAsync("admin", request with { IsMandatory = true }, default));
        Assert.Single(await f.Db.DesktopDirectReleases.ToListAsync());
    }

    [Theory]
    [InlineData("https://103.179.190.182:8443/models/plain.zip", "desktop_direct_filename_invalid")]
    [InlineData("https://outside.invalid/VideoMaker-1.0.1-2-win-x64-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.zip", "desktop_external_url_invalid")]
    [InlineData("https://103.179.190.182:8443/models/VideoMaker-1.0.2-2-win-x64-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.zip", "desktop_direct_version_mismatch")]
    public async Task Invalid_or_mismatched_URL_is_rejected_before_making_a_catalog_entry(string url, string code)
    {
        using var f = new Fixture();
        await Code(code, () => f.Service.CreateAsync("admin", f.Request() with { DownloadUrl = url }, default));
        Assert.Empty(await f.Db.DesktopDirectReleases.ToListAsync());
    }

    [Fact]
    public async Task Saving_an_unreachable_but_syntactically_approved_VPS_link_needs_no_Setup_or_server_outbound()
    {
        using var f = new Fixture();
        var saved = await f.Service.CreateAsync("admin", f.Request(), default);
        Assert.NotEqual(Guid.Empty, saved.ReleaseId);
        Assert.Equal("desktop_direct_bootstrapper_required", saved.ErrorCode);
        await Code("desktop_direct_bootstrapper_required", () => f.Service.PublishAsync(saved.ReleaseId, saved.Revision, default));
        Assert.False((await f.Db.DesktopDirectReleases.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Old_Setup_without_direct_capability_cannot_publish_or_list_direct_releases()
    {
        using var f = new Fixture(directCapability: false); await f.UploadSetup();
        var draft = await f.Service.CreateAsync("admin", f.Request(), default);
        Assert.False(draft.CanPublish);
        await Code("desktop_direct_bootstrapper_required", () => f.Service.PublishAsync(draft.ReleaseId, draft.Revision, default));
        Assert.Empty(await f.Service.VisibleAsync("Production", "Stable", "win-x64", null, default));
    }

    [Fact]
    public async Task Publish_survives_restart_and_exact_requests_stop_after_deactivation_or_Setup_revocation()
    {
        using var f = new Fixture(); var setup = await f.UploadSetup();
        var draft = await f.Service.CreateAsync("admin", f.Request(), default);
        var published = await f.Service.PublishAsync(draft.ReleaseId, draft.Revision, default);
        Assert.True(published.IsActive); Assert.Equal("Published", published.Status);
        await using var restarted = f.NewContext(); var service = f.NewService(restarted);
        var visible = Assert.Single(await service.VisibleAsync("Production", "Stable", "win-x64", null, default));
        Assert.Equal(4, visible.CatalogProtocol); Assert.Equal(3, visible.ApplicationDistributionProtocol);
        Assert.Equal("3,4", visible.SharedBootstrapper.SupportedCatalogProtocols);
        DesktopDirectCatalogPolicy.ValidateRelease(visible, new Uri("https://metadata.invalid/"), "Production");
        Assert.Null(await service.ExactAsync(draft.ReleaseId, "Other", setup.Bootstrapper.BootstrapperId, default));
        Assert.Null(await service.ExactAsync(draft.ReleaseId, "Production", Guid.NewGuid(), default));
        var inactive = await f.Service.DeactivateAsync(draft.ReleaseId, published.Revision, default);
        Assert.Null(await service.ExactAsync(draft.ReleaseId, "Production", setup.Bootstrapper.BootstrapperId, default));
        var active = await f.Service.PublishAsync(draft.ReleaseId, inactive.Revision, default);
        Assert.Equal(published.FirstPublishedAtUtc, active.FirstPublishedAtUtc);
        await f.Bootstrappers.RevokeAsync(setup.Bootstrapper.BootstrapperId, setup.Revision, default);
        Assert.Null(await service.ExactAsync(draft.ReleaseId, "Production", setup.Bootstrapper.BootstrapperId, default));
    }

    [Fact]
    public async Task Revision_and_published_identity_remain_immutable_even_after_deactivation()
    {
        using var f = new Fixture(); await f.UploadSetup(); var request = f.Request();
        var draft = await f.Service.CreateAsync("admin", request, default);
        await Code("desktop_direct_changed", () => f.Service.PublishAsync(draft.ReleaseId, Guid.NewGuid(), default));
        var published = await f.Service.PublishAsync(draft.ReleaseId, draft.Revision, default);
        var updated = await f.Service.UpdateAsync(draft.ReleaseId, "admin", request with { Revision = published.Revision, ReleaseNotes = "new notes" }, default);
        Assert.Equal("new notes", updated.ReleaseNotes);
        await Code("desktop_direct_changed", () => f.Service.DeactivateAsync(draft.ReleaseId, published.Revision, default));
        var inactive = await f.Service.DeactivateAsync(draft.ReleaseId, updated.Revision, default);
        await Code("desktop_direct_immutable", () => f.Service.UpdateAsync(draft.ReleaseId, "admin", f.Request("1.0.2") with { Revision = inactive.Revision }, default));
        await Code("desktop_direct_immutable", () => f.Service.UpdateAsync(draft.ReleaseId, "admin", request with
        { Revision = inactive.Revision, DownloadUrl = request.DownloadUrl.Replace(new string('a', 64), new string('b', 64), StringComparison.Ordinal) }, default));
    }

    [Fact]
    public async Task Duplicate_version_does_not_replace_the_existing_identity()
    {
        using var f = new Fixture(); var first = await f.Service.CreateAsync("admin", f.Request(), default);
        await Code("desktop_direct_changed", () => f.Service.CreateAsync("admin", f.Request(), default));
        f.Db.ChangeTracker.Clear(); Assert.Equal(first.ReleaseId, (await f.Db.DesktopDirectReleases.SingleAsync()).DirectReleaseId);
    }

    [Fact]
    public async Task Update_check_uses_semantic_version_and_repair_requires_exact_declared_build()
    {
        using var f = new Fixture(); await f.UploadSetup();
        foreach (var version in new[] { "1.9.0", "1.10.0" })
        { var d = await f.Service.CreateAsync("admin", f.Request(version), default); await f.Service.PublishAsync(d.ReleaseId, d.Revision, default); }
        var controller = new DesktopDirectUpdatesController(f.Service);
        Assert.Equal("1.10.0", (await controller.Check("1.9.0")).Release!.Version);
        Assert.False((await controller.Check("1.10.0", buildNumber: 1)).IsUpdateAvailable);
        Assert.IsType<NotFoundResult>((await controller.Repair("1.10.0", 999)).Result);
        Assert.IsType<OkObjectResult>((await controller.Repair("1.10.0", 2)).Result);
        Assert.Empty(await f.Service.VisibleAsync("Other", "Stable", "win-x64", null, default));
    }

    [Fact]
    public async Task Altered_catalog_checksum_cannot_be_served_as_a_valid_release()
    {
        using var f = new Fixture(); await f.UploadSetup(); var draft = await f.Service.CreateAsync("admin", f.Request(), default);
        await f.Service.PublishAsync(draft.ReleaseId, draft.Revision, default);
        var entity = await f.Db.DesktopDirectReleases.SingleAsync(); entity.ExpectedSha256 = new string('c', 64); await f.Db.SaveChangesAsync();
        Assert.Empty(await f.Service.VisibleAsync("Production", "Stable", "win-x64", null, default));
    }

    [Fact]
    public async Task Nonrevoked_Setup_1_2_keeps_its_exact_binary_when_Setup_1_3_becomes_active()
    {
        using var f = new Fixture(); var original = await f.UploadSetup();
        var draft = await f.Service.CreateAsync("admin", f.Request(), default);
        await f.Service.PublishAsync(draft.ReleaseId, draft.Revision, default);
        var replacement = await f.UploadSetup("B");
        Assert.Equal("1.3.0", replacement.Bootstrapper.Version);
        Assert.NotEqual(original.Bootstrapper.Sha256, replacement.Bootstrapper.Sha256);
        var controller = new LauncherDirectDistributionController(f.Service);
        var oldCompatible = Assert.Single((await controller.Versions(bootstrapperVersion: "1.2.0")).Releases);
        Assert.Equal(original.Bootstrapper.BootstrapperId, oldCompatible.SharedBootstrapper.BootstrapperId);
        Assert.Equal(original.Bootstrapper.Sha256, oldCompatible.SharedBootstrapper.Sha256);
        Assert.Equal(replacement.Bootstrapper.BootstrapperId, Assert.Single((await controller.Versions()).Releases).SharedBootstrapper.BootstrapperId);
        Assert.Empty((await controller.Versions(bootstrapperVersion: "1.4.0")).Releases);
        Assert.Empty((await controller.Versions(bootstrapperId: replacement.Bootstrapper.BootstrapperId, bootstrapperVersion: "1.2.0")).Releases);
        var previousEntity = await f.Db.DesktopBootstrappers.SingleAsync(x => x.BootstrapperId == original.Bootstrapper.BootstrapperId);
        await f.Bootstrappers.RevokeAsync(previousEntity.BootstrapperId, previousEntity.Revision, default);
        Assert.Empty((await controller.Versions(bootstrapperVersion: "1.2.0")).Releases);
        Assert.Single((await controller.Versions()).Releases);
    }

    [Fact]
    public async Task Direct_mode_does_not_claim_or_retry_legacy_jobs_or_call_the_inspector()
    {
        using var f = new Fixture(); var inspector = new ForbiddenInspector();
        var legacy = new DesktopReleasePreparationService(f.Db, f.DbOptions, inspector, f.Bootstrappers, Options.Create(f.Settings), TimeProvider.System);
        Assert.False(await legacy.ProcessNextAsync(default));
        await Code("desktop_server_inspection_disabled", () => legacy.CreateAsync("admin", new("1.0.1", Fixture.Url("1.0.1"), Guid.NewGuid()), default));
        await Code("desktop_server_inspection_disabled", () => legacy.RetryAsync(Guid.NewGuid(), Guid.NewGuid(), default));
        Assert.Equal(0, inspector.Calls); Assert.Empty(await f.Db.DesktopReleasePreparations.ToListAsync());
        Assert.Equal("DirectCatalog", f.Bootstrappers.Status().PublicationMode); Assert.False(f.Bootstrappers.Status().ReleasePreparationEnabled);
    }

    private static async Task Code(string code, Func<Task> action) => Assert.Equal(code, (await Assert.ThrowsAsync<AccountApiException>(action)).Code);
    private sealed class ForbiddenInspector : IDesktopReleaseArchiveInspector
    {
        public int Calls;
        public Task<DesktopReleaseInspectionResult> InspectAsync(DesktopReleaseInspectionRequest request, IProgress<DesktopReleaseInspectionProgress>? progress, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Catalog must not download packages."); }
    }
    private sealed class CapabilityReader(bool enabled) : IDesktopBootstrapperMetadataReader
    {
        public DesktopBootstrapperBindingInfo Read(string path) => DesktopExecutableMetadata.ReadBootstrapperBinding(
            Path.Combine(AppContext.BaseDirectory, "DistributionResources", "DistributionResourcesA.exe")) with
            { Version = DesktopExecutableMetadata.ReadApplicationVersion(path).Version, SupportedCatalogProtocols = enabled ? "3,4" : "3" };
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "VM-DirectCatalog-" + Guid.NewGuid().ToString("N"));
        public DbContextOptions<AccountDbContext> DbOptions { get; }
        public AccountDbContext Db { get; }
        public DesktopReleaseOptions Settings { get; }
        public DesktopReleaseStorage Storage { get; }
        public DesktopBootstrapperService Bootstrappers { get; }
        public DesktopDirectCatalogService Service { get; }
        private readonly bool _capability;
        public Fixture(bool directCapability = true)
        {
            _capability = directCapability; Directory.CreateDirectory(Root);
            DbOptions = new DbContextOptionsBuilder<AccountDbContext>().UseSqlite($"Data Source={Path.Combine(Root, "proof.db")};Pooling=False").Options;
            Db = NewContext(); Db.Database.ExecuteSqlRaw(Schema);
            Settings = new DesktopReleaseOptions { PublicationMode = "DirectCatalog", SharedBootstrapperEnabled = true, ApiBaseUrl = "https://metadata.invalid/",
                Profile = "Production", StorageRoot = Path.Combine(Root, "setup"), AllowedExternalPrefixes = DesktopArtifactDownloadPolicy.ApprovedExternalPrefixes.ToArray() };
            Storage = new(Options.Create(Settings), new Host(Root));
            Bootstrappers = NewBootstrapper(Db); Service = NewService(Db);
        }
        public AccountDbContext NewContext() => new(DbOptions);
        private DesktopBootstrapperService NewBootstrapper(AccountDbContext context) => new(context, Storage, new CapabilityReader(_capability), Options.Create(Settings), TimeProvider.System);
        public DesktopDirectCatalogService NewService(AccountDbContext context) => new(context, NewBootstrapper(context), Options.Create(Settings), TimeProvider.System);
        public static string Url(string version) => DesktopArtifactDownloadPolicy.ModelHostPackagePrefix + $"VideoMaker-{version}-2-win-x64-{new string('a', 64)}.zip";
        public DesktopDirectReleaseRequest Request(string version = "1.0.1") => new(version, Url(version), Guid.NewGuid());
        public async Task<DesktopBootstrapperAdminResponse> UploadSetup(string variant = "A")
        {
            using var bytes = new MemoryStream(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "DistributionResources", $"DistributionResources{variant}.exe")));
            var setup = await Bootstrappers.UploadAsync("admin", "Setup.exe", bytes, bytes.Length, default);
            return await Bootstrappers.ActivateAsync(setup.Bootstrapper.BootstrapperId, setup.Revision, default);
        }
        public void Dispose()
        {
            Db.Dispose();
            if (!Path.GetFullPath(Root).StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
        private const string Schema = """
            CREATE TABLE DesktopDirectReleases(DirectReleaseId TEXT PRIMARY KEY,Version TEXT NOT NULL,DownloadUrl TEXT NOT NULL,FileName TEXT NOT NULL,
              ExpectedSha256 TEXT NOT NULL,DeclaredBuildNumber INTEGER NOT NULL,Profile TEXT NOT NULL,Channel TEXT NOT NULL,Platform TEXT NOT NULL,
              CatalogProtocol INTEGER NOT NULL,ApplicationDistributionProtocol INTEGER NOT NULL,ModelBundleId TEXT NOT NULL,MinimumBootstrapperVersion TEXT NOT NULL,
              ReleaseNotes TEXT,IsMandatory INTEGER NOT NULL,IsActive INTEGER NOT NULL,PublishedAtUtc TEXT,FirstPublishedAtUtc TEXT,Revision TEXT NOT NULL,
              ActorUserId TEXT NOT NULL,ClientRequestId TEXT NOT NULL,CreateRequestFingerprint TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,UpdatedAtUtc TEXT NOT NULL,
              UNIQUE(Profile,Channel,Platform,Version),UNIQUE(ActorUserId,ClientRequestId));
            CREATE TABLE DesktopBootstrappers(BootstrapperId TEXT PRIMARY KEY,Version TEXT NOT NULL,Profile TEXT NOT NULL,Platform TEXT NOT NULL,Channel TEXT NOT NULL,
              ServerBaseUrl TEXT NOT NULL,ModelBundleId TEXT NOT NULL,DistributionProtocol INTEGER NOT NULL,FileName TEXT NOT NULL,RelativePath TEXT NOT NULL,
              SizeBytes INTEGER NOT NULL,Sha256 TEXT NOT NULL,EmbeddedMetadataJson TEXT NOT NULL,IsActive INTEGER NOT NULL,IsRevoked INTEGER NOT NULL,
              Revision TEXT NOT NULL,CreatedByUserId TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,ActivatedAtUtc TEXT);
            CREATE TABLE AppReleases(AppReleaseId TEXT PRIMARY KEY,Version TEXT NOT NULL,BuildNumber INTEGER NOT NULL,Channel TEXT NOT NULL,Platform TEXT NOT NULL,
              MinimumSupportedDesktopVersion TEXT,DownloadUrl TEXT,Sha256 TEXT,ReleaseNotes TEXT,IsMandatory INTEGER NOT NULL,IsActive INTEGER NOT NULL,
              PublishedAtUtc TEXT NOT NULL,FirstPublishedAtUtc TEXT,Revision TEXT NOT NULL);
            CREATE TABLE AppReleaseArtifacts(AppReleaseArtifactId TEXT PRIMARY KEY,AppReleaseId TEXT NOT NULL,Kind TEXT NOT NULL,FileName TEXT NOT NULL,RelativePath TEXT,
              SizeBytes INTEGER NOT NULL,Sha256 TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,SourceKind TEXT NOT NULL,ExternalDownloadUrl TEXT,InstallMode TEXT NOT NULL,
              DistributionProtocol INTEGER NOT NULL,RegistrationEvidenceJson TEXT,BoundPackageSha256 TEXT,ModelBundleId TEXT,VerifiedPreparationId TEXT);
            CREATE TABLE DesktopReleasePreparations(PreparationId TEXT PRIMARY KEY,ActorUserId TEXT NOT NULL,ClientRequestId TEXT NOT NULL,Version TEXT NOT NULL,DownloadUrl TEXT NOT NULL,
              ReleaseNotes TEXT,IsMandatory INTEGER NOT NULL,Profile TEXT NOT NULL,Channel TEXT NOT NULL,Platform TEXT NOT NULL,Revision TEXT NOT NULL,
              InputFingerprint TEXT NOT NULL,CreateRequestFingerprint TEXT NOT NULL,Generation INTEGER NOT NULL,State TEXT NOT NULL,Stage TEXT NOT NULL,
              BytesReceived INTEGER NOT NULL,TotalBytes INTEGER,AttemptCount INTEGER NOT NULL,LeaseOwner TEXT,LeaseExpiresAtUtc TEXT,HeartbeatAtUtc TEXT,NextAttemptAtUtc TEXT,
              CancellationRequested INTEGER NOT NULL,ErrorCode TEXT,ErrorMessage TEXT,InspectionEvidenceJson TEXT,VerifiedAtUtc TEXT,ReleaseId TEXT,CreatedAtUtc TEXT NOT NULL,UpdatedAtUtc TEXT NOT NULL);
            """;
    }
    private sealed class Host(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "DirectCatalogFixture";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

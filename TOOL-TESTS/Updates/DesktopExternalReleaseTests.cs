using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Controllers;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Updates;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_TESTS.Updates;

// Real SQLite transactions and tiny owned Setup fixtures. No model, server,
// real database, HTTP/DNS download or execution of the uploaded file.
public sealed class DesktopExternalReleaseTests
{
    [Fact]
    public void Registration_and_publish_inherit_Admin_authorization_without_anonymous_overrides()
    {
        var controller = typeof(AdminDesktopReleasesController);
        Assert.Contains(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(), x => x.Roles == "Admin");
        foreach (var name in new[] { nameof(AdminDesktopReleasesController.RegisterExternal), nameof(AdminDesktopReleasesController.Publish) })
            Assert.Empty(controller.GetMethod(name)!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Fact]
    public async Task Legacy_internal_upload_publish_and_edit_are_preserved_without_requiring_Setup()
    {
        using var f = new Fixture();
        var draft = await f.Draft();
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var path in f.Registration().PackageManifest.ManagedFiles.Where(x => x != "update-manifest.json"))
            {
                using var entry = archive.CreateEntry(path).Open(); entry.Write("fixture"u8);
            }
            using var manifest = archive.CreateEntry("update-manifest.json").Open();
            JsonSerializer.Serialize(manifest, f.Registration().PackageManifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        zip.Position = 0;
        await f.Service.SaveArtifactAsync(draft.ReleaseId, "DesktopPackage", "internal.zip", zip, zip.Length, default);
        var ready = await f.Current(draft.ReleaseId);
        var published = await f.Service.PublishAsync(draft.ReleaseId, ready.Revision, default);
        Assert.True(published.IsActive);
        Assert.Null(published.FirstPublishedAtUtc);
        Assert.Single(published.Artifacts);
        var visible = (await f.Service.GetLatestPackageAsync("win-x64", "Stable", 1, default))!;
        Assert.Equal(draft.ReleaseId, visible.Release.AppReleaseId);
        Assert.StartsWith("/api/launcher-distribution/", LauncherDistributionController.MapPackage(visible).DownloadUrl);
        var edited = await f.Service.UpdateAsync(draft.ReleaseId, f.Edit(published, active: false) with { ReleaseNotes = "edited" }, default);
        Assert.Equal("edited", edited.ReleaseNotes);
        await Code("desktop_artifact_source_conflict", () => f.Service.RegisterExternalAsync(draft.ReleaseId, f.Registration(), default));
    }

    [Fact]
    public async Task Disabled_by_default_rejects_registration_without_storage_mutation()
    {
        using var f = new Fixture(enabled: false);
        var draft = await f.Draft();
        await Code("desktop_external_disabled", () => f.Service.RegisterExternalAsync(draft.ReleaseId, f.Registration(), default));
        Assert.Equal(0, f.Storage.Saves);
        Assert.False(Directory.Exists(f.Root));
    }

    [Fact]
    public async Task Register_is_metadata_only_draft_and_identical_retry_preserves_revision()
    {
        using var f = new Fixture();
        var draft = await f.Draft();
        var artifact = await f.Service.RegisterExternalAsync(draft.ReleaseId, f.Registration(), default);
        var first = await f.Current(draft.ReleaseId);
        var repeated = await f.Service.RegisterExternalAsync(draft.ReleaseId, f.Registration(), default);
        Assert.True(artifact.MetadataRegisteredOnly);
        Assert.Equal(DesktopArtifactSourceKinds.ExternalVps, artifact.SourceKind);
        Assert.Equal(artifact.ArtifactId, repeated.ArtifactId);
        Assert.Equal(first.Revision, (await f.Current(draft.ReleaseId)).Revision);
        Assert.Equal(0, f.Storage.Saves);
        Assert.Equal(0, f.Storage.Resolves);
        Assert.False(first.IsActive);
        Assert.Empty(await f.Service.GetVisiblePackagesAsync("win-x64", "Stable", 2, default));
        await Code("desktop_exact_setup_required", () => f.Service.PublishAsync(draft.ReleaseId, first.Revision, default));
    }

    [Theory]
    [InlineData("https://outside.invalid/apps/app.zip")]
    [InlineData("http://103.179.190.182:8443/apps/videomaker/app.zip")]
    [InlineData("https://103.179.190.182/apps/videomaker/app.zip")]
    [InlineData("https://u:p@103.179.190.182:8443/apps/videomaker/app.zip")]
    [InlineData("https://103.179.190.182:8443/apps/videomaker/app.zip?token=x")]
    [InlineData("https://103.179.190.182:8443/apps/videomaker/../app.zip")]
    [InlineData("https://103.179.190.182:8443/apps/videomaker/%2e%2e/app.zip")]
    [InlineData("https://103.179.190.182:8443/apps/videomaker/app.zip#x")]
    public async Task Url_rejection_precedes_any_artifact_storage(string url)
    {
        using var f = new Fixture();
        var draft = await f.Draft();
        await Code("desktop_external_url_invalid", () => f.Service.RegisterExternalAsync(draft.ReleaseId,
            f.Registration() with { DownloadUrl = url }, default));
        Assert.Equal(0, f.Storage.Saves);
        Assert.Empty((await f.Current(draft.ReleaseId)).Artifacts);
    }

    [Fact]
    public async Task Operator_configuration_cannot_expand_compiled_consumer_origins()
    {
        using var f = new Fixture(prefix: "https://outside.invalid/apps/");
        var draft = await f.Draft();
        await Code("desktop_external_url_invalid", () => f.Service.RegisterExternalAsync(draft.ReleaseId,
            f.Registration() with { DownloadUrl = "https://outside.invalid/apps/app.zip" }, default));
        Assert.Equal(0, f.Storage.Saves);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("protocol")]
    [InlineData("bundle")]
    [InlineData("size")]
    [InlineData("expanded")]
    [InlineData("entries")]
    [InlineData("hash")]
    public async Task Invalid_binding_or_resource_metadata_never_registers(string kind)
    {
        using var f = new Fixture();
        var draft = await f.Draft();
        var r = f.Registration();
        r = kind switch
        {
            "version" => r with { Version = "9.9.9" }, "protocol" => r with { DistributionProtocol = 999 },
            "bundle" => r with { ModelBundleId = "other" }, "size" => r with { SizeBytes = 2L * 1024 * 1024 * 1024 + 1 },
            "expanded" => r with { ExpandedSizeBytes = 4L * 1024 * 1024 * 1024 + 1 },
            "entries" => r with { EntryCount = 200001 }, _ => r with { Sha256 = "invalid" }
        };
        await Code("desktop_external_metadata_invalid", () => f.Service.RegisterExternalAsync(draft.ReleaseId, r, default));
        Assert.Empty((await f.Current(draft.ReleaseId)).Artifacts);
    }

    [Fact]
    public async Task Artifact_metadata_cannot_exceed_the_consumer_size_or_name_limit()
    {
        using var f = new Fixture();
        var draft = await f.Draft(); var request = f.Registration();
        await Code("desktop_external_metadata_invalid", () => f.Service.RegisterExternalAsync(draft.ReleaseId,
            request with { Setup = request.Setup with { SizeBytes = 512L * 1024 * 1024 + 1 } }, default));
        await Code("desktop_external_metadata_invalid", () => f.Service.RegisterExternalAsync(draft.ReleaseId,
            request with { Setup = request.Setup with { FileName = new string('s', 177) + ".exe" } }, default));
        await Code("desktop_external_metadata_invalid", () => f.Service.RegisterExternalAsync(draft.ReleaseId,
            request with { FileName = new string('a', 177) + ".zip" }, default));
        Assert.Empty((await f.Current(draft.ReleaseId)).Artifacts);
    }

    [Theory]
    [InlineData("TOOL-LOCAL.exe", "tool-local.EXE")]
    [InlineData("../outside.exe", "notice.txt")]
    [InlineData("C:/outside.exe", "notice.txt")]
    public async Task Unsafe_or_duplicate_declared_manifest_fails(string first, string second)
    {
        using var f = new Fixture();
        var draft = await f.Draft();
        var r = f.Registration();
        r = r with { PackageManifest = r.PackageManifest with { ManagedFiles = [.. r.PackageManifest.ManagedFiles, first, second] } };
        await Code("desktop_external_manifest_invalid", () => f.Service.RegisterExternalAsync(draft.ReleaseId, r, default));
    }

    [Fact]
    public async Task Wrong_Setup_digest_is_removed_and_cannot_make_release_visible()
    {
        using var f = new Fixture();
        var draft = await f.Register();
        await using var wrong = new MemoryStream("MZwrong"u8.ToArray());
        var registration = f.Registration();
        await Code("desktop_exact_setup_invalid", () => f.Service.SaveArtifactAsync(draft.ReleaseId, "Setup",
            registration.Setup.FileName, wrong, wrong.Length, default));
        Assert.Empty(Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories));
        Assert.Single((await f.Current(draft.ReleaseId)).Artifacts);
    }

    [Fact]
    public async Task Publishing_is_atomic_exact_bound_and_hidden_from_legacy_consumers()
    {
        using var f = new Fixture();
        var legacy = await f.Legacy();
        var draft = await f.Register();
        await f.Upload(draft.ReleaseId);
        var ready = await f.Current(draft.ReleaseId);
        await Code("desktop_release_changed", () => f.Service.PublishAsync(draft.ReleaseId, draft.Revision, default));
        Assert.False((await f.Current(draft.ReleaseId)).IsActive);
        var published = await f.Service.PublishAsync(draft.ReleaseId, ready.Revision, default);
        Assert.NotNull(published.FirstPublishedAtUtc);
        Assert.Equal(legacy, (await f.Service.GetLatestPackageAsync("win-x64", "Stable", 1, default))!.Release.AppReleaseId);
        var current = (await f.Service.GetLatestPackageAsync("win-x64", "Stable", 2, default))!;
        Assert.Equal(draft.ReleaseId, current.Release.AppReleaseId);
        var response = LauncherDistributionController.MapPackage(current);
        Assert.Equal(f.Registration().DownloadUrl, response.DownloadUrl);
        Assert.Equal(2, response.DistributionProtocol);
        Assert.Equal(response.Sha256, response.SetupArtifact!.PackageSha256);
        Assert.Equal($"/api/launcher-distribution/versions/{draft.ReleaseId:D}/setup/download", response.SetupArtifact.DownloadUrl);
        Assert.Null(await f.Service.GetVisibleSetupAsync(draft.ReleaseId, 1, default));
        Assert.NotNull(await f.Service.GetVisibleSetupAsync(draft.ReleaseId, 2, default));
        var controller = new LauncherDistributionController(f.Service, f.Storage);
        var rejected = Assert.IsType<ObjectResult>(await controller.Download(draft.ReleaseId, default));
        Assert.Equal(410, rejected.StatusCode);
        Assert.Equal("desktop_external_direct_download_required", Assert.IsType<ApiErrorResponse>(rejected.Value).Code);
        Assert.DoesNotContain(f.Storage.ResolvedValues, x => x.Contains("://", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Disabled_or_missing_Setup_cannot_hide_a_previous_compatible_release()
    {
        using var f = new Fixture();
        var legacy = await f.Legacy();
        var published = await f.Publish();
        var disabled = f.NewService(f.Context, enabled: false);
        Assert.Equal(legacy, (await disabled.GetLatestPackageAsync("win-x64", "Stable", 2, default))!.Release.AppReleaseId);
        var setup = f.Context.AppReleaseArtifacts.Single(x => x.AppReleaseId == published.ReleaseId && x.Kind == "Setup");
        File.Delete(f.Storage.ResolvePath(setup.RelativePath!));
        Assert.Equal(legacy, (await f.Service.GetLatestPackageAsync("win-x64", "Stable", 2, default))!.Release.AppReleaseId);
        Assert.Null(await f.Service.GetVisibleSetupAsync(published.ReleaseId, 2, default));
    }

    [Fact]
    public async Task Future_inactive_and_other_channel_releases_are_not_distributed()
    {
        using var f = new Fixture();
        var published = await f.Publish(publishedAt: DateTime.UtcNow.AddDays(1));
        Assert.Empty(await f.Service.GetVisiblePackagesAsync("win-x64", "Stable", 2, default));
        Assert.Empty(await f.Service.GetVisiblePackagesAsync("win-x64", "Beta", 2, default));
        Assert.Null(await f.Service.GetVisibleSetupAsync(published.ReleaseId, 2, default));
    }

    [Fact]
    public async Task Deactivation_does_not_unlock_published_artifacts_or_identity()
    {
        using var f = new Fixture();
        var published = await f.Publish();
        var inactive = await f.Service.UpdateAsync(published.ReleaseId, f.Edit(published, active: false), default);
        await Code("desktop_release_immutable", () => f.Service.RegisterExternalAsync(published.ReleaseId,
            f.Registration() with { DownloadUrl = DesktopArtifactDownloadPolicy.ApplicationPrefix + "other.zip" }, default));
        await Code("desktop_external_binding_invalid", () => f.Service.UpdateAsync(published.ReleaseId,
            f.Edit(inactive, active: false) with { BuildNumber = 99 }, default));
        var saves = f.Storage.Saves;
        await Code("desktop_release_immutable", () => f.Upload(published.ReleaseId));
        await Code("desktop_release_immutable", () => f.Service.DeleteAsync(published.ReleaseId, default));
        Assert.Equal(saves, f.Storage.Saves);
        Assert.False((await f.Current(published.ReleaseId)).IsActive);
    }

    [Fact]
    public async Task Sqlite_concurrency_token_prevents_stale_writer_from_deactivating_published_release()
    {
        using var f = new Fixture();
        var draft = await f.Register();
        await f.Upload(draft.ReleaseId);
        var ready = await f.Current(draft.ReleaseId);
        using var staleContext = f.NewContext();
        await staleContext.AppReleases.Include(x => x.Artifacts).SingleAsync(x => x.AppReleaseId == draft.ReleaseId);
        var staleService = f.NewService(staleContext);
        await f.Service.PublishAsync(draft.ReleaseId, ready.Revision, default);
        await Code("desktop_release_changed", () => staleService.UpdateAsync(draft.ReleaseId,
            f.Edit(ready, active: false) with { ReleaseNotes = "stale" }, default));
        using var fresh = f.NewContext();
        var stored = await fresh.AppReleases.SingleAsync(x => x.AppReleaseId == draft.ReleaseId);
        Assert.True(stored.IsActive);
        Assert.NotEqual("stale", stored.ReleaseNotes);
    }

    [Fact]
    public async Task Repair_uses_exact_release_and_never_substitutes_latest_Setup()
    {
        using var f = new Fixture();
        var release = await f.Publish();
        var controller = new DesktopUpdatesController(f.Service);
        var exact = await controller.Repair("1.2.0", 22, cancellationToken: default, distributionProtocol: 2);
        Assert.Equal(release.ReleaseId, Assert.IsType<DesktopReleaseResponse>(Assert.IsType<OkObjectResult>(exact.Result).Value).ReleaseId);
        var missing = await controller.Repair("1.1.0", 21, cancellationToken: default, distributionProtocol: 2);
        Assert.IsType<NotFoundObjectResult>(missing.Result);
        var legacy = await controller.Repair("1.2.0", 22, cancellationToken: default);
        Assert.IsType<NotFoundObjectResult>(legacy.Result);
    }

    [Fact]
    public async Task Deleting_an_external_draft_never_passes_URL_or_null_to_file_storage()
    {
        using var f = new Fixture();
        var draft = await f.Register();
        await f.Service.DeleteAsync(draft.ReleaseId, default);
        Assert.Null(await f.Service.GetAdminReleaseAsync(draft.ReleaseId, default));
        Assert.DoesNotContain(f.Storage.ResolvedValues, x => x.Contains("://", StringComparison.Ordinal));
        Assert.Equal(0, f.Storage.Saves);
    }

    private static async Task Code(string code, Func<Task> action) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<AccountApiException>(action)).Code);

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "der-" + Guid.NewGuid().ToString("N")[..10]);
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly bool _enabled;
        private readonly string _prefix;
        public AccountDbContext Context { get; }
        public RecordingStorage Storage { get; }
        public DesktopReleaseService Service { get; }
        private static readonly byte[] SetupBytes = "MZsetup"u8.ToArray();
        public Fixture(bool enabled = true, string? prefix = null)
        {
            _enabled = enabled; _prefix = prefix ?? DesktopArtifactDownloadPolicy.ApplicationPrefix;
            _connection.Open();
            _connection.CreateFunction("NEWSEQUENTIALID", () => Guid.NewGuid().ToString("D").ToUpperInvariant());
            using var command = _connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys=ON;
                CREATE TABLE AppReleases(AppReleaseId TEXT PRIMARY KEY DEFAULT(NEWSEQUENTIALID()),
                  Version TEXT NOT NULL,BuildNumber INTEGER NOT NULL,Channel TEXT NOT NULL,Platform TEXT NOT NULL,
                  MinimumSupportedDesktopVersion TEXT,DownloadUrl TEXT,Sha256 TEXT,ReleaseNotes TEXT,
                  IsMandatory INTEGER NOT NULL,IsActive INTEGER NOT NULL,PublishedAtUtc TEXT NOT NULL,
                  FirstPublishedAtUtc TEXT,Revision TEXT NOT NULL,
                  UNIQUE(Version,BuildNumber,Channel,Platform));
                CREATE TABLE AppReleaseArtifacts(AppReleaseArtifactId TEXT PRIMARY KEY DEFAULT(NEWSEQUENTIALID()),
                  AppReleaseId TEXT NOT NULL REFERENCES AppReleases(AppReleaseId) ON DELETE CASCADE,
                  Kind TEXT NOT NULL,FileName TEXT NOT NULL,RelativePath TEXT,SizeBytes INTEGER NOT NULL,Sha256 TEXT NOT NULL,
                  CreatedAtUtc TEXT NOT NULL,SourceKind TEXT NOT NULL,ExternalDownloadUrl TEXT,InstallMode TEXT NOT NULL,
                  DistributionProtocol INTEGER NOT NULL,RegistrationEvidenceJson TEXT,BoundPackageSha256 TEXT,ModelBundleId TEXT,VerifiedPreparationId TEXT,
                  UNIQUE(AppReleaseId,Kind));
                """;
            command.ExecuteNonQuery();
            Context = NewContext();
            Storage = new RecordingStorage(new DesktopReleaseStorage(Options.Create(new DesktopReleaseOptions { StorageRoot = Root }), new Host(Root)));
            Service = NewService(Context);
        }
        public AccountDbContext NewContext() => new(new DbContextOptionsBuilder<AccountDbContext>().UseSqlite(_connection).Options);
        public DesktopReleaseService NewService(AccountDbContext db, bool? enabled = null) => new(db, Storage, TimeProvider.System,
            Options.Create(new DesktopReleaseOptions { ExternalVpsEnabled = enabled ?? _enabled, AllowedExternalPrefixes = [_prefix] }));
        public Task<AdminDesktopReleaseResponse> Draft(DateTime? publishedAt = null) => Service.CreateAsync(new("1.2.0", 22,
            "Stable", "win-x64", null, null, false, false, publishedAt ?? DateTime.UtcNow.AddMinutes(-1)), default);
        public async Task<AdminDesktopReleaseResponse> Register(DateTime? publishedAt = null)
        {
            var draft = await Draft(publishedAt);
            await Service.RegisterExternalAsync(draft.ReleaseId, Registration(), default);
            return await Current(draft.ReleaseId);
        }
        public async Task<AdminDesktopReleaseResponse> Publish(DateTime? publishedAt = null)
        {
            var draft = await Register(publishedAt); await Upload(draft.ReleaseId);
            return await Service.PublishAsync(draft.ReleaseId, (await Current(draft.ReleaseId)).Revision, default);
        }
        public async Task Upload(Guid id)
        {
            await using var source = new MemoryStream(SetupBytes);
            await Service.SaveArtifactAsync(id, "Setup", "VideoMaker Setup.exe", source, source.Length, default);
        }
        public async Task<AdminDesktopReleaseResponse> Current(Guid id) => (await Service.GetAdminReleaseAsync(id, default))!;
        public AdminDesktopReleaseRequest Edit(AdminDesktopReleaseResponse r, bool active) => new(r.Version, r.BuildNumber,
            r.Channel, r.Platform, r.MinimumSupportedDesktopVersion, r.ReleaseNotes, r.IsMandatory, active, r.PublishedAtUtc, r.Revision);
        public RegisterExternalDesktopArtifactRequest Registration()
        {
            string[] files = ["TOOL-LOCAL.exe", "update-manifest.json", .. DesktopMediaBundleIntegrity.RequiredRelativePaths];
            return new("1.2.0", 22, "Stable", "win-x64", DesktopArtifactDownloadPolicy.ApplicationPrefix + "1.2.0-22/app.zip",
                "app.zip", 1024, new string('a', 64), new("VideoMaker", "1.2.0", 22, "win-x64", files), 2048,
                100, new("VideoMaker Setup.exe", SetupBytes.Length, Convert.ToHexString(SHA256.HashData(SetupBytes)).ToLowerInvariant()),
                ModelBundleId: ApprovedStaticModelBundle.Current.BundleId);
        }
        public async Task<Guid> Legacy()
        {
            var id = Guid.NewGuid();
            Context.AppReleases.Add(new AppRelease { AppReleaseId = id, Version = "1.1.0", BuildNumber = 21, Channel = "Stable",
                Platform = "win-x64", IsActive = true, PublishedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                Artifacts = [new AppReleaseArtifact { AppReleaseArtifactId = Guid.NewGuid(), AppReleaseId = id, Kind = "DesktopPackage",
                    FileName = "old.zip", RelativePath = "old/package.zip", SizeBytes = 10, Sha256 = new string('b', 64), CreatedAtUtc = DateTime.UtcNow }] });
            await Context.SaveChangesAsync(); return id;
        }
        public void Dispose() { Context.Dispose(); _connection.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private sealed class RecordingStorage(IDesktopReleaseStorage inner) : IDesktopReleaseStorage
    {
        public int Saves; public int Resolves; public List<string> ResolvedValues { get; } = [];
        public Task<StoredDesktopArtifact> SaveAsync(Guid id, string kind, string name, Stream source, long length, CancellationToken token)
        { Saves++; return inner.SaveAsync(id, kind, name, source, length, token); }
        public string ResolvePath(string path) { Resolves++; ResolvedValues.Add(path); return inner.ResolvePath(path); }
        public void DeleteFile(string? path) { if (path is not null) inner.DeleteFile(path); }
        public void DeleteRelease(Guid id) => inner.DeleteRelease(id);
    }
    private sealed class Host(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "DesktopReleaseFixture";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

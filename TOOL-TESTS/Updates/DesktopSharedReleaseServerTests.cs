using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
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
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_TESTS.Updates;

// Durable transitions use real SQLite transactions and separate DbContexts. Only inspection is injected;
// bootstrapper tests read real native VERSIONINFO fixtures without executing them. This is not SQL Server QA.
public sealed class DesktopSharedReleaseServerTests
{
    [Fact]
    public async Task Plain_Publish_metadata_survives_restart_and_is_served_only_for_published_exact_Setup_and_hash()
    {
        using var f = new Fixture(); f.Inspector.PlainPublish = true;
        var setup = await f.UploadSetup(activate: true); var ready = await f.Ready();
        var launcher = new LauncherDistributionController(f.Releases, f.Storage, f.Bootstrappers);
        Assert.IsType<NotFoundResult>(await launcher.PackageMetadata(ready.ReleaseId!.Value, setup.Bootstrapper.BootstrapperId, new string('a', 64)));
        await f.Service.PublishAsync(ready.PreparationId, ready.Revision, default);
        var preparationJson = (await f.Db.DesktopReleasePreparations.AsNoTracking().SingleAsync()).InspectionEvidenceJson;
        Assert.Contains("packageMetadata", preparationJson!);
        await using var restarted = f.NewContext();
        var bootstrapper = new DesktopBootstrapperService(restarted, f.Storage, new DesktopBootstrapperMetadataReader(), Options.Create(f.Options), f.Clock);
        var releases = new DesktopReleaseService(restarted, f.Storage, f.Clock, Options.Create(f.Options), bootstrapper);
        var package = await releases.GetSharedPackageAsync(ready.ReleaseId.Value, setup.Bootstrapper.BootstrapperId, "Production", default);
        var metadata = Assert.IsType<DesktopPackageMetadataDocument>(package!.PackageMetadata);
        var response = LauncherDistributionController.MapPackage(package);
        var reference = Assert.IsType<DesktopPackageMetadataArtifactResponse>(response.PackageMetadata);
        Assert.Equal(package.Artifact.Sha256, reference.ArchiveSha256);
        Assert.Contains($"/bootstrappers/{setup.Bootstrapper.BootstrapperId:D}/package-metadata/", reference.DownloadUrl);
        Assert.DoesNotContain("?", reference.DownloadUrl);
        var restartedLauncher = new LauncherDistributionController(releases, f.Storage, bootstrapper);
        var file = Assert.IsType<FileContentResult>(await restartedLauncher.PackageMetadata(ready.ReleaseId.Value, setup.Bootstrapper.BootstrapperId, reference.Sha256));
        Assert.Equal(reference.SizeBytes, file.FileContents.LongLength);
        Assert.Equal(reference.Sha256, Convert.ToHexString(SHA256.HashData(file.FileContents)).ToLowerInvariant());
        Assert.Equal(DesktopPackageMetadataPolicy.SerializeDocument(metadata), file.FileContents);
        Assert.Equal("publish/", DesktopPackageMetadataPolicy.DeserializeDocument(file.FileContents).PayloadRoot);
        Assert.IsType<NotFoundResult>(await restartedLauncher.PackageMetadata(ready.ReleaseId.Value, setup.Bootstrapper.BootstrapperId, new string('0', 64)));
        Assert.IsType<NotFoundResult>(await restartedLauncher.PackageMetadata(ready.ReleaseId.Value, Guid.NewGuid(), reference.Sha256));
        var current = await f.Current(ready.PreparationId);
        await f.Service.DeactivateAsync(ready.PreparationId, current.Revision, default);
        Assert.IsType<NotFoundResult>(await restartedLauncher.PackageMetadata(ready.ReleaseId.Value, setup.Bootstrapper.BootstrapperId, reference.Sha256));
    }

    [Fact]
    public async Task Plain_Publish_metadata_with_another_archive_hash_cannot_materialize_a_release()
    {
        using var f = new Fixture(); f.Inspector.PlainPublish = true;
        f.Inspector.OnInspect = (request, _, _) =>
        {
            var result = f.Inspector.Result(request);
            return Task.FromResult(result with { PackageMetadata = result.PackageMetadata! with { ArchiveSha256 = new string('b', 64) } });
        };
        var draft = await f.Service.CreateAsync("admin", f.Request(), default);
        await f.Service.ProcessNextAsync(default);
        var failed = await f.Current(draft.PreparationId);
        Assert.Equal("Failed", failed.Status); Assert.Equal("desktop_release_inspection_invalid", failed.ErrorCode);
        Assert.Empty(await f.Db.AppReleases.ToListAsync());
    }

    [Fact]
    public void Mutation_controllers_require_Admin_and_have_no_anonymous_action()
    {
        foreach (var controller in new[] { typeof(AdminDesktopReleasePreparationsController), typeof(AdminDesktopBootstrappersController) })
        {
            Assert.Contains(controller.GetCustomAttributes<AuthorizeAttribute>(true), x => x.Roles == "Admin");
            Assert.Empty(controller.GetMethods().SelectMany(x => x.GetCustomAttributes<AllowAnonymousAttribute>(true)));
        }
        Assert.False(new DesktopReleaseOptions().SharedBootstrapperEnabled);
    }

    [Fact]
    public async Task Disabled_create_does_not_make_a_draft_release_or_start_inspection()
    {
        using var f = new Fixture(enabled: false);
        await Code("desktop_shared_bootstrapper_disabled", () => f.Service.CreateAsync("admin", f.Request(), default));
        Assert.Empty(await f.Db.DesktopReleasePreparations.ToListAsync());
        Assert.Empty(await f.Db.AppReleases.ToListAsync());
        Assert.False(await f.Service.ProcessNextAsync(default));
        Assert.Equal(0, f.Inspector.Calls);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("v1.2.0")]
    [InlineData("1.2.0-preview")]
    [InlineData("1.2.0.1")]
    [InlineData("0.0.0")]
    [InlineData("2147483648.0.0")]
    public async Task Draft_version_is_validated_before_inspection_or_fake_build(string version)
    {
        using var f = new Fixture();
        await Code("desktop_release_version_invalid", () => f.Service.CreateAsync("admin", f.Request(version), default));
        Assert.Empty(await f.Db.AppReleases.ToListAsync()); Assert.Equal(0, f.Inspector.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_idempotency_conflicts_on_different_notes_or_mandatory(bool mandatory)
    {
        using var f = new Fixture(); var request = f.Request() with { ReleaseNotes = "original" };
        var first = await f.Service.CreateAsync("admin", request, default);
        var retry = await f.Service.CreateAsync("admin", request, default);
        Assert.Equal(first.PreparationId, retry.PreparationId); Assert.Equal(first.Revision, retry.Revision);
        await Code("desktop_preparation_request_reused", () => f.Service.CreateAsync("admin", mandatory
            ? request with { IsMandatory = true } : request with { ReleaseNotes = "different" }, default));
        Assert.Single(await f.Db.DesktopReleasePreparations.ToListAsync()); Assert.Empty(await f.Db.AppReleases.ToListAsync());
    }

    [Fact]
    public async Task Original_create_retry_returns_latest_draft_even_after_URL_and_notes_edit()
    {
        using var f = new Fixture(); var request = f.Request();
        var first = await f.Service.CreateAsync("admin", request, default);
        var changed = await f.Service.UpdateAsync(first.PreparationId, "admin", request with
        { Version = "1.3.0", DownloadUrl = Fixture.Url("1.3.0"), ReleaseNotes = "changed", Revision = first.Revision }, default);
        var retried = await f.Service.CreateAsync("admin", request, default);
        Assert.Equal(changed.PreparationId, retried.PreparationId); Assert.Equal("1.3.0", retried.Version);
        Assert.Equal(changed.Revision, retried.Revision); Assert.Equal(2, retried.Generation);
        Assert.Empty(await f.Db.AppReleases.ToListAsync());
    }

    [Fact]
    public async Task Ready_materializes_actual_build_and_proof_inactive_and_requires_system_Setup()
    {
        using var f = new Fixture(); var ready = await f.Ready();
        Assert.Equal("Ready", ready.Status); Assert.False(ready.IsActive); Assert.False(ready.CanPublish);
        Assert.Equal(7, ready.BuildNumber); Assert.NotNull(ready.ReleaseId);
        var artifact = await f.Db.AppReleaseArtifacts.SingleAsync();
        Assert.Null(artifact.RelativePath); Assert.Equal(ready.PreparationId, artifact.VerifiedPreparationId);
        Assert.Equal(3, artifact.DistributionProtocol); Assert.Equal(artifact.Sha256, artifact.BoundPackageSha256);
        await Code("desktop_shared_bootstrapper_required", () => f.Service.PublishAsync(ready.PreparationId, ready.Revision, default));
        Assert.Empty(await f.Releases.GetVisiblePackagesAsync("win-x64", "Stable", 3, default));
        Assert.False((await f.Db.AppReleases.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Two_versions_share_one_native_Setup_and_only_protocol3_sees_them_version_first()
    {
        using var f = new Fixture(); var setup = await f.UploadSetup(activate: true);
        f.Inspector.Build = 900; var a = await f.Ready("1.2.0");
        await f.Service.PublishAsync(a.PreparationId, a.Revision, default);
        f.Inspector.Build = 1; var b = await f.Ready("1.3.0");
        await f.Service.PublishAsync(b.PreparationId, b.Revision, default);
        var visible = await f.Releases.GetVisiblePackagesAsync("win-x64", "Stable", 3, default);
        Assert.Equal(new[] { "1.3.0", "1.2.0" }, visible.Select(x => x.Release.Version));
        Assert.All(visible, x => { Assert.Equal(setup.Bootstrapper.BootstrapperId, x.SharedBootstrapper!.BootstrapperId); Assert.Equal(setup.Bootstrapper.Sha256, x.SharedBootstrapper.Sha256); });
        Assert.Single(await f.Db.DesktopBootstrappers.ToListAsync());
        Assert.Empty(await f.Releases.GetVisiblePackagesAsync("win-x64", "Stable", 1, default));
        Assert.Empty(await f.Releases.GetVisiblePackagesAsync("win-x64", "Stable", 2, default));
        var metadata = LauncherDistributionController.MapPackage(visible[0]);
        Assert.Null(metadata.SetupArtifact); Assert.NotNull(metadata.SharedBootstrapper);
        Assert.Equal(Fixture.Url("1.3.0"), metadata.DownloadUrl);
        var admin = await f.Releases.GetAdminReleaseAsync(b.ReleaseId!.Value, default);
        Assert.False(Assert.Single(admin!.Artifacts).MetadataRegisteredOnly);
    }

    [Fact]
    public async Task Publication_CAS_deactivation_and_immutable_identity_are_preserved()
    {
        using var f = new Fixture(); await f.UploadSetup(activate: true); var ready = await f.Ready();
        await Code("desktop_preparation_changed", () => f.Service.PublishAsync(ready.PreparationId, Guid.NewGuid(), default));
        var published = await f.Service.PublishAsync(ready.PreparationId, ready.Revision, default);
        var firstPublished = (await f.Db.AppReleases.SingleAsync()).FirstPublishedAtUtc;
        var inactive = await f.Service.DeactivateAsync(published.PreparationId, published.Revision, default);
        Assert.False(inactive.IsActive); Assert.True(inactive.Published);
        Assert.Empty(await f.Releases.GetVisiblePackagesAsync("win-x64", "Stable", 3, default));
        await Code("desktop_release_immutable", () => f.Service.UpdateAsync(inactive.PreparationId, "admin", f.Request("1.3.0") with { Revision = inactive.Revision }, default));
        var reactivated = await f.Service.PublishAsync(inactive.PreparationId, inactive.Revision, default);
        Assert.True(reactivated.IsActive); Assert.Equal(firstPublished, (await f.Db.AppReleases.SingleAsync()).FirstPublishedAtUtc);
        await Code("desktop_preparation_managed_release", () => f.Releases.DeleteAsync(ready.ReleaseId!.Value, default));
        using var stream = new MemoryStream("MZfake"u8.ToArray());
        await Code("desktop_preparation_managed_release", () => f.Releases.SaveArtifactAsync(ready.ReleaseId!.Value, "Setup", "other.exe", stream, stream.Length, default));
    }

    [Fact]
    public async Task Editing_ready_identity_invalidates_proof_and_reinspection_updates_only_inactive_release()
    {
        using var f = new Fixture(); var ready = await f.Ready();
        var changed = await f.Service.UpdateAsync(ready.PreparationId, "admin", f.Request("1.3.0") with { Revision = ready.Revision }, default);
        Assert.Equal("Queued", changed.Status); Assert.Null(changed.Sha256); Assert.Null((await f.Db.AppReleaseArtifacts.SingleAsync()).VerifiedPreparationId);
        Assert.False((await f.Db.AppReleases.SingleAsync()).IsActive);
        Assert.True(await f.Service.ProcessNextAsync(default));
        var rechecked = await f.Current(ready.PreparationId);
        Assert.Equal(ready.ReleaseId, rechecked.ReleaseId); Assert.Equal("1.3.0", rechecked.Version); Assert.Equal("Ready", rechecked.Status);
        Assert.Equal(rechecked.PreparationId, (await f.Db.AppReleaseArtifacts.SingleAsync()).VerifiedPreparationId);
    }

    [Fact]
    public async Task Failed_inspection_never_materializes_release_and_sanitizes_untrusted_exception()
    {
        using var f = new Fixture(); var draft = await f.Service.CreateAsync("admin", f.Request(), default);
        f.Inspector.OnInspect = (_, _, _) => throw new InvalidDataException("SECRET fixture diagnostic must stay private");
        Assert.True(await f.Service.ProcessNextAsync(default));
        var failed = await f.Current(draft.PreparationId);
        Assert.Equal("Failed", failed.Status); Assert.DoesNotContain("SECRET", failed.ErrorMessage!);
        Assert.Empty(await f.Db.AppReleases.ToListAsync());
        var retried = await f.Service.RetryAsync(draft.PreparationId, failed.Revision, default);
        Assert.Equal("Queued", retried.Status); Assert.Equal(2, retried.Generation);
        f.Inspector.OnInspect = null; Assert.True(await f.Service.ProcessNextAsync(default));
        Assert.Equal("Ready", (await f.Current(draft.PreparationId)).Status);
    }

    [Theory]
    [InlineData("desktop_preparation_download_failed")]
    [InlineData("desktop_preparation_timeout")]
    public async Task Wrapped_transient_inspection_failure_is_retried_from_durable_queue(string code)
    {
        using var f = new Fixture(); var draft = await f.Service.CreateAsync("admin", f.Request(), default);
        f.Inspector.OnInspect = (_, _, _) => throw new DesktopReleaseInspectionException(code, "safe fixture message");
        await f.Service.ProcessNextAsync(default);
        var queued = await f.Current(draft.PreparationId);
        Assert.Equal("Queued", queued.Status); Assert.Equal(code, queued.ErrorCode);
        Assert.False(await f.Service.ProcessNextAsync(default));
        f.Clock.Advance(TimeSpan.FromSeconds(11)); f.Inspector.OnInspect = null;
        await using var restarted = f.NewContext(); Assert.True(await f.NewService(restarted).ProcessNextAsync(default));
        Assert.Equal("Ready", (await f.Current(draft.PreparationId)).Status); Assert.Equal(2, f.Inspector.Calls);
    }

    [Fact]
    public async Task Wrong_profile_is_rejected_before_listing_check_or_repair_metadata()
    {
        using var f = new Fixture(); await f.UploadSetup(activate: true); var ready = await f.Ready();
        await f.Service.PublishAsync(ready.PreparationId, ready.Revision, default);
        var launcher = new LauncherDistributionController(f.Releases, f.Storage, f.Bootstrappers);
        Assert.Empty((await launcher.Versions(distributionProtocol: 3, profile: "Staging")).Releases);
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>((await launcher.Latest(distributionProtocol: 3, profile: "Staging")).Result);
        var updates = new DesktopUpdatesController(f.Releases);
        Assert.False((await updates.Check("1.0.0", 1, distributionProtocol: 3, profile: "Staging")).IsUpdateAvailable);
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>((await updates.Repair("1.2.0", 7, distributionProtocol: 3, profile: "Staging")).Result);
        Assert.Single((await launcher.Versions(distributionProtocol: 3, profile: "Production")).Releases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_inspection_cannot_commit_after_cancel_or_identity_generation_change(bool cancel)
    {
        using var f = new Fixture(); var draft = await f.Service.CreateAsync("admin", f.Request(), default);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Inspector.OnInspect = async (request, _, _) => { started.SetResult(); await release.Task; return f.Inspector.Result(request); };
        var running = f.Service.ProcessNextAsync(default); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using (var other = f.NewContext())
        {
            var service = f.NewService(other);
            var current = (await service.GetAsync(draft.PreparationId, default))!;
            if (cancel) await service.CancelAsync(draft.PreparationId, current.Revision, default);
            else await service.UpdateAsync(draft.PreparationId, "admin", f.Request("1.3.0") with { Revision = current.Revision }, default);
        }
        release.SetResult(); await running;
        Assert.Empty(await f.Db.AppReleases.ToListAsync());
        var after = await f.Current(draft.PreparationId);
        Assert.Equal(cancel ? "Cancelled" : "Queued", after.Status); Assert.Equal(2, after.Generation);
        f.Inspector.OnInspect = null;
        if (cancel) await f.Service.RetryAsync(after.PreparationId, after.Revision, default);
        Assert.True(await f.Service.ProcessNextAsync(default)); Assert.Single(await f.Db.AppReleases.ToListAsync());
    }

    [Fact]
    public async Task Expired_owner_is_reclaimed_after_restart_but_cannot_complete_before_reclaim()
    {
        using var f = new Fixture(); var draft = await f.Service.CreateAsync("admin", f.Request(), default);
        f.Inspector.OnInspect = (request, _, _) => { f.Clock.Advance(TimeSpan.FromSeconds(91)); return Task.FromResult(f.Inspector.Result(request)); };
        await f.Service.ProcessNextAsync(default);
        Assert.Empty(await f.Db.AppReleases.ToListAsync());
        Assert.Equal("Downloading", (await f.Current(draft.PreparationId)).Status);
        f.Inspector.OnInspect = null;
        await using var restarted = f.NewContext(); await f.NewService(restarted).ProcessNextAsync(default);
        Assert.Equal("Ready", (await f.Current(draft.PreparationId)).Status);
        Assert.Equal(2, (await f.Db.DesktopReleasePreparations.AsNoTracking().SingleAsync()).AttemptCount);
    }

    [Fact]
    public async Task Exhausted_expired_claim_is_failed_without_incrementing_past_database_limit()
    {
        using var f = new Fixture(); var draft = await f.Service.CreateAsync("admin", f.Request(), default);
        var entity = await f.Db.DesktopReleasePreparations.SingleAsync();
        entity.AttemptCount = 5; entity.State = "Downloading"; entity.LeaseOwner = Guid.NewGuid(); entity.LeaseExpiresAtUtc = f.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
        await f.Db.SaveChangesAsync(); await f.Service.ProcessNextAsync(default);
        var failed = await f.Current(draft.PreparationId);
        Assert.Equal("Failed", failed.Status); Assert.Equal("desktop_preparation_attempts_exhausted", failed.ErrorCode);
        Assert.Equal(5, (await f.Db.DesktopReleasePreparations.AsNoTracking().SingleAsync()).AttemptCount); Assert.Equal(0, f.Inspector.Calls);
    }

    [Fact]
    public async Task Heartbeat_records_Inspecting_but_never_resurrects_an_expired_lease()
    {
        using var f = new Fixture(); await f.Service.CreateAsync("admin", f.Request(), default);
        var entity = await f.Db.DesktopReleasePreparations.SingleAsync(); var owner = Guid.NewGuid();
        entity.State = "Downloading"; entity.LeaseOwner = owner; entity.LeaseExpiresAtUtc = f.Clock.GetUtcNow().UtcDateTime.AddSeconds(30);
        await f.Db.SaveChangesAsync();
        var method = typeof(DesktopReleasePreparationService).GetMethod("HeartbeatAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Task<bool> Beat() => (Task<bool>)method.Invoke(f.Service, [entity, owner, new DesktopReleaseInspectionProgress("Inspecting", 33, 50), CancellationToken.None])!;
        Assert.True(await Beat());
        Assert.Equal("Inspecting", (await f.Db.DesktopReleasePreparations.AsNoTracking().SingleAsync()).State);
        f.Clock.Advance(TimeSpan.FromSeconds(91)); Assert.False(await Beat());
        Assert.True((await f.Db.DesktopReleasePreparations.AsNoTracking().SingleAsync()).LeaseExpiresAtUtc < f.Clock.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task Native_Setup_registration_is_file_only_idempotent_and_rejects_different_bytes_at_same_version()
    {
        using var f = new Fixture(); var first = await f.UploadSetup(); var repeated = await f.UploadSetup();
        Assert.Equal(first.Bootstrapper.BootstrapperId, repeated.Bootstrapper.BootstrapperId);
        Assert.Equal(first.Revision, repeated.Revision);
        using var changed = new MemoryStream([.. Fixture.SetupBytes(), 1, 2]);
        await Code("desktop_bootstrapper_immutable", () => f.Bootstrappers.UploadAsync("admin", "Setup.exe", changed, changed.Length, default));
        Assert.Single(await f.Db.DesktopBootstrappers.ToListAsync());
        Assert.Single(Directory.GetFiles(f.StorageRoot, "*.exe", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Native_executable_without_shared_metadata_returns_400_and_cleans_uploaded_file()
    {
        using var f = new Fixture();
        using var source = new MemoryStream(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "DistributionResources", "DistributionResourcesB.exe")));
        var error = await Assert.ThrowsAsync<AccountApiException>(() => f.Bootstrappers.UploadAsync("admin", "Setup.exe", source, source.Length, default));
        Assert.Equal(400, error.StatusCode); Assert.Equal("desktop_bootstrapper_file_invalid", error.Code);
        Assert.Empty(await f.Db.DesktopBootstrappers.ToListAsync());
        Assert.Empty(Directory.GetFiles(f.StorageRoot, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Same_size_corrupt_Setup_cannot_activate_publish_or_download()
    {
        using var f = new Fixture(); var setup = await f.UploadSetup();
        var entity = await f.Db.DesktopBootstrappers.SingleAsync(); var path = f.Storage.ResolvePath(entity.RelativePath);
        var bytes = await File.ReadAllBytesAsync(path); bytes[^1] ^= 1; await File.WriteAllBytesAsync(path, bytes);
        await Code("desktop_bootstrapper_file_invalid", () => f.Bootstrappers.ActivateAsync(entity.BootstrapperId, setup.Revision, default));
        Assert.False((await f.Db.DesktopBootstrappers.AsNoTracking().SingleAsync()).IsActive);
        await File.WriteAllBytesAsync(path, Fixture.SetupBytes());
        await f.Bootstrappers.ActivateAsync(entity.BootstrapperId, setup.Revision, default);
        var ready = await f.Ready(); await File.WriteAllBytesAsync(path, bytes);
        await Code("desktop_bootstrapper_file_invalid", () => f.Service.PublishAsync(ready.PreparationId, ready.Revision, default));
        Assert.False((await f.Db.AppReleases.SingleAsync()).IsActive);
        Assert.Null(await f.Bootstrappers.DownloadAsync(entity.BootstrapperId, default));
    }

    [Fact]
    public async Task Revocation_blocks_distribution_and_exact_Setup_without_deleting_historical_bytes()
    {
        using var f = new Fixture(); var setup = await f.UploadSetup(activate: true); var ready = await f.Ready();
        var published = await f.Service.PublishAsync(ready.PreparationId, ready.Revision, default);
        Assert.NotNull(await f.Releases.GetSharedPackageAsync(ready.ReleaseId!.Value, setup.Bootstrapper.BootstrapperId, "Production", default));
        Assert.Null(await f.Releases.GetSharedPackageAsync(ready.ReleaseId.Value, setup.Bootstrapper.BootstrapperId, "Staging", default));
        await f.Bootstrappers.RevokeAsync(setup.Bootstrapper.BootstrapperId, setup.Revision, default);
        Assert.Empty(await f.Releases.GetVisiblePackagesAsync("win-x64", "Stable", 3, default));
        Assert.Null(await f.Bootstrappers.CompatibleAsync(setup.Bootstrapper.BootstrapperId, "1.0.0", default));
        Assert.Single(Directory.GetFiles(f.StorageRoot, "*.exe", SearchOption.AllDirectories));
        Assert.True(published.IsActive);
    }

    private static async Task Code(string expected, Func<Task> action)
    { var error = await Assert.ThrowsAsync<AccountApiException>(action); Assert.Equal(expected, error.Code); }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "VM-SharedRelease-" + Guid.NewGuid().ToString("N"));
        public string StorageRoot => Path.Combine(Root, "packages");
        public AccountDbContext Db { get; }
        public DbContextOptions<AccountDbContext> ContextOptions { get; }
        public DesktopReleaseOptions Options { get; }
        public MutableClock Clock { get; } = new();
        public FakeInspector Inspector { get; } = new();
        public DesktopReleaseStorage Storage { get; }
        public DesktopBootstrapperService Bootstrappers { get; }
        public DesktopReleasePreparationService Service { get; }
        public DesktopReleaseService Releases { get; }
        public Fixture(bool enabled = true)
        {
            Directory.CreateDirectory(Root);
            ContextOptions = new DbContextOptionsBuilder<AccountDbContext>().UseSqlite($"Data Source={Path.Combine(Root, "fixture.db")};Pooling=False").Options;
            Db = NewContext(); Db.Database.ExecuteSqlRaw(Schema);
            Options = new DesktopReleaseOptions { SharedBootstrapperEnabled = enabled, ApiBaseUrl = "https://metadata.invalid/", Profile = "Production",
                StorageRoot = StorageRoot, AllowedExternalPrefixes = [DesktopArtifactDownloadPolicy.ApplicationPrefix], MaximumPreparationAttempts = 5 };
            Storage = new DesktopReleaseStorage(Microsoft.Extensions.Options.Options.Create(Options), new Host(Root));
            Bootstrappers = NewBootstrapper(Db); Service = NewService(Db);
            Releases = new(Db, Storage, Clock, Microsoft.Extensions.Options.Options.Create(Options), Bootstrappers);
        }
        public AccountDbContext NewContext() => new(ContextOptions);
        private DesktopBootstrapperService NewBootstrapper(AccountDbContext db) => new(db, Storage, new DesktopBootstrapperMetadataReader(), Microsoft.Extensions.Options.Options.Create(Options), Clock);
        public DesktopReleasePreparationService NewService(AccountDbContext db) => new(db, ContextOptions, Inspector, NewBootstrapper(db), Microsoft.Extensions.Options.Options.Create(Options), Clock);
        public static string Url(string version) => DesktopArtifactDownloadPolicy.ApplicationPrefix + version + "/app.zip";
        public DesktopReleasePreparationRequest Request(string version = "1.2.0") => new(version, Url(version), Guid.NewGuid());
        public async Task<DesktopReleasePreparationResponse> Ready(string version = "1.2.0")
        {
            var draft = await Service.CreateAsync("admin", Request(version), default);
            Assert.True(await Service.ProcessNextAsync(default)); return await Current(draft.PreparationId);
        }
        public async Task<DesktopReleasePreparationResponse> Current(Guid id) { Db.ChangeTracker.Clear(); return (await Service.GetAsync(id, default))!; }
        public static byte[] SetupBytes() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "DistributionResources", "DistributionResourcesA.exe"));
        public async Task<DesktopBootstrapperAdminResponse> UploadSetup(bool activate = false)
        {
            using var bytes = new MemoryStream(SetupBytes());
            var result = await Bootstrappers.UploadAsync("admin", "Setup.exe", bytes, bytes.Length, default);
            return activate ? await Bootstrappers.ActivateAsync(result.Bootstrapper.BootstrapperId, result.Revision, default) : result;
        }
        public void Dispose()
        {
            Db.Dispose();
            if (!Path.GetFullPath(Root).StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe owned test root.");
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
        private const string Schema = """
            PRAGMA foreign_keys=ON;
            CREATE TABLE AppReleases(AppReleaseId TEXT PRIMARY KEY,Version TEXT NOT NULL,BuildNumber INTEGER NOT NULL,Channel TEXT NOT NULL,Platform TEXT NOT NULL,
              MinimumSupportedDesktopVersion TEXT,DownloadUrl TEXT,Sha256 TEXT,ReleaseNotes TEXT,IsMandatory INTEGER NOT NULL,IsActive INTEGER NOT NULL,PublishedAtUtc TEXT NOT NULL,
              FirstPublishedAtUtc TEXT,Revision TEXT NOT NULL,UNIQUE(Version,BuildNumber,Channel,Platform));
            CREATE TABLE DesktopReleasePreparations(PreparationId TEXT PRIMARY KEY,ActorUserId TEXT NOT NULL,ClientRequestId TEXT NOT NULL,Version TEXT NOT NULL,DownloadUrl TEXT NOT NULL,
              ReleaseNotes TEXT,IsMandatory INTEGER NOT NULL,Profile TEXT NOT NULL,Channel TEXT NOT NULL,Platform TEXT NOT NULL,Revision TEXT NOT NULL,
              InputFingerprint TEXT NOT NULL,CreateRequestFingerprint TEXT NOT NULL,Generation INTEGER NOT NULL,State TEXT NOT NULL,Stage TEXT NOT NULL,BytesReceived INTEGER NOT NULL,TotalBytes INTEGER,
              AttemptCount INTEGER NOT NULL CHECK(AttemptCount<=5),LeaseOwner TEXT,LeaseExpiresAtUtc TEXT,HeartbeatAtUtc TEXT,NextAttemptAtUtc TEXT,CancellationRequested INTEGER NOT NULL,
              ErrorCode TEXT,ErrorMessage TEXT,InspectionEvidenceJson TEXT,VerifiedAtUtc TEXT,ReleaseId TEXT REFERENCES AppReleases(AppReleaseId),CreatedAtUtc TEXT NOT NULL,UpdatedAtUtc TEXT NOT NULL,
              UNIQUE(ActorUserId,ClientRequestId));
            CREATE UNIQUE INDEX PrepRelease ON DesktopReleasePreparations(ReleaseId) WHERE ReleaseId IS NOT NULL;
            CREATE TABLE AppReleaseArtifacts(AppReleaseArtifactId TEXT PRIMARY KEY,AppReleaseId TEXT NOT NULL REFERENCES AppReleases(AppReleaseId) ON DELETE CASCADE,
              Kind TEXT NOT NULL,FileName TEXT NOT NULL,RelativePath TEXT,SizeBytes INTEGER NOT NULL,Sha256 TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,
              SourceKind TEXT NOT NULL,ExternalDownloadUrl TEXT,InstallMode TEXT NOT NULL,DistributionProtocol INTEGER NOT NULL,RegistrationEvidenceJson TEXT,
              BoundPackageSha256 TEXT,ModelBundleId TEXT,VerifiedPreparationId TEXT REFERENCES DesktopReleasePreparations(PreparationId),UNIQUE(AppReleaseId,Kind));
            CREATE TABLE DesktopBootstrappers(BootstrapperId TEXT PRIMARY KEY,Version TEXT NOT NULL,Profile TEXT NOT NULL,Platform TEXT NOT NULL,Channel TEXT NOT NULL,
              ServerBaseUrl TEXT NOT NULL,ModelBundleId TEXT NOT NULL,DistributionProtocol INTEGER NOT NULL,FileName TEXT NOT NULL,RelativePath TEXT NOT NULL,
              SizeBytes INTEGER NOT NULL,Sha256 TEXT NOT NULL,EmbeddedMetadataJson TEXT NOT NULL,IsActive INTEGER NOT NULL,IsRevoked INTEGER NOT NULL,
              Revision TEXT NOT NULL,CreatedByUserId TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,ActivatedAtUtc TEXT,UNIQUE(Profile,Platform,Channel,Version));
            CREATE UNIQUE INDEX BootstrapperActive ON DesktopBootstrappers(Profile,Platform,Channel) WHERE IsActive=1;
            """;
    }
    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }
    private sealed class FakeInspector : IDesktopReleaseArchiveInspector
    {
        public int Calls;
        public int Build = 7;
        public bool PlainPublish;
        public Func<DesktopReleaseInspectionRequest, IProgress<DesktopReleaseInspectionProgress>?, CancellationToken, Task<DesktopReleaseInspectionResult>>? OnInspect;
        public Task<DesktopReleaseInspectionResult> InspectAsync(DesktopReleaseInspectionRequest request, IProgress<DesktopReleaseInspectionProgress>? progress, CancellationToken token)
        { Calls++; return OnInspect?.Invoke(request, progress, token) ?? Task.FromResult(Result(request)); }
        public DesktopReleaseInspectionResult Result(DesktopReleaseInspectionRequest request)
        {
            if (!PlainPublish) return new("app.zip", 1024, new string('a', 64),
                new("VideoMaker", request.ExpectedVersion, Build, "win-x64", ["TOOL-LOCAL.exe", "update-manifest.json", "distribution-profile.json"]),
                2048, 3, ApprovedStaticModelBundle.Current.BundleId, "1.2.0",
                new(1, 3, "Production", "https://metadata.invalid/", "Stable", "win-x64", ApprovedStaticModelBundle.Current.BundleId, "1.2.0"));
            var files = new[] { "TOOL-LOCAL.exe", "appsettings.json" }.Concat(TOOL_SHARED.Distribution.DesktopMediaBundleIntegrity.RequiredRelativePaths)
                .Order(StringComparer.Ordinal).Select(path => new DesktopPackageFileHash(path, 256, new string('c', 64))).ToArray();
            var manifest = new DesktopUpdateManifest("VideoMaker", request.ExpectedVersion, Build, "win-x64",
                files.Select(x => x.Path).Concat(["update-manifest.json", "distribution-profile.json", "package-inventory.json"]).Order(StringComparer.Ordinal).ToArray());
            var profile = new DesktopPackageDistributionProfile(1, 3, "Production", "https://metadata.invalid/", "Stable", "win-x64", ApprovedStaticModelBundle.Current.BundleId, "1.1.0");
            var metadata = new DesktopPackageMetadataDocument(1, new string('a', 64), "publish/", manifest, profile, files, files.Sum(x => x.SizeBytes), files.Length);
            return new("app.zip", 1024, new string('a', 64), manifest, metadata.ExpandedSizeBytes, metadata.EntryCount,
                ApprovedStaticModelBundle.Current.BundleId, "1.1.0", profile, metadata);
        }
    }
    private sealed class Host(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SharedReleaseFixture";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

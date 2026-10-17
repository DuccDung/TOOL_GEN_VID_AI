using System.Net;
using System.Net.Http.Json;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.Updates;
using TOOL_LOCAL.SystemSetup;
using TOOL_SHARED.Contracts.Authentication;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_TESTS.Updates;

public sealed class StandaloneCatalogDesktopTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-standalone-desktop-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Update_check_keeps_the_standalone_snapshot_typed_without_a_fabricated_shared_Setup_or_archive_size()
    {
        var release = StandaloneDesktopDistributionTests.Release("9.0.0") with { IsMandatory = true };
        var paths = new List<string>();
        using var handler = new Handler(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath); Assert.Contains("catalogProtocol=5", request.RequestUri.Query);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DesktopStandaloneUpdateCheckResponse(true, true, release)) };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        using var session = await SessionAsync();
        var result = await new DesktopUpdateApiClient(http, session, new DesktopUpdateOptions()).CheckAsync(default);
        Assert.True(result.IsUpdateAvailable); Assert.True(result.IsMandatory); Assert.Null(result.Release); Assert.Null(result.DirectRelease);
        Assert.Equal(release, result.StandaloneRelease);
        Assert.Equal("/api/desktop-updates/standalone/check", Assert.Single(paths));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_empty_standalone_catalog_preserves_direct_then_legacy_fallback(bool missing)
    {
        var paths = new List<string>();
        using var handler = new Handler(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath.Contains("/standalone/", StringComparison.Ordinal))
                return missing ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK) { Content = JsonContent.Create(new DesktopStandaloneUpdateCheckResponse(false, false, null)) };
            if (request.RequestUri.AbsolutePath.Contains("/direct/", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DesktopDirectUpdateCheckResponse(false, false, null)) };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DesktopUpdateCheckResponse(false, false, null)) };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        using var session = await SessionAsync();
        var result = await new DesktopUpdateApiClient(http, session, new DesktopUpdateOptions()).CheckAsync(default);
        Assert.False(result.IsUpdateAvailable); Assert.Null(result.Release); Assert.Null(result.DirectRelease); Assert.Null(result.StandaloneRelease);
        Assert.Equal(new[] { "/api/desktop-updates/standalone/check", "/api/desktop-updates/direct/check", "/api/desktop-updates/check" }, paths);
    }

    [Fact]
    public async Task Repair_queries_and_requires_the_exact_installed_version_and_build()
    {
        var release = StandaloneDesktopDistributionTests.Release(DesktopBuildInfo.Version, DesktopBuildInfo.BuildNumber);
        using var handler = new Handler(request =>
        {
            Assert.Equal("/api/desktop-updates/standalone/repair", request.RequestUri!.AbsolutePath);
            Assert.Contains("version=" + Uri.EscapeDataString(DesktopBuildInfo.Version), request.RequestUri.Query);
            Assert.Contains("buildNumber=" + DesktopBuildInfo.BuildNumber, request.RequestUri.Query);
            Assert.Contains("catalogProtocol=5", request.RequestUri.Query);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(release) };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        using var session = await SessionAsync();
        Assert.Equal(release, await new DesktopUpdateApiClient(http, session, new DesktopUpdateOptions()).GetStandaloneRepairReleaseAsync(default));
    }

    [Fact]
    public async Task A_standalone_unauthorized_response_invalidates_the_session_without_falling_back()
    {
        var paths = new List<string>();
        using var handler = new Handler(request => { paths.Add(request.RequestUri!.AbsolutePath); return new(HttpStatusCode.Unauthorized); });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        using var session = await SessionAsync();
        await Assert.ThrowsAsync<AccountClientException>(() => new DesktopUpdateApiClient(http, session, new DesktopUpdateOptions()).CheckAsync(default));
        Assert.False(session.IsAuthenticated); Assert.Single(paths);
    }

    [Fact]
    public async Task Missing_optional_Setup_pointer_gives_an_actionable_error_before_network_or_install_ownership_reads()
    {
        var service = new DesktopInstalledMaintenanceService(new Uri("https://api.example.test/"));
        var error = await Assert.ThrowsAsync<SetupException>(() => service.StartStandaloneAsync(
            StandaloneDesktopDistributionTests.Release("9.0.0"), "update", null, default));
        Assert.Equal("desktop_setup_download_unavailable", error.Code);
        Assert.Contains("Setup", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData("api")]
    [InlineData("version")]
    [InlineData("hash")]
    public async Task A_malformed_standalone_update_is_rejected_without_silently_selecting_a_legacy_release(string mutation)
    {
        var release = StandaloneDesktopDistributionTests.Release("9.0.0");
        release = mutation switch { "api" => release with { ApiBaseUrl = "https://outside.invalid/" },
            "version" => StandaloneDesktopDistributionTests.Release(DesktopBuildInfo.Version), _ => release with { ExpectedSha256 = new string('f', 64) } };
        var calls = 0;
        using var handler = new Handler(_ => { calls++; return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DesktopStandaloneUpdateCheckResponse(true, false, release)) }; });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        using var session = await SessionAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => new DesktopUpdateApiClient(http, session, new DesktopUpdateOptions()).CheckAsync(default));
        Assert.Equal(1, calls); Assert.True(session.IsAuthenticated);
    }

    private async Task<AccountSessionManager> SessionAsync()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = JsonContent.Create(new AuthTokenResponse(
            "synthetic-access", DateTime.UtcNow.AddMinutes(10), "synthetic-refresh", DateTime.UtcNow.AddDays(1), Guid.NewGuid(), Guid.NewGuid(),
            new UserProfileResponse("fixture", "fixture@example.test", "Fixture", "Active", ["Admin"]))) });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        var session = new AccountSessionManager(new AccountApiClient(http), new MemoryStore(),
            new DeviceIdentityService(Path.Combine(_root, Guid.NewGuid().ToString("N"), "device.bin")));
        Assert.True(await session.TryRestoreAsync()); return session;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (!request.RequestUri!.AbsolutePath.StartsWith("/api/auth/", StringComparison.Ordinal))
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            return Task.FromResult(reply(request));
        }
    }
    private sealed class MemoryStore : ITokenStore
    {
        public Task<StoredRefreshToken?> LoadAsync(CancellationToken token = default) => Task.FromResult<StoredRefreshToken?>(new("synthetic-refresh", DateTime.UtcNow.AddDays(1)));
        public Task SaveAsync(StoredRefreshToken value, CancellationToken token = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken token = default) => Task.CompletedTask;
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

using System.Net;
using System.Net.Http.Json;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.Updates;
using TOOL_SHARED.Contracts.Authentication;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_TESTS.Updates;

public sealed class DirectCatalogDesktopTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "direct-desktop-api-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UpdateCheckKeepsTheDirectReleaseTypedWithoutInventingSizeOrLegacyMetadata()
    {
        var release = DirectCatalogSetupTests.Release(); var paths = new List<string>();
        using var handler = new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/standalone/", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
            paths.Add(request.RequestUri!.AbsolutePath);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DesktopDirectUpdateCheckResponse(true, true, release)) };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.invalid/") };
        using var session = await SessionAsync();
        var client = new DesktopUpdateApiClient(http, session, new DesktopUpdateOptions());
        var result = await client.CheckAsync(CancellationToken.None);
        Assert.True(result.IsUpdateAvailable); Assert.True(result.IsMandatory); Assert.Null(result.Release);
        Assert.Equal(release, result.DirectRelease); Assert.Equal("/api/desktop-updates/direct/check", Assert.Single(paths));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrEmptyDirectCatalogStillSupportsLegacyUpdateChecks(bool missing)
    {
        var paths = new List<string>();
        using var handler = new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/standalone/", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
            paths.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath.EndsWith("/direct/check")
                ? missing ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK) { Content = JsonContent.Create(new DesktopDirectUpdateCheckResponse(false, false, null)) }
                : new(HttpStatusCode.OK) { Content = JsonContent.Create(new DesktopUpdateCheckResponse(false, false, null)) };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.invalid/") };
        using var session = await SessionAsync();
        var result = await new DesktopUpdateApiClient(http, session, new DesktopUpdateOptions()).CheckAsync(CancellationToken.None);
        Assert.False(result.IsUpdateAvailable); Assert.Null(result.Release); Assert.Null(result.DirectRelease);
        Assert.Equal(new[] { "/api/desktop-updates/direct/check", "/api/desktop-updates/check" }, paths);
    }

    [Fact]
    public async Task DirectRepairUsesTheExactCurrentVersionAndBuildQuery()
    {
        var release = DirectCatalogSetupTests.Release(DesktopBuildInfo.Version, DesktopBuildInfo.BuildNumber);
        using var handler = new Handler(request =>
        {
            Assert.Equal("/api/desktop-updates/direct/repair", request.RequestUri!.AbsolutePath);
            Assert.Contains("version=" + Uri.EscapeDataString(DesktopBuildInfo.Version), request.RequestUri.Query);
            Assert.Contains("buildNumber=" + DesktopBuildInfo.BuildNumber, request.RequestUri.Query);
            Assert.Contains("catalogProtocol=4", request.RequestUri.Query);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(release) };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.invalid/") };
        using var session = await SessionAsync();
        Assert.Equal(release, await new DesktopUpdateApiClient(http, session, new DesktopUpdateOptions()).GetDirectRepairReleaseAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UnauthorizedDirectCatalogInvalidatesTheSession()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.invalid/") };
        using var session = await SessionAsync();
        await Assert.ThrowsAsync<AccountClientException>(() => new DesktopUpdateApiClient(http, session,
            new DesktopUpdateOptions()).CheckAsync(CancellationToken.None));
        Assert.False(session.IsAuthenticated);
    }

    private async Task<AccountSessionManager> SessionAsync()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = JsonContent.Create(new AuthTokenResponse(
            "synthetic-access", DateTime.UtcNow.AddMinutes(10), "synthetic-refresh", DateTime.UtcNow.AddDays(1), Guid.NewGuid(), Guid.NewGuid(),
            new UserProfileResponse("fixture", "fixture@example.test", "Fixture", "Active", ["Admin"]))) });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.invalid/") };
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
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}

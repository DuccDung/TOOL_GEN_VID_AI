using System.Net;
using System.Net.Http.Json;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.WebView;
using TOOL_SHARED.Contracts.Accounts;
using TOOL_SHARED.Contracts.Authentication;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_TESTS.Authentication;

public sealed class LicenseSessionManagerTests
{
    [Fact]
    public async Task InitializeAsync_MissingLicense_KeepsSessionAndDoesNotActivateOrHeartbeat()
    {
        var now = DateTime.UtcNow;
        var handler = new RecordingHandler(_ => JsonResponse(MissingLicense(now)));
        using var session = await CreateAuthenticatedSessionAsync();
        using var httpClient = CreateHttpClient(handler);
        await using var manager = new LicenseSessionManager(new LicenseApiClient(httpClient, session));

        await manager.InitializeAsync();

        Assert.True(session.IsAuthenticated);
        Assert.True(manager.IsLocked);
        Assert.Equal(LicenseAccessStates.Missing, manager.Current?.AccessState);
        Assert.Equal(["GET /api/license/current"], handler.Requests);
    }

    [Fact]
    public async Task RefreshNowAsync_LockedLicenseBecomesActiveWithoutRestartingSession()
    {
        var now = DateTime.UtcNow;
        var responses = new Queue<HttpResponseMessage>(
        [
            JsonResponse(MissingLicense(now)),
            JsonResponse(ActiveLicense(now, currentDeviceActivated: false)),
            JsonResponse(ActiveLicense(now, currentDeviceActivated: true)),
            JsonResponse(ActiveLicense(now, currentDeviceActivated: true))
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var session = await CreateAuthenticatedSessionAsync();
        using var httpClient = CreateHttpClient(handler);
        await using var manager = new LicenseSessionManager(new LicenseApiClient(httpClient, session));
        await manager.InitializeAsync();

        var result = await manager.RefreshNowAsync();

        Assert.True(session.IsAuthenticated);
        Assert.True(result.HasActiveLicense);
        Assert.True(result.CurrentDeviceActivated);
        Assert.True(manager.HasValidLease);
        Assert.Equal(
            [
                "GET /api/license/current",
                "GET /api/license/current",
                "POST /api/license/activate-current-device",
                "POST /api/license/heartbeat"
            ],
            handler.Requests);
    }

    [Fact]
    public async Task InitializeAsync_DeviceLimit_ReturnsLockedStateInsteadOfClosingApp()
    {
        var now = DateTime.UtcNow;
        var handler = new RecordingHandler(request =>
            request.RequestUri?.AbsolutePath == "/api/license/current"
                ? JsonResponse(ActiveLicense(now, currentDeviceActivated: false))
                : JsonResponse(
                    new ApiErrorResponse("device_limit_reached", "License đã đạt số thiết bị tối đa."),
                    HttpStatusCode.Conflict));
        using var session = await CreateAuthenticatedSessionAsync();
        using var httpClient = CreateHttpClient(handler);
        await using var manager = new LicenseSessionManager(new LicenseApiClient(httpClient, session));

        await manager.InitializeAsync();

        Assert.True(manager.IsLocked);
        Assert.Equal(LicenseAccessStates.DeviceLimit, manager.Current?.AccessState);
        Assert.Equal("device_limit_reached", manager.Current?.AccessReasonCode);
        Assert.Equal(
            ["GET /api/license/current", "POST /api/license/activate-current-device"],
            handler.Requests);
    }

    [Fact]
    public async Task ConcurrentSessionLimit_NotifiesImmediatelyLocksAccessAndRecoversOnlyAfterSuccessfulHeartbeat()
    {
        var now = DateTime.UtcNow;
        var rejected = false;
        var handler = new RecordingHandler(request => request.RequestUri?.AbsolutePath == "/api/license/heartbeat" && rejected
            ? JsonResponse(new ApiErrorResponse("concurrent_session_limit", "Gói đã đạt số phiên chạy đồng thời tối đa."), HttpStatusCode.Conflict)
            : JsonResponse(ActiveLicense(now, true)));
        using var session = await CreateAuthenticatedSessionAsync();
        using var httpClient = CreateHttpClient(handler);
        await using var manager = new LicenseSessionManager(new LicenseApiClient(httpClient, session));
        var notifications = new List<CurrentLicenseResponse?>();
        manager.LicenseInvalidated += _ => notifications.Add(manager.Current);
        await manager.InitializeAsync();
        Assert.True(manager.HasValidLease);

        rejected = true;
        var locked = await manager.RefreshNowAsync();

        Assert.True(session.IsAuthenticated);
        Assert.True(manager.IsLocked);
        Assert.True(locked.HasActiveLicense);
        Assert.True(locked.CurrentDeviceActivated);
        Assert.Null(locked.LeaseExpiresAtUtc);
        Assert.Equal(LicenseAccessStates.SessionLimit, locked.AccessState);
        Assert.Equal("concurrent_session_limit", Assert.Single(notifications)?.AccessReasonCode);
        await Assert.ThrowsAsync<AccountClientException>(() => manager.EnsureAccessAsync());
        Assert.Single(notifications);

        rejected = false;
        await manager.RefreshNowAsync();
        Assert.True(manager.HasValidLease);
        Assert.Equal(LicenseAccessStates.Active, manager.Current?.AccessState);
        rejected = true;
        await manager.RefreshNowAsync();
        Assert.Equal(2, notifications.Count);
    }

    [Theory]
    [InlineData(409, "concurrent_session_limit", LicenseAccessStates.SessionLimit)]
    [InlineData(403, "session_unavailable", LicenseAccessStates.Unavailable)]
    [InlineData(423, "license_suspended", LicenseAccessStates.Suspended)]
    public async Task InitializeAsync_ExpectedLicenseDenial_KeepsAuthenticatedSessionForLogout(int status, string code, string state)
    {
        var handler = new RecordingHandler(request => request.RequestUri?.AbsolutePath == "/api/license/current"
            ? JsonResponse(ActiveLicense(DateTime.UtcNow, true))
            : JsonResponse(new ApiErrorResponse(code, "Không thể tiếp tục phiên."), (HttpStatusCode)status));
        using var session = await CreateAuthenticatedSessionAsync();
        using var httpClient = CreateHttpClient(handler);
        await using var manager = new LicenseSessionManager(new LicenseApiClient(httpClient, session));

        await manager.InitializeAsync();

        Assert.True(manager.IsLocked);
        Assert.True(session.IsAuthenticated);
        Assert.Equal(state, manager.Current?.AccessState);
        Assert.Null(manager.Current?.LeaseExpiresAtUtc);
    }

    [Fact]
    public async Task UnauthorizedHeartbeat_StillClearsAuthentication()
    {
        var handler = new RecordingHandler(request => request.RequestUri?.AbsolutePath == "/api/license/current"
            ? JsonResponse(ActiveLicense(DateTime.UtcNow, true))
            : JsonResponse(new ApiErrorResponse("invalid_access_token", "Phiên đăng nhập đã hết hạn."), HttpStatusCode.Unauthorized));
        using var session = await CreateAuthenticatedSessionAsync();
        using var httpClient = CreateHttpClient(handler);
        await using var manager = new LicenseSessionManager(new LicenseApiClient(httpClient, session));

        await Assert.ThrowsAsync<AccountClientException>(() => manager.InitializeAsync());

        Assert.False(session.IsAuthenticated);
        Assert.True(manager.IsLocked);
    }

    [Fact]
    public async Task LockedSession_LogoutBridgeRevokesOnlyCurrentSessionAndReturnsToLogin()
    {
        var logoutRequests = new List<LogoutRequest>();
        using var session = await CreateAuthenticatedSessionAsync(logoutRequests.Add);
        var handler = new RecordingHandler(request => request.RequestUri?.AbsolutePath == "/api/license/current"
            ? JsonResponse(ActiveLicense(DateTime.UtcNow, true))
            : JsonResponse(new ApiErrorResponse("concurrent_session_limit", "Đã đạt giới hạn phiên."), HttpStatusCode.Conflict));
        using var httpClient = CreateHttpClient(handler);
        await using var manager = new LicenseSessionManager(new LicenseApiClient(httpClient, session));
        await manager.InitializeAsync();
        var messages = new List<string>();
        var returnedToLogin = false;
        using var bridge = new DashboardBridge(session, manager, null!, null!, null!, null!, null!, null!, false,
            messages.Add, () => returnedToLogin = true);

        await bridge.HandleAsync("""{"type":"auth.logout","requestId":"logout-test","payload":{}}""");

        Assert.False(session.IsAuthenticated);
        Assert.True(returnedToLogin);
        Assert.False(Assert.Single(logoutRequests).RevokeAllSessions);
        using var response = System.Text.Json.JsonDocument.Parse(Assert.Single(messages));
        Assert.Equal("auth.loggedOut", response.RootElement.GetProperty("type").GetString());
        Assert.Equal("logout-test", response.RootElement.GetProperty("requestId").GetString());
    }

    private static CurrentLicenseResponse MissingLicense(DateTime now) => new(
        false,
        null,
        null,
        null,
        null,
        null,
        null,
        0,
        0,
        0,
        null,
        false,
        now,
        null,
        300,
        LicenseAccessStates.Missing,
        "license_missing",
        "Tài khoản chưa có gói sử dụng.");

    private static CurrentLicenseResponse ActiveLicense(DateTime now, bool currentDeviceActivated) => new(
        true,
        Guid.Parse("7f900881-7617-443d-917f-98fb454a0619"),
        "monthly",
        "Gói tháng",
        "Active",
        now.AddDays(-1),
        now.AddDays(29),
        1,
        currentDeviceActivated ? 1 : 0,
        0,
        "{}",
        currentDeviceActivated,
        now,
        currentDeviceActivated ? now.AddMinutes(10) : null,
        300,
        LicenseAccessStates.Active,
        null,
        null);

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://server.example.test/")
    };

    private static HttpResponseMessage JsonResponse<T>(
        T body,
        HttpStatusCode statusCode = HttpStatusCode.OK) => new(statusCode)
        {
            Content = JsonContent.Create(body)
        };

    private static async Task<AccountSessionManager> CreateAuthenticatedSessionAsync(Action<LogoutRequest>? onLogout = null)
    {
        var response = new AuthTokenResponse(
            "access-token",
            DateTime.UtcNow.AddMinutes(10),
            "refresh-token",
            DateTime.UtcNow.AddDays(1),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new UserProfileResponse(
                "user-1",
                "user@example.test",
                "Test User",
                "Active",
                ["User"]));
        var store = new MemoryTokenStore(new StoredRefreshToken("initial-refresh", DateTime.UtcNow.AddDays(1)));
        var session = new AccountSessionManager(
            new RestoreAccountApiClient(response, onLogout),
            store,
            new DeviceIdentityService());
        Assert.True(await session.TryRestoreAsync());
        return session;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method.Method} {request.RequestUri?.AbsolutePath}");
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class RestoreAccountApiClient(AuthTokenResponse response, Action<LogoutRequest>? onLogout = null) : IAccountApiClient
    {
        public Task<AuthTokenResponse> RefreshAsync(
            RefreshTokenRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(response);

        public Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AuthTokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task LogoutAsync(string accessToken, LogoutRequest request, CancellationToken cancellationToken = default)
        {
            onLogout?.Invoke(request);
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryTokenStore(StoredRefreshToken? stored) : ITokenStore
    {
        private StoredRefreshToken? _stored = stored;

        public Task<StoredRefreshToken?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_stored);

        public Task SaveAsync(StoredRefreshToken token, CancellationToken cancellationToken = default)
        {
            _stored = token;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            _stored = null;
            return Task.CompletedTask;
        }
    }
}

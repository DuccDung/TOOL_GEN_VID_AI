using System.Net;
using System.Net.Http.Json;
using TOOL_LOCAL.Authentication;
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

    private static async Task<AccountSessionManager> CreateAuthenticatedSessionAsync()
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
            new RestoreAccountApiClient(response),
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

    private sealed class RestoreAccountApiClient(AuthTokenResponse response) : IAccountApiClient
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

        public Task LogoutAsync(string accessToken, LogoutRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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

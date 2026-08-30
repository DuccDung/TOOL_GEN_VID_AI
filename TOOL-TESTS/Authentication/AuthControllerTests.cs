using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Controllers;
using TOOL_SHARED.Contracts.Authentication;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_TESTS.Authentication;

public sealed class AuthControllerTests
{
    [Fact]
    public async Task Register_ReturnsStructuredConflictForExpectedRegistrationError()
    {
        var service = new StubAuthService(new AccountApiException(
            StatusCodes.Status409Conflict,
            "email_already_exists",
            "Email này đã được sử dụng.",
            new Dictionary<string, string[]> { ["email"] = ["Email này đã được sử dụng."] }));
        var controller = new AuthController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "trace-controller-001"
                }
            }
        };
        var request = new RegisterRequest(
            "existing@example.com",
            "StrongPass1!",
            null,
            new DeviceRegistrationRequest("fingerprint", "device", "Windows", "1.0"));

        var response = await controller.Register(request, CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(result.Value);
        Assert.Equal("email_already_exists", error.Code);
        Assert.Equal("trace-controller-001", error.TraceId);
        Assert.Equal("Email này đã được sử dụng.", Assert.Single(error.Errors!["email"]));
    }

    [Fact]
    public async Task Login_ReturnsStructuredUnauthorizedForInvalidCredentials()
    {
        var service = new StubAuthService(loginResult: AuthLoginResult.Rejected(new AuthFailure(
            StatusCodes.Status401Unauthorized,
            "invalid_credentials",
            "Email hoặc mật khẩu không đúng.")));
        var controller = new AuthController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "trace-login-001"
                }
            }
        };
        var request = new LoginRequest(
            "user@example.com",
            "wrong-password",
            new DeviceRegistrationRequest("fingerprint", "device", "Windows", "1.0"));

        var response = await controller.Login(request, CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(result.Value);
        Assert.Equal("invalid_credentials", error.Code);
        Assert.Equal("trace-login-001", error.TraceId);
    }

    [Fact]
    public async Task Refresh_ReturnsStructuredUnauthorizedForInvalidRefreshToken()
    {
        var service = new StubAuthService(refreshException: new AccountApiException(
            StatusCodes.Status401Unauthorized,
            "invalid_refresh_token",
            "Refresh token không hợp lệ hoặc đã hết hạn."));
        var controller = new AuthController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "trace-refresh-001"
                }
            }
        };

        var response = await controller.Refresh(
            new RefreshTokenRequest("revoked-token"),
            CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(result.Value);
        Assert.Equal("invalid_refresh_token", error.Code);
        Assert.Equal("trace-refresh-001", error.TraceId);
    }

    private sealed class StubAuthService(
        AccountApiException? registerException = null,
        AuthLoginResult? loginResult = null,
        AccountApiException? refreshException = null) : IAuthService
    {
        public Task<AuthTokenResponse> RegisterAsync(
            RegisterRequest request,
            ClientRequestContext client,
            CancellationToken cancellationToken) => Task.FromException<AuthTokenResponse>(
                (Exception?)registerException ?? new NotSupportedException());

        public Task<AuthLoginResult> LoginAsync(
            LoginRequest request,
            ClientRequestContext client,
            CancellationToken cancellationToken) => Task.FromResult(
                loginResult ?? throw new NotSupportedException());

        public Task<AuthTokenResponse> RefreshAsync(
            RefreshTokenRequest request,
            ClientRequestContext client,
            CancellationToken cancellationToken) => Task.FromException<AuthTokenResponse>(
                (Exception?)refreshException ?? new NotSupportedException());

        public Task LogoutAsync(
            string userId,
            Guid currentSessionId,
            LogoutRequest request,
            ClientRequestContext client,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UserProfileResponse> GetProfileAsync(
            string userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

using System.Net;
using System.Net.Http.Json;
using TOOL_LOCAL.Authentication;
using TOOL_SHARED.Contracts.Authentication;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_TESTS.Authentication;

public sealed class AccountApiClientTests
{
    [Fact]
    public async Task RegisterAsync_PreservesServerErrorsAndTraceId()
    {
        var errorResponse = new ApiErrorResponse(
            "email_already_exists",
            "Email này đã được sử dụng.",
            new Dictionary<string, string[]> { ["email"] = ["Email này đã được sử dụng."] },
            "trace-registration-001");
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(errorResponse)
            }))
        {
            BaseAddress = new Uri("https://localhost/")
        };
        var client = new AccountApiClient(httpClient);
        var request = new RegisterRequest(
            "existing@example.com",
            "StrongPass1!",
            null,
            new DeviceRegistrationRequest("fingerprint", "device", "Windows", "1.0"));

        var exception = await Assert.ThrowsAsync<AccountClientException>(() => client.RegisterAsync(request));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("email_already_exists", exception.Code);
        Assert.Equal("trace-registration-001", exception.TraceId);
        Assert.Equal("Email này đã được sử dụng.", Assert.Single(exception.Errors["email"]));
    }

    [Fact]
    public async Task LoginAsync_PreservesInvalidCredentialsResponse()
    {
        var errorResponse = new ApiErrorResponse(
            "invalid_credentials",
            "Email hoặc mật khẩu không đúng.",
            null,
            "trace-login-001");
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = JsonContent.Create(errorResponse)
            }))
        {
            BaseAddress = new Uri("https://localhost/")
        };
        var client = new AccountApiClient(httpClient);
        var request = new LoginRequest(
            "user@example.com",
            "wrong-password",
            new DeviceRegistrationRequest("fingerprint", "device", "Windows", "1.0"));

        var exception = await Assert.ThrowsAsync<AccountClientException>(() => client.LoginAsync(request));

        Assert.Equal((int)HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal("invalid_credentials", exception.Code);
        Assert.Equal("trace-login-001", exception.TraceId);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_PostsEmailToAnonymousEndpoint()
    {
        HttpMethod? method = null;
        string? path = null;
        ForgotPasswordRequest? payload = null;
        using var httpClient = new HttpClient(new CallbackHttpMessageHandler(async request =>
        {
            method = request.Method;
            path = request.RequestUri?.AbsolutePath;
            payload = await request.Content!.ReadFromJsonAsync<ForgotPasswordRequest>();
            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = JsonContent.Create(new ForgotPasswordResponse("accepted"))
            };
        }))
        {
            BaseAddress = new Uri("https://localhost/")
        };
        var client = new AccountApiClient(httpClient);

        await client.RequestPasswordResetAsync(new ForgotPasswordRequest("user@example.com"));

        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("/api/auth/forgot-password", path);
        Assert.Equal("user@example.com", payload?.Email);
    }

    [Fact]
    public async Task ResetPasswordAsync_PostsOtpAndNewPassword()
    {
        ResetPasswordRequest? payload = null;
        using var httpClient = new HttpClient(new CallbackHttpMessageHandler(async request =>
        {
            Assert.Equal("/api/auth/reset-password", request.RequestUri?.AbsolutePath);
            payload = await request.Content!.ReadFromJsonAsync<ResetPasswordRequest>();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }))
        {
            BaseAddress = new Uri("https://localhost/")
        };
        var client = new AccountApiClient(httpClient);

        await client.ResetPasswordAsync(new ResetPasswordRequest(
            "user@example.com",
            "123456",
            "NewStrongPass1!"));

        Assert.Equal("user@example.com", payload?.Email);
        Assert.Equal("123456", payload?.Otp);
        Assert.Equal("NewStrongPass1!", payload?.NewPassword);
    }

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class CallbackHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => callback(request);
    }
}

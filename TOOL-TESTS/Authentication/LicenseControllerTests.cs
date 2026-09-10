using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Controllers;
using TOOL_SHARED.Contracts.Accounts;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_TESTS.Authentication;

public sealed class LicenseControllerTests
{
    [Theory]
    [InlineData("heartbeat", 409, "concurrent_session_limit")]
    [InlineData("heartbeat", 403, "session_unavailable")]
    [InlineData("activate", 409, "device_limit_reached")]
    [InlineData("activate", 403, "license_required")]
    [InlineData("current", 423, "account_locked")]
    public async Task ExpectedDenial_ReturnsStructuredResponseWithoutEscapingController(string operation, int status, string code)
    {
        var controller = CreateController(new StubAccountService(new AccountApiException(status, code, "Không thể tiếp tục phiên này.")));

        var response = await Invoke(controller, operation);

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(status, result.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(result.Value);
        Assert.Equal(code, error.Code);
        Assert.Equal("Không thể tiếp tục phiên này.", error.Message);
        Assert.Equal("license-test", error.TraceId);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("activate")]
    [InlineData("heartbeat")]
    public async Task SuccessfulRequest_PreservesLicenseResponse(string operation)
    {
        var controller = CreateController(new StubAccountService());

        var response = await Invoke(controller, operation);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Same(StubAccountService.License, result.Value);
    }

    [Fact]
    public async Task UnexpectedFailure_RemainsVisibleToGlobalErrorHandler()
    {
        var controller = CreateController(new StubAccountService(new InvalidOperationException("unexpected")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Heartbeat(CancellationToken.None));
    }

    private static Task<ActionResult<CurrentLicenseResponse>> Invoke(LicenseController controller, string operation) => operation switch
    {
        "current" => controller.GetCurrent(CancellationToken.None),
        "activate" => controller.ActivateCurrentDevice(CancellationToken.None),
        _ => controller.Heartbeat(CancellationToken.None)
    };

    private static LicenseController CreateController(IAccountManagementService service) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "license-test",
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "user"),
                    new Claim(AuthClaimTypes.DeviceId, Guid.NewGuid().ToString()),
                    new Claim(AuthClaimTypes.SessionId, Guid.NewGuid().ToString())
                ], "test"))
            }
        }
    };

    private sealed class StubAccountService(Exception? failure = null) : IAccountManagementService
    {
        internal static readonly CurrentLicenseResponse License = new(true, Guid.NewGuid(), "monthly", "Gói tháng", "Active",
            null, null, 1, 1, 0, "{}", true, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(15));
        private Task<CurrentLicenseResponse> Result() => failure is null
            ? Task.FromResult(License) : Task.FromException<CurrentLicenseResponse>(failure);
        public Task<CurrentLicenseResponse> GetCurrentLicenseAsync(string userId, Guid deviceId, CancellationToken ct) => Result();
        public Task<CurrentLicenseResponse> ActivateCurrentDeviceAsync(string userId, Guid deviceId, Guid sessionId, CancellationToken ct) => Result();
        public Task<CurrentLicenseResponse> VerifyHeartbeatAsync(string userId, Guid deviceId, Guid sessionId, CancellationToken ct) => Result();
        public Task<IReadOnlyList<RegisteredDeviceResponse>> GetDevicesAsync(string userId, Guid deviceId, CancellationToken ct) => throw new NotSupportedException();
        public Task RevokeDeviceAsync(string userId, Guid deviceId, CancellationToken ct) => throw new NotSupportedException();
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Accounts;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/licenses")]
public sealed class AdminLicensesController(IAdminLicenseService licenseService) : ControllerBase
{
    [HttpGet("overview")]
    public Task<AdminLicenseOverviewResponse> GetOverview(CancellationToken cancellationToken) =>
        licenseService.GetOverviewAsync(cancellationToken);

    [HttpGet("plans")]
    public Task<IReadOnlyList<AdminLicensePlanResponse>> GetPlans(CancellationToken cancellationToken) =>
        licenseService.GetPlansAsync(cancellationToken);

    [HttpGet("payments")]
    public Task<IReadOnlyList<AdminLicensePaymentResponse>> GetPayments(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int? take,
        CancellationToken cancellationToken) =>
        licenseService.GetPaymentsAsync(search, status, take, cancellationToken);

    [HttpPost("plans")]
    public Task<AdminLicensePlanResponse> CreatePlan(
        [FromBody] SaveLicensePlanRequest request,
        CancellationToken cancellationToken) =>
        licenseService.CreatePlanAsync(request, AdminUserId(), cancellationToken);

    [HttpPut("plans/{planId:guid}")]
    public Task<AdminLicensePlanResponse> UpdatePlan(
        Guid planId,
        [FromBody] SaveLicensePlanRequest request,
        CancellationToken cancellationToken) =>
        licenseService.UpdatePlanAsync(planId, request, AdminUserId(), cancellationToken);

    [HttpGet("users")]
    public Task<IReadOnlyList<AdminUserSummaryResponse>> GetUsers(
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        licenseService.GetUsersAsync(search, cancellationToken);

    [HttpGet("users/{userId}")]
    public Task<AdminUserDetailResponse> GetUser(string userId, CancellationToken cancellationToken) =>
        licenseService.GetUserAsync(userId, cancellationToken);

    [HttpPost("users/{userId}/grant")]
    public Task<AdminUserLicenseResponse> GrantLicense(
        string userId,
        [FromBody] GrantUserLicenseRequest request,
        CancellationToken cancellationToken) =>
        licenseService.GrantLicenseAsync(userId, request, AdminUserId(), cancellationToken);

    [HttpPost("user-licenses/{licenseId:guid}/extend")]
    public Task<AdminUserLicenseResponse> ExtendLicense(
        Guid licenseId,
        [FromBody] ExtendUserLicenseRequest request,
        CancellationToken cancellationToken) =>
        licenseService.ExtendLicenseAsync(licenseId, request, AdminUserId(), cancellationToken);

    [HttpPut("user-licenses/{licenseId:guid}/status")]
    public Task<AdminUserLicenseResponse> ChangeLicenseStatus(
        Guid licenseId,
        [FromBody] ChangeUserLicenseStatusRequest request,
        CancellationToken cancellationToken) =>
        licenseService.ChangeLicenseStatusAsync(licenseId, request, AdminUserId(), cancellationToken);

    [HttpDelete("devices/{deviceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeDevice(Guid deviceId, CancellationToken cancellationToken)
    {
        await licenseService.RevokeDeviceAsync(deviceId, AdminUserId(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken cancellationToken)
    {
        await licenseService.RevokeSessionAsync(sessionId, AdminUserId(), cancellationToken);
        return NoContent();
    }

    private string AdminUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Admin user claim is missing.");
}

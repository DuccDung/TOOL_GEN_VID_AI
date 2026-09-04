using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/organization-pools")]
public sealed class AdminOrganizationPoolsController(
    IOrganizationProvisioningAdminService provisioningService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<OrganizationPoolSummaryResponse>> GetPools(CancellationToken cancellationToken) =>
        provisioningService.GetPoolsAsync(cancellationToken);

    [HttpGet("{poolId:guid}")]
    public Task<OrganizationPoolDetailResponse> GetPool(Guid poolId, CancellationToken cancellationToken) =>
        provisioningService.GetPoolAsync(poolId, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<OrganizationPoolSummaryResponse>> CreatePool(
        SaveOrganizationPoolRequest request,
        CancellationToken cancellationToken)
    {
        var result = await provisioningService.CreatePoolAsync(request, AdminUserId(), cancellationToken);
        return Created($"/api/admin/organization-pools/{result.OrganizationPoolId:D}", result);
    }

    [HttpPut("{poolId:guid}")]
    public Task<OrganizationPoolSummaryResponse> UpdatePool(
        Guid poolId,
        SaveOrganizationPoolRequest request,
        CancellationToken cancellationToken) =>
        provisioningService.UpdatePoolAsync(poolId, request, AdminUserId(), cancellationToken);

    [HttpPut("{poolId:guid}/organizations/{organizationId:guid}")]
    public Task<OrganizationPoolOrganizationResponse> UpsertOrganization(
        Guid poolId,
        Guid organizationId,
        SaveOrganizationPoolOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId != organizationId)
        {
            throw new ArgumentException("Organization ID không khớp route.");
        }
        return provisioningService.UpsertOrganizationAsync(poolId, request, AdminUserId(), cancellationToken);
    }

    [HttpDelete("{poolId:guid}/organizations/{organizationId:guid}")]
    public async Task<IActionResult> RemoveOrganization(
        Guid poolId,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        await provisioningService.RemoveOrganizationAsync(poolId, organizationId, AdminUserId(), cancellationToken);
        return NoContent();
    }

    [HttpPut("license-plans/{licensePlanId:guid}")]
    public Task<LicensePlanOrganizationPoolResponse> UpsertLicensePlan(
        Guid licensePlanId,
        SaveLicensePlanOrganizationPoolRequest request,
        CancellationToken cancellationToken) =>
        provisioningService.UpsertLicensePlanAsync(licensePlanId, request, AdminUserId(), cancellationToken);

    [HttpDelete("license-plans/{licensePlanId:guid}")]
    public async Task<IActionResult> RemoveLicensePlan(Guid licensePlanId, CancellationToken cancellationToken)
    {
        await provisioningService.RemoveLicensePlanAsync(licensePlanId, AdminUserId(), cancellationToken);
        return NoContent();
    }

    [HttpGet("assignments")]
    public Task<IReadOnlyList<OrganizationSeatAssignmentResponse>> GetAssignments(
        [FromQuery] string? status,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) =>
        provisioningService.GetAssignmentsAsync(status, take, cancellationToken);

    [HttpPost("assignments/{assignmentId:guid}/retry")]
    public Task<RetryOrganizationSeatAssignmentResponse> RetryAssignment(
        Guid assignmentId,
        CancellationToken cancellationToken) =>
        provisioningService.RetryAssignmentAsync(assignmentId, AdminUserId(), cancellationToken);

    private string AdminUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Admin user claim is missing.");
}

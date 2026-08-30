using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations")]
public sealed class OrganizationsController(IOrganizationService organizationService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<OrganizationSummaryResponse>> GetMine(CancellationToken cancellationToken) =>
        organizationService.GetMineAsync(UserId(), cancellationToken);

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<OrganizationSummaryResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OrganizationSummaryResponse>> Create(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await organizationService.CreateAsync(request, RequestContext(), cancellationToken);
        return Created($"/api/organizations/{result.OrganizationId:D}", result);
    }

    [HttpGet("{organizationId:guid}/members")]
    public Task<IReadOnlyList<OrganizationMemberResponse>> GetMembers(
        Guid organizationId,
        CancellationToken cancellationToken) =>
        organizationService.GetMembersAsync(organizationId, UserId(), cancellationToken);

    [HttpPost("{organizationId:guid}/members")]
    [ProducesResponseType<OrganizationMemberResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OrganizationMemberResponse>> AddMember(
        Guid organizationId,
        AddOrganizationMemberRequest request,
        CancellationToken cancellationToken)
    {
        var result = await organizationService.AddMemberAsync(organizationId, request, RequestContext(), cancellationToken);
        return Created($"/api/organizations/{organizationId:D}/members/{result.UserId}", result);
    }

    [HttpPut("{organizationId:guid}/members/{memberUserId}")]
    public Task<OrganizationMemberResponse> UpdateMember(
        Guid organizationId,
        string memberUserId,
        UpdateOrganizationMemberRequest request,
        CancellationToken cancellationToken) =>
        organizationService.UpdateMemberAsync(organizationId, memberUserId, request, RequestContext(), cancellationToken);

    [HttpPut("{organizationId:guid}/budget")]
    public Task<OrganizationSummaryResponse> UpdateBudget(
        Guid organizationId,
        UpdateOrganizationBudgetRequest request,
        CancellationToken cancellationToken) =>
        organizationService.UpdateBudgetAsync(organizationId, request, RequestContext(), cancellationToken);

    [HttpGet("{organizationId:guid}/providers")]
    public Task<IReadOnlyList<OrganizationProviderResponse>> GetProviders(
        Guid organizationId,
        CancellationToken cancellationToken) =>
        organizationService.GetProvidersAsync(organizationId, UserId(), cancellationToken);

    [HttpPut("{organizationId:guid}/providers/{providerCode}/credential")]
    [ProducesResponseType<OrganizationProviderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizationProviderResponse>> RotateProviderCredential(
        Guid organizationId,
        string providerCode,
        SaveOrganizationProviderCredentialRequest request,
        CancellationToken cancellationToken)
    {
        var result = await organizationService.RotateProviderCredentialAsync(
            organizationId,
            providerCode,
            request,
            RequestContext(),
            cancellationToken);
        if (result.Response is { } response)
        {
            return Ok(response);
        }

        var failure = result.Failure
            ?? throw new InvalidOperationException("Credential rotation result does not contain a response or failure.");
        return StatusCode(
            failure.StatusCode,
            new ApiErrorResponse(
                failure.Code,
                failure.Message,
                null,
                HttpContext.TraceIdentifier));
    }

    [HttpGet("{organizationId:guid}/video-policy")]
    public Task<OrganizationVideoPolicyResponse?> GetVideoPolicy(
        Guid organizationId,
        CancellationToken cancellationToken) =>
        organizationService.GetVideoPolicyAsync(organizationId, UserId(), cancellationToken);

    [HttpPut("{organizationId:guid}/video-policy")]
    public Task<OrganizationVideoPolicyResponse> UpdateVideoPolicy(
        Guid organizationId,
        UpdateOrganizationVideoPolicyRequest request,
        CancellationToken cancellationToken) =>
        organizationService.UpdateVideoPolicyAsync(organizationId, request, RequestContext(), cancellationToken);

    [HttpGet("{organizationId:guid}/usage")]
    public Task<OrganizationUsageResponse> GetUsage(
        Guid organizationId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) =>
        organizationService.GetUsageAsync(organizationId, UserId(), take, cancellationToken);

    [HttpGet("{organizationId:guid}/audit")]
    public Task<IReadOnlyList<OrganizationAuditItemResponse>> GetAudit(
        Guid organizationId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) =>
        organizationService.GetAuditAsync(organizationId, UserId(), take, cancellationToken);

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private OrganizationRequestContext RequestContext() =>
        new(
            UserId(),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            HttpContext.TraceIdentifier);
}

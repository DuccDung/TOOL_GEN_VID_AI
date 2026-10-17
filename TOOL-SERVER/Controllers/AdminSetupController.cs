using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Accounts;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/setup")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminSetupController(AdminSetupService service) : ControllerBase
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    [HttpGet("bootstrap")]
    public Task<AdminSetupBootstrap> Bootstrap(CancellationToken ct) => service.BootstrapAsync(Actor, ct);
    [HttpPost("drafts")]
    public Task<AdminSetupDraft> Create(CreateAdminSetupDraftRequest request, CancellationToken ct) => service.CreateDraftAsync(request, Actor, ct);
    [HttpGet("drafts/{id:guid}")]
    public Task<AdminSetupDraft> Draft(Guid id, CancellationToken ct) => service.GetDraftAsync(id, Actor, ct);
    [HttpPut("drafts/{id:guid}")]
    public Task<AdminSetupDraft> Save(Guid id, SaveAdminSetupDraftRequest request, CancellationToken ct) => service.SaveDraftAsync(id, request, Actor, ct);
    [HttpPost("drafts/{id:guid}/organization")]
    public Task<AdminSetupResult> Organization(Guid id, AdminSetupCommand request, CancellationToken ct) => service.PrepareOrganizationAsync(id, request, Actor, ct);
    [HttpPost("drafts/{id:guid}/connections")]
    [RequestSizeLimit(16384)]
    public Task<AdminSetupResult> Connection(Guid id, AdminSetupConnectionRequest request, CancellationToken ct) => service.ConnectAsync(id, request, Actor, ct);
    [HttpPost("drafts/{id:guid}/pricing")]
    public Task<AdminSetupResult> Pricing(Guid id, AdminSetupPricingRequest request, CancellationToken ct) => service.SavePricingAsync(id, request, Actor, ct);
    [HttpPost("drafts/{id:guid}/configuration")]
    public Task<AdminSetupResult> Configuration(Guid id, AdminSetupCommand request, CancellationToken ct) => service.ConfigureAsync(id, request, Actor, ct);
    [HttpGet("drafts/{id:guid}/readiness")]
    public Task<AdminSetupReadiness> Readiness(Guid id, CancellationToken ct) => service.ReadinessAsync(id, Actor, ct);
    [HttpPost("drafts/{id:guid}/publish")]
    public Task<AdminSetupResult> Publish(Guid id, AdminSetupPublishRequest request, CancellationToken ct) => service.PublishAsync(id, request, Actor, ct);
    [HttpGet("drafts/{id:guid}/operations/{operationId:guid}")]
    public Task<AdminSetupOperation> Operation(Guid id, Guid operationId, CancellationToken ct) => service.OperationAsync(id, operationId, Actor, ct);
}

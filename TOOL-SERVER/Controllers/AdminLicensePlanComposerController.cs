using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Accounts;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/licenses/plan-composer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminLicensePlanComposerController(AdminLicensePlanComposerService service) : ControllerBase
{
    [HttpGet("setup")]
    public Task<AdminLicensePlanSetupResponse> Setup(CancellationToken ct) => service.GetSetupAsync(ct);

    [HttpGet("{planId:guid}")]
    public Task<AdminLicensePlanEditorResponse> Edit(Guid planId, CancellationToken ct) => service.GetEditorAsync(planId, ct);

    [HttpPost]
    public Task<ComposeLicensePlanResponse> Create(ComposeLicensePlanRequest request, CancellationToken ct) =>
        service.CreateAsync(request, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException(), ct);
}

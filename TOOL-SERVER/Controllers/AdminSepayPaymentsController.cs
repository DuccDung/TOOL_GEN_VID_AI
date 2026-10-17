using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Payments;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/payments/sepay")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminSepayPaymentsController(ISepayPaymentAdminService service) : ControllerBase
{
    [HttpGet]
    public Task<AdminSepaySettingsResponse> Get(CancellationToken cancellationToken) => service.GetAsync(cancellationToken);

    [HttpPost("accounts")]
    public Task<AdminSepaySettingsResponse> Create(SaveSepayAccountRequest request, CancellationToken cancellationToken) =>
        service.SaveAccountAsync(null, request, AdminUserId(), cancellationToken);

    [HttpPut("accounts/{accountId:guid}")]
    public Task<AdminSepaySettingsResponse> Update(Guid accountId, SaveSepayAccountRequest request, CancellationToken cancellationToken) =>
        service.SaveAccountAsync(accountId, request, AdminUserId(), cancellationToken);

    [HttpPut("selected-account")]
    public Task<AdminSepaySettingsResponse> Select(SelectSepayAccountRequest request, CancellationToken cancellationToken) =>
        service.SelectAccountAsync(request, AdminUserId(), cancellationToken);

    [HttpPut("enabled")]
    public Task<AdminSepaySettingsResponse> SetEnabled(SetSepayEnabledRequest request, CancellationToken cancellationToken) =>
        service.SetEnabledAsync(request, AdminUserId(), cancellationToken);

    private string AdminUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Admin user claim is missing.");
}

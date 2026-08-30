using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Providers;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/ai-pricing")]
public sealed class AdminAiPricingController(IAiPricingAdminService pricingService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AdminProviderResponse>> GetCatalog(CancellationToken cancellationToken) =>
        pricingService.GetCatalogAsync(cancellationToken);

    [HttpPut("providers/{providerId:guid}")]
    public Task<AdminProviderResponse> UpdateProviderState(
        Guid providerId,
        UpdateAdminProviderStateRequest request,
        CancellationToken cancellationToken) =>
        pricingService.UpdateProviderStateAsync(providerId, request, Context(), cancellationToken);

    [HttpPut("models/{modelId:guid}")]
    public Task<AdminProviderModelResponse> UpdateModelState(
        Guid modelId,
        UpdateAdminProviderModelStateRequest request,
        CancellationToken cancellationToken) =>
        pricingService.UpdateModelStateAsync(modelId, request, Context(), cancellationToken);

    [HttpPost("models/{modelId:guid}/rates")]
    [ProducesResponseType<AdminCostRateResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AdminCostRateResponse>> AddRate(
        Guid modelId,
        CreateAdminCostRateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await pricingService.AddRateAsync(modelId, request, Context(), cancellationToken);
        return Created($"/api/admin/ai-pricing/rates/{result.CostRateId:D}", result);
    }

    [HttpDelete("rates/{rateId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeactivateRate(Guid rateId, CancellationToken cancellationToken)
    {
        await pricingService.DeactivateRateAsync(rateId, Context(), cancellationToken);
        return NoContent();
    }

    private AdminRequestContext Context() =>
        new(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            HttpContext.TraceIdentifier);
}

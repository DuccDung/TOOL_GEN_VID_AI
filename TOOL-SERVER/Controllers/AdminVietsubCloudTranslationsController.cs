using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Vietsub.Translation;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/vietsub/cloud-translations")]
public sealed class AdminVietsubCloudTranslationsController(IServiceProvider services) : ControllerBase
{
    [HttpPost("{jobId:guid}/reconcile")]
    public Task<VietsubCloudReconcileResponse> Reconcile(Guid jobId, VietsubCloudReconcileRequest input, CancellationToken ct) =>
        services.GetRequiredService<VietsubCloudTranslationService>().ReconcileAsync(jobId, input, CloudAccess.From(User), ct);
}

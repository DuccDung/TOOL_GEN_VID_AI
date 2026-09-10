using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Vietsub.Translation;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_SERVER.Controllers;

[ApiController, Authorize]
[Route("api/vietsub/projects/{projectId:guid}/cloud-translation")]
public sealed class VietsubCloudTranslationsController : ControllerBase
{
    private readonly VietsubCloudTranslationService service;
    internal VietsubCloudTranslationsController(VietsubCloudTranslationService service) => this.service = service;
    // Public constructor with service resolution keeps the implementation internal to the server.
    public VietsubCloudTranslationsController(IServiceProvider services) => service = services.GetRequiredService<VietsubCloudTranslationService>();

    [HttpGet("availability")]
    public Task<VietsubCloudAvailability> Availability(Guid projectId, [FromQuery] Guid organizationId, CancellationToken ct) =>
        service.AvailabilityAsync(projectId, organizationId, CloudAccess.From(User), ct);
    [HttpPost("jobs"), RequestSizeLimit(VietsubCloudSnapshot.MaximumBytes)]
    public async Task<ActionResult<VietsubCloudJobResponse>> Start(Guid projectId, VietsubCloudStartRequest body, CancellationToken ct) =>
        Accepted(await service.StartAsync(projectId, body, CloudAccess.From(User), ct));
    [HttpGet("jobs")]
    public Task<VietsubCloudJobResponse?> Find(Guid projectId, [FromQuery] Guid organizationId,
        [FromQuery] Guid clientOperationId, CancellationToken ct) => service.FindAsync(projectId, organizationId, clientOperationId, CloudAccess.From(User), ct);
    [HttpGet("jobs/{jobId:guid}")]
    public Task<VietsubCloudJobResponse> Get(Guid projectId, Guid jobId, [FromQuery] Guid organizationId, CancellationToken ct) =>
        service.GetAsync(projectId, organizationId, jobId, CloudAccess.From(User), ct);
    [HttpGet("jobs/{jobId:guid}/results")]
    public Task<VietsubCloudResultPage> Results(Guid projectId, Guid jobId, [FromQuery] Guid organizationId,
        [FromQuery] int cursor, CancellationToken ct) => service.ResultsAsync(projectId, organizationId, jobId, cursor, CloudAccess.From(User), ct);
    [HttpPost("jobs/{jobId:guid}/{command}")]
    public Task<VietsubCloudJobResponse> Control(Guid projectId, Guid jobId, string command,
        [FromQuery] Guid organizationId, CancellationToken ct) => service.ControlAsync(projectId, organizationId, jobId, command, CloudAccess.From(User), ct);
}

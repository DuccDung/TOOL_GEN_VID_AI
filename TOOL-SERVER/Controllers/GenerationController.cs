using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Generation;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("ai-gateway")]
[Route("api/generation")]
public sealed class GenerationController(
    IGenerationService generationService,
    IKlingOutputProxyService outputProxy,
    IVideoOutputStore videoOutputStore,
    IGeneratedImageContentService generatedImageContentService,
    IGeneratedVoiceContentService generatedVoiceContentService) : ControllerBase
{
    [HttpGet("providers/status")]
    [ProducesResponseType<GenerationProviderStatusResponse>(StatusCodes.Status200OK)]
    public Task<GenerationProviderStatusResponse> GetProviderStatus(
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken) =>
        generationService.GetProviderStatusAsync(organizationId, RequireUserId(), RequireDeviceId(), cancellationToken);

    [HttpPost("content")]
    [ProducesResponseType<GeneratedContentResponse>(StatusCodes.Status200OK)]
    public Task<GeneratedContentResponse> GenerateContent(
        [FromBody] GenerateContentRequest request,
        CancellationToken cancellationToken) =>
        generationService.GenerateContentAsync(request, RequireUserId(), RequireDeviceId(), cancellationToken);

    [HttpPost("characters/{characterId:guid}/reference-images")]
    [ProducesResponseType<GenerateCharacterReferenceImageResponse>(StatusCodes.Status200OK)]
    public Task<GenerateCharacterReferenceImageResponse> GenerateCharacterReferenceImage(
        Guid characterId,
        [FromBody] GenerateCharacterReferenceImageRequest request,
        CancellationToken cancellationToken)
    {
        if (characterId != request.CharacterId)
        {
            throw new ArgumentException("Character ID trên URL không khớp nội dung yêu cầu.");
        }
        return generationService.GenerateCharacterReferenceImageAsync(
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);
    }

    [HttpGet("character-images/{providerRequestId:guid}/content")]
    [Produces("image/png", "image/jpeg")]
    public async Task<IActionResult> DownloadCharacterImage(
        Guid providerRequestId,
        CancellationToken cancellationToken)
    {
        var content = await generatedImageContentService.GetAsync(
            providerRequestId,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.ContentLength = content.SizeBytes;
        Response.Headers.ETag = $"\"{content.Sha256}\"";
        return File(content.Payload, content.MimeType, enableRangeProcessing: false);
    }

    [HttpPost("scenes/{sceneId:guid}/voice")]
    [ProducesResponseType<SceneVoiceGenerationResponse>(StatusCodes.Status200OK)]
    public Task<SceneVoiceGenerationResponse> GenerateSceneVoice(
        Guid sceneId,
        [FromBody] GenerateSceneVoiceRequest request,
        CancellationToken cancellationToken)
    {
        if (sceneId != request.SceneId)
        {
            throw new ArgumentException("Scene ID trên URL không khớp nội dung yêu cầu.");
        }
        return generationService.GenerateSceneVoiceAsync(
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);
    }

    [HttpGet("scene-voices/{providerRequestId:guid}/content")]
    [Produces("audio/wav")]
    public async Task<IActionResult> DownloadSceneVoice(
        Guid providerRequestId,
        CancellationToken cancellationToken)
    {
        var content = await generatedVoiceContentService.GetAsync(
            providerRequestId,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.ContentLength = content.SizeBytes;
        Response.Headers.ETag = $"\"{content.Sha256}\"";
        return File(content.Payload, content.MimeType, enableRangeProcessing: false);
    }

    [HttpPost("kling/videos")]
    [ProducesResponseType<KlingVideoTaskResponse>(StatusCodes.Status200OK)]
    public Task<KlingVideoTaskResponse> SubmitKlingVideo(
        [FromBody] SubmitKlingVideoRequest request,
        CancellationToken cancellationToken) =>
        generationService.SubmitKlingVideoAsync(request, RequireUserId(), RequireDeviceId(), cancellationToken);

    [HttpPost("videos")]
    [ProducesResponseType<VideoTaskResponse>(StatusCodes.Status200OK)]
    public Task<VideoTaskResponse> SubmitVideo(
        [FromBody] SubmitVideoRequest request,
        CancellationToken cancellationToken) =>
        generationService.SubmitVideoAsync(request, RequireUserId(), RequireDeviceId(), cancellationToken);

    [HttpGet("videos/{providerRequestId:guid}")]
    [ProducesResponseType<VideoTaskResponse>(StatusCodes.Status200OK)]
    public Task<VideoTaskResponse> GetVideoStatus(
        Guid providerRequestId,
        CancellationToken cancellationToken) =>
        generationService.GetVideoStatusAsync(providerRequestId, RequireUserId(), RequireDeviceId(), cancellationToken);

    [HttpGet("videos/{providerRequestId:guid}/content")]
    public Task DownloadVideo(
        Guid providerRequestId,
        CancellationToken cancellationToken) =>
        videoOutputStore.CopyToResponseAsync(
            HttpContext,
            providerRequestId,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

    [HttpGet("kling/videos/{providerRequestId:guid}")]
    [ProducesResponseType<KlingVideoTaskResponse>(StatusCodes.Status200OK)]
    public Task<KlingVideoTaskResponse> GetKlingVideoStatus(
        Guid providerRequestId,
        CancellationToken cancellationToken) =>
        generationService.GetKlingVideoStatusAsync(providerRequestId, RequireUserId(), RequireDeviceId(), cancellationToken);

    [HttpGet("kling/videos/{providerRequestId:guid}/content")]
    public Task DownloadKlingVideo(
        Guid providerRequestId,
        CancellationToken cancellationToken) =>
        outputProxy.CopyToResponseAsync(
            HttpContext,
            providerRequestId,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

    private Guid RequireDeviceId() =>
        Guid.TryParse(User.FindFirstValue(AuthClaimTypes.DeviceId), out var deviceId)
            ? deviceId
            : throw new AccountApiException(
                StatusCodes.Status401Unauthorized,
                "missing_device_claim",
                "Phiên đăng nhập không có thông tin thiết bị hợp lệ.");

    private string RequireUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new AccountApiException(
            StatusCodes.Status401Unauthorized,
            "missing_user_claim",
            "Phiên đăng nhập không hợp lệ.");
}

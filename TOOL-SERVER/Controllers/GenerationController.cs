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
    ISceneFirstFrameService sceneFirstFrameService,
    IKlingOutputProxyService outputProxy,
    IVideoOutputStore videoOutputStore,
    IGeneratedImageContentService generatedImageContentService,
    IGeneratedVoiceContentService generatedVoiceContentService) : ControllerBase
{
    [HttpGet("providers/status")]
    [EnableRateLimiting("ai-status")]
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

    [HttpGet("content/failure")]
    [EnableRateLimiting("ai-status")]
    [ProducesResponseType<LatestContentLanguageFailureResponse>(StatusCodes.Status200OK)]
    public async Task<LatestContentLanguageFailureResponse> GetLatestContentLanguageFailure(
        [FromQuery] Guid projectId,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken) =>
        new(await generationService.GetLatestContentLanguageFailureAsync(
                projectId,
                organizationId,
                RequireUserId(),
                RequireDeviceId(),
                cancellationToken));

    [HttpPost("content/repair/quote")]
    [ProducesResponseType<ContentRepairQuoteResponse>(StatusCodes.Status200OK)]
    public Task<ContentRepairQuoteResponse> GetContentRepairQuote(
        [FromBody] ContentRepairQuoteRequest request,
        CancellationToken cancellationToken) =>
        generationService.GetContentRepairQuoteAsync(
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

    [HttpPost("content/repair")]
    [ProducesResponseType<GeneratedContentResponse>(StatusCodes.Status200OK)]
    public Task<GeneratedContentResponse> RepairContent(
        [FromBody] RepairContentRequest request,
        CancellationToken cancellationToken) =>
        generationService.RepairContentAsync(
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

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

    [HttpPost("images/scene-first-frames")]
    [ProducesResponseType<GenerateSceneFirstFrameResponse>(StatusCodes.Status200OK)]
    public Task<GenerateSceneFirstFrameResponse> GenerateSceneFirstFrame(
        [FromBody] GenerateSceneFirstFrameRequest request,
        CancellationToken cancellationToken) =>
        sceneFirstFrameService.GenerateAsync(
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

    [HttpGet("images/scene-first-frames/{providerRequestId:guid}/content")]
    [Produces("image/png", "image/jpeg")]
    public async Task<IActionResult> DownloadSceneFirstFrame(
        Guid providerRequestId,
        CancellationToken cancellationToken)
    {
        var content = await generatedImageContentService.GetAsync(
            providerRequestId,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken,
            GeneratedImageContentKind.SceneFirstFrame);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.ContentLength = content.SizeBytes;
        Response.Headers.ETag = $"\"{content.Sha256}\"";
        return File(content.Payload, content.MimeType, enableRangeProcessing: false);
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
            cancellationToken,
            GeneratedImageContentKind.CharacterReference);
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

    [HttpPost("scenes/{sceneId:guid}/speech-verification/quote")]
    [ProducesResponseType<SceneSpeechVerificationQuoteResponse>(StatusCodes.Status200OK)]
    public Task<SceneSpeechVerificationQuoteResponse> GetSceneSpeechVerificationQuote(
        Guid sceneId,
        [FromBody] SceneSpeechVerificationQuoteRequest request,
        CancellationToken cancellationToken)
    {
        if (sceneId != request.SceneId)
        {
            throw new ArgumentException("Scene ID trên URL không khớp nội dung yêu cầu.");
        }
        return generationService.GetSceneSpeechVerificationQuoteAsync(
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);
    }

    [HttpGet("projects/{projectId:guid}/voice-profiles")]
    [ProducesResponseType<VoiceProfileVersionListResponse>(StatusCodes.Status200OK)]
    public Task<VoiceProfileVersionListResponse> GetVoiceProfileVersions(
        Guid projectId,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken) =>
        generationService.GetVoiceProfileVersionsAsync(
            projectId,
            organizationId,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

    [HttpPost("projects/{projectId:guid}/voice-profiles/drafts")]
    [ProducesResponseType<VoiceProfileVersionSummary>(StatusCodes.Status200OK)]
    public Task<VoiceProfileVersionSummary> CreateVoiceProfileDraft(
        Guid projectId,
        [FromBody] CreateVoiceProfileDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (projectId != request.ProjectId)
        {
            throw new ArgumentException("Project ID trên URL không khớp nội dung yêu cầu.");
        }
        return generationService.CreateVoiceProfileDraftAsync(
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);
    }

    [HttpPost("voice-catalog-preview/quote")]
    [ProducesResponseType<VoiceCatalogPreviewQuoteResponse>(StatusCodes.Status200OK)]
    public Task<VoiceCatalogPreviewQuoteResponse> GetVoiceCatalogPreviewContextQuote(
        [FromBody] VoiceCatalogPreviewContextQuoteRequest request,
        CancellationToken cancellationToken) =>
        generationService.GetVoiceCatalogPreviewContextQuoteAsync(
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);

    [HttpPost("projects/{projectId:guid}/voice-catalog-preview/quote")]
    [ProducesResponseType<VoiceCatalogPreviewQuoteResponse>(StatusCodes.Status200OK)]
    public Task<VoiceCatalogPreviewQuoteResponse> GetVoiceCatalogPreviewQuote(
        Guid projectId,
        [FromBody] VoiceCatalogPreviewQuoteRequest request,
        CancellationToken cancellationToken)
    {
        if (projectId != request.ProjectId)
        {
            throw new ArgumentException("Project ID trên URL không khớp nội dung yêu cầu.");
        }
        return generationService.GetVoiceCatalogPreviewQuoteAsync(
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);
    }

    [HttpPost("projects/{projectId:guid}/voice-catalog-preview")]
    [ProducesResponseType<VoiceCatalogPreviewResponse>(StatusCodes.Status200OK)]
    public Task<VoiceCatalogPreviewResponse> GenerateVoiceCatalogPreview(
        Guid projectId,
        [FromBody] GenerateVoiceCatalogPreviewRequest request,
        CancellationToken cancellationToken)
    {
        if (projectId != request.ProjectId)
        {
            throw new ArgumentException("Project ID trên URL không khớp nội dung yêu cầu.");
        }
        return generationService.GenerateVoiceCatalogPreviewAsync(
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);
    }

    [HttpPost("projects/{projectId:guid}/voice-profiles/{versionId:guid}/preview/quote")]
    [ProducesResponseType<VoiceProfilePreviewQuoteResponse>(StatusCodes.Status200OK)]
    public Task<VoiceProfilePreviewQuoteResponse> GetVoiceProfilePreviewQuote(
        Guid projectId,
        Guid versionId,
        [FromBody] VoiceProfilePreviewQuoteRequest request,
        CancellationToken cancellationToken)
    {
        EnsureVoiceProfileRoute(projectId, versionId, request.ProjectId, request.VoiceProfileVersionId);
        return generationService.GetVoiceProfilePreviewQuoteAsync(
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);
    }

    [HttpPost("projects/{projectId:guid}/voice-profiles/{versionId:guid}/preview")]
    [ProducesResponseType<VoiceProfilePreviewResponse>(StatusCodes.Status200OK)]
    public Task<VoiceProfilePreviewResponse> GenerateVoiceProfilePreview(
        Guid projectId,
        Guid versionId,
        [FromBody] GenerateVoiceProfilePreviewRequest request,
        CancellationToken cancellationToken)
    {
        EnsureVoiceProfileRoute(projectId, versionId, request.ProjectId, request.VoiceProfileVersionId);
        return generationService.GenerateVoiceProfilePreviewAsync(
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);
    }

    [HttpPost("projects/{projectId:guid}/voice-profiles/{versionId:guid}/approve")]
    [ProducesResponseType<VoiceProfileVersionSummary>(StatusCodes.Status200OK)]
    public Task<VoiceProfileVersionSummary> ApproveVoiceProfileVersion(
        Guid projectId,
        Guid versionId,
        [FromBody] ApproveVoiceProfileVersionRequest request,
        CancellationToken cancellationToken)
    {
        EnsureVoiceProfileRoute(projectId, versionId, request.ProjectId, request.VoiceProfileVersionId);
        return generationService.ApproveVoiceProfileVersionAsync(
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);
    }

    [HttpPost("projects/{projectId:guid}/voice-profiles/{versionId:guid}/supersede")]
    [ProducesResponseType<VoiceProfileVersionSummary>(StatusCodes.Status200OK)]
    public Task<VoiceProfileVersionSummary> SupersedeVoiceProfileVersion(
        Guid projectId,
        Guid versionId,
        [FromBody] SupersedeVoiceProfileVersionRequest request,
        CancellationToken cancellationToken)
    {
        EnsureVoiceProfileRoute(projectId, versionId, request.ProjectId, request.VoiceProfileVersionId);
        return generationService.SupersedeVoiceProfileVersionAsync(
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);
    }

    [HttpPost("scenes/{sceneId:guid}/voice/quote")]
    [ProducesResponseType<SceneVoiceQuoteResponse>(StatusCodes.Status200OK)]
    public Task<SceneVoiceQuoteResponse> GetSceneVoiceQuote(
        Guid sceneId,
        [FromBody] SceneVoiceQuoteRequest request,
        CancellationToken cancellationToken)
    {
        if (sceneId != request.SceneId)
        {
            throw new ArgumentException("Scene ID trên URL không khớp nội dung yêu cầu.");
        }
        return generationService.GetSceneVoiceQuoteAsync(
            request,
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);
    }

    [HttpPost("scenes/{sceneId:guid}/speech-verification")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(27 * 1024 * 1024)]
    [ProducesResponseType<SceneSpeechVerificationResponse>(StatusCodes.Status200OK)]
    public async Task<SceneSpeechVerificationResponse> VerifySceneSpeech(
        Guid sceneId,
        [FromForm] Guid projectId,
        [FromForm] int scenePlanVersion,
        [FromForm] string expectedSpeechHash,
        [FromForm] string mediaSha256,
        [FromForm] long durationMs,
        [FromForm] string idempotencyKey,
        [FromForm] Guid? organizationId,
        [FromForm] Guid? sourceMediaAssetId,
        [FromForm] IFormFile audioFile,
        CancellationToken cancellationToken)
    {
        if (audioFile is null ||
            audioFile.Length <= 0 ||
            audioFile.Length > 25L * 1024 * 1024 ||
            audioFile.ContentType is not ("audio/wav" or "audio/x-wav" or "application/octet-stream"))
        {
            throw new ArgumentException("File WAV kiểm tra lời nói không hợp lệ.");
        }
        await using var input = audioFile.OpenReadStream();
        using var buffer = new MemoryStream(checked((int)audioFile.Length));
        await input.CopyToAsync(buffer, cancellationToken);
        return await generationService.VerifySceneSpeechAsync(
            new VerifySceneSpeechRequest(
                projectId,
                sceneId,
                scenePlanVersion,
                expectedSpeechHash,
                mediaSha256,
                durationMs,
                idempotencyKey,
                organizationId,
                sourceMediaAssetId),
            buffer.ToArray(),
            RequireUserId(),
            RequireDeviceId(),
            cancellationToken);
    }

    [HttpPost("scenes/{sceneId:guid}/speech-verification/review/approve")]
    [ProducesResponseType<SceneSpeechVerificationResponse>(StatusCodes.Status200OK)]
    public Task<SceneSpeechVerificationResponse> ApproveSpeechVerificationReview(
        Guid sceneId,
        [FromBody] ApproveSpeechVerificationReviewRequest request,
        CancellationToken cancellationToken)
    {
        if (sceneId != request.SceneId)
        {
            throw new ArgumentException("Scene ID trên URL không khớp nội dung yêu cầu.");
        }
        return generationService.ApproveSpeechVerificationReviewAsync(
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
    [EnableRateLimiting("ai-status")]
    [ProducesResponseType<VideoTaskResponse>(StatusCodes.Status200OK)]
    public Task<VideoTaskResponse> GetVideoStatus(
        Guid providerRequestId,
        CancellationToken cancellationToken) =>
        generationService.GetVideoStatusAsync(providerRequestId, RequireUserId(), RequireDeviceId(), cancellationToken);

    [HttpGet("videos/{providerRequestId:guid}/content")]
    [EnableRateLimiting("ai-status")]
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
    [EnableRateLimiting("ai-status")]
    [ProducesResponseType<KlingVideoTaskResponse>(StatusCodes.Status200OK)]
    public Task<KlingVideoTaskResponse> GetKlingVideoStatus(
        Guid providerRequestId,
        CancellationToken cancellationToken) =>
        generationService.GetKlingVideoStatusAsync(providerRequestId, RequireUserId(), RequireDeviceId(), cancellationToken);

    [HttpGet("kling/videos/{providerRequestId:guid}/content")]
    [EnableRateLimiting("ai-status")]
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

    private static void EnsureVoiceProfileRoute(
        Guid routeProjectId,
        Guid routeVersionId,
        Guid requestProjectId,
        Guid requestVersionId)
    {
        if (routeProjectId != requestProjectId || routeVersionId != requestVersionId)
        {
            throw new ArgumentException("Định danh voice profile trên URL không khớp nội dung yêu cầu.");
        }
    }
}

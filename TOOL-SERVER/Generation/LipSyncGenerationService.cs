using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Models;
using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_SERVER.Generation;

public interface ILipSyncGenerationService
{
    Task<LipSyncInputSessionResponse> CreateInputSessionAsync(CreateLipSyncInputSessionRequest request, string userId, Guid deviceId, CancellationToken cancellationToken);
    Task<LipSyncTaskResponse> SubmitAsync(SubmitLipSyncRequest request, string userId, Guid deviceId, CancellationToken cancellationToken);
    Task<LipSyncTaskResponse> GetStatusAsync(Guid providerRequestId, string userId, Guid deviceId, CancellationToken cancellationToken);
    Task<LipSyncTaskResponse> MaterializeAsync(MaterializeLipSyncOutputRequest request, string userId, Guid deviceId, CancellationToken cancellationToken);
    Task<LipSyncTaskResponse> ApproveAsync(ReviewLipSyncOutputRequest request, string userId, Guid deviceId, CancellationToken cancellationToken);
    Task<LipSyncTaskResponse> RejectAsync(ReviewLipSyncOutputRequest request, string userId, Guid deviceId, CancellationToken cancellationToken);
}

internal sealed class LipSyncGenerationService(
    VideoFactoryDbContext dbContext,
    IGenerationAccessService accessService,
    IProviderRuntimeResolver providerResolver,
    IAiCostEstimator costEstimator,
    IAiBudgetService budgetService,
    ILipSyncInputStore inputStore,
    ILipSyncProviderRouter providerRouter,
    IVideoOutputStore outputStore,
    IOptions<LipSyncOptions> options,
    TimeProvider timeProvider,
    ILogger<LipSyncGenerationService> logger,
    ILipSyncRuntimeSettingsProvider? runtimeSettingsProvider = null) : ILipSyncGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LipSyncOptions _options = options.Value;

    public async Task<LipSyncInputSessionResponse> CreateInputSessionAsync(
        CreateLipSyncInputSessionRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await EnsureEnabledAsync(cancellationToken);
        ValidateSessionRequest(request);
        var access = await accessService.RequireAsync(userId, deviceId, request.OrganizationId, request.ProjectId, cancellationToken);
        var project = access.Project!;
        var inputs = await RequireCurrentInputsAsync(request, project, cancellationToken);
        var provider = await ResolveProviderAsync(project, access.OrganizationId, requireEnabled: true, cancellationToken);
        var quote = await costEstimator.QuoteLipSyncAsync(provider.ProviderModelId, request.DurationMs, cancellationToken);
        RequirePrice(quote);

        var now = UtcNow();
        var existing = await dbContext.LipSyncInputSessions
            .Where(x => x.OrganizationId == access.OrganizationId &&
                        x.RequestedByUserId == userId &&
                        x.ProjectId == request.ProjectId &&
                        x.SceneId == request.SceneId &&
                        x.ScenePlanVersion == request.ScenePlanVersion &&
                        x.VideoGenerationId == request.VideoGenerationId &&
                        x.VoiceGenerationId == request.VoiceGenerationId &&
                        x.VideoSha256 == request.VideoSha256.ToLower() &&
                        x.AudioSha256 == request.AudioSha256.ToLower() &&
                        x.PreparedVideoSha256 == request.PreparedVideoSha256.ToLower() &&
                        x.PreparedAudioSha256 == request.PreparedAudioSha256.ToLower() &&
                        x.DurationMs == request.DurationMs &&
                        x.ExpiresAtUtc > now &&
                        (x.Status == LipSyncStatuses.Uploading || x.Status == LipSyncStatuses.Ready))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return ToSessionResponse(existing, provider, quote);
        }

        if (!HasLipSyncSnapshot(project))
        {
            project.LipSyncProviderCode = provider.ProviderCode;
            project.LipSyncModelCode = provider.ModelCode;
            project.LipSyncPolicyVersion = FalLipSyncPolicy.PolicyVersion;
            project.LipSyncSnapshotAtUtc = now;
        }
        var session = new LipSyncInputSession
        {
            LipSyncInputSessionId = Guid.NewGuid(),
            OrganizationId = access.OrganizationId,
            RequestedByUserId = userId,
            ProjectId = request.ProjectId,
            SceneId = request.SceneId,
            ScenePlanVersion = request.ScenePlanVersion,
            VideoGenerationId = inputs.VideoGeneration.VideoGenerationId,
            VoiceGenerationId = inputs.VoiceGeneration.VoiceGenerationId,
            VideoSha256 = request.VideoSha256.ToLowerInvariant(),
            AudioSha256 = request.AudioSha256.ToLowerInvariant(),
            PreparedVideoSha256 = request.PreparedVideoSha256.ToLowerInvariant(),
            PreparedAudioSha256 = request.PreparedAudioSha256.ToLowerInvariant(),
            DurationMs = request.DurationMs,
            Status = LipSyncStatuses.Uploading,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(Math.Clamp(_options.InputRetentionHours, 1, 72)),
            RowVersion = new byte[8]
        };
        dbContext.LipSyncInputSessions.Add(session);
        project.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSessionResponse(session, provider, quote);
    }

    public async Task<LipSyncTaskResponse> SubmitAsync(
        SubmitLipSyncRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await EnsureEnabledAsync(cancellationToken);
        if (request.ProjectId == Guid.Empty || request.SceneId == Guid.Empty || request.LipSyncInputSessionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 450)
        {
            throw new ArgumentException("Yêu cầu lip-sync không hợp lệ.");
        }
        var access = await accessService.RequireAsync(userId, deviceId, request.OrganizationId, request.ProjectId, cancellationToken);
        var project = access.Project!;
        var session = await dbContext.LipSyncInputSessions.SingleOrDefaultAsync(
            x => x.LipSyncInputSessionId == request.LipSyncInputSessionId &&
                 x.ProjectId == request.ProjectId &&
                 x.SceneId == request.SceneId,
            cancellationToken) ?? throw NotFound();
        if (session.OrganizationId != access.OrganizationId || !string.Equals(session.RequestedByUserId, userId, StringComparison.Ordinal))
        {
            throw NotFound();
        }
        var existingRequest = await dbContext.ProviderRequests.SingleOrDefaultAsync(
            x => x.OrganizationId == access.OrganizationId && x.IdempotencyKey == request.IdempotencyKey,
            cancellationToken);
        if (existingRequest is not null)
        {
            var existingGeneration = await dbContext.LipSyncGenerations.SingleOrDefaultAsync(
                x => x.ProviderRequestId == existingRequest.ProviderRequestId,
                cancellationToken) ?? throw Conflict("idempotency_key_conflict", "Idempotency key đã được dùng cho tác vụ khác.");
            if (existingGeneration.LipSyncInputSessionId != session.LipSyncInputSessionId || existingGeneration.SceneId != request.SceneId)
            {
                throw Conflict("idempotency_key_conflict", "Idempotency key đã được dùng cho input lip-sync khác.");
            }
            return ToResponse(existingGeneration, existingRequest);
        }
        if (session.ExpiresAtUtc <= UtcNow())
        {
            throw new AccountApiException(StatusCodes.Status410Gone, LipSyncErrorCodes.InputExpired, "Input lip-sync đã hết hạn; hãy chuẩn bị lại clip và WAV.");
        }
        if (session.Status != LipSyncStatuses.Ready || session.VideoStorageKey is null || session.AudioStorageKey is null)
        {
            throw new AccountApiException(StatusCodes.Status409Conflict, LipSyncErrorCodes.InputNotReady, "Chưa upload đủ video và WAV cho lip-sync.");
        }
        var currentInputs = await RequireCurrentInputsAsync(
            new CreateLipSyncInputSessionRequest(
                session.ProjectId,
                session.SceneId,
                session.ScenePlanVersion,
                session.VideoGenerationId,
                session.VoiceGenerationId,
                session.VideoSha256,
                session.AudioSha256,
                session.PreparedVideoSha256,
                session.PreparedAudioSha256,
                session.DurationMs,
                session.OrganizationId),
            project,
            cancellationToken);

        var provider = await ResolveProviderAsync(project, access.OrganizationId, requireEnabled: true, cancellationToken);
        var quote = await costEstimator.QuoteLipSyncAsync(provider.ProviderModelId, session.DurationMs, cancellationToken);
        RequirePrice(quote);
        var now = UtcNow();
        var requestJson = JsonSerializer.Serialize(new
        {
            access.OrganizationId,
            request.ProjectId,
            request.SceneId,
            session.LipSyncInputSessionId,
            session.ScenePlanVersion,
            session.VideoGenerationId,
            session.VoiceGenerationId,
            session.VideoSha256,
            session.AudioSha256,
            session.PreparedVideoSha256,
            session.PreparedAudioSha256,
            session.DurationMs,
            ProviderCode = provider.ProviderCode,
            ModelCode = provider.ModelCode,
            PolicyVersion = FalLipSyncPolicy.PolicyVersion,
            InputPolicyVersion = FalLipSyncPolicy.InputPolicyVersion,
            ModelVariant = FalLipSyncPolicy.ModelVariant,
            SyncMode = FalLipSyncPolicy.SyncMode
        }, JsonOptions);
        var requestLog = new ProviderRequest
        {
            ProviderRequestId = Guid.NewGuid(),
            OrganizationId = access.OrganizationId,
            RequestedByUserId = userId,
            OrganizationProviderCredentialId = provider.OrganizationProviderCredentialId,
            ProjectId = request.ProjectId,
            SceneId = request.SceneId,
            ProviderId = provider.ProviderId,
            ProviderModelId = provider.ProviderModelId,
            RequestKind = "LipSync",
            ProviderCode = provider.ProviderCode,
            ModelCode = provider.ModelCode,
            IdempotencyKey = request.IdempotencyKey,
            RequestHash = Sha256Hex(requestJson),
            Status = LipSyncStatuses.Submitting,
            RequestJson = requestJson,
            EstimatedCost = quote.EstimatedCost,
            RateSnapshotJson = quote.RateSnapshotJson,
            CurrencyCode = quote.CurrencyCode,
            UsageJson = JsonSerializer.Serialize(new { durationSeconds = Math.Ceiling(session.DurationMs / 1000m), syncMode = FalLipSyncPolicy.SyncMode }, JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            SubmittedAtUtc = now,
            RowVersion = new byte[8]
        };
        var attempt = await dbContext.LipSyncGenerations.CountAsync(x => x.SceneId == request.SceneId, cancellationToken) + 1;
        var generation = new LipSyncGeneration
        {
            LipSyncGenerationId = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            SceneId = request.SceneId,
            LipSyncInputSessionId = session.LipSyncInputSessionId,
            ProviderRequestId = requestLog.ProviderRequestId,
            VideoGenerationId = session.VideoGenerationId,
            VoiceGenerationId = session.VoiceGenerationId,
            AttemptNumber = attempt,
            Status = LipSyncStatuses.Submitting,
            PolicyVersion = FalLipSyncPolicy.PolicyVersion,
            RequestedDurationMs = session.DurationMs,
            VideoSha256 = session.VideoSha256,
            AudioSha256 = session.AudioSha256,
            PreparedVideoSha256 = session.PreparedVideoSha256,
            PreparedAudioSha256 = session.PreparedAudioSha256,
            CreatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var reservation = await budgetService.ReserveAsync(
            access.OrganizationId,
            userId,
            request.ProjectId,
            requestLog.ProviderRequestId,
            request.IdempotencyKey,
            provider.ProviderCode,
            provider.ModelCode,
            quote.EstimatedCost,
            cancellationToken);
        requestLog.BudgetReservationId = reservation.ReservationId;
        session.Status = LipSyncStatuses.Submitted;
        session.SubmittedAtUtc = now;
        ApplySceneTaskStatus(currentInputs.Scene, LipSyncStatuses.Submitting, null, null, now);
        dbContext.ProviderRequests.Add(requestLog);
        dbContext.LipSyncGenerations.Add(generation);
        project.EstimatedCost += quote.EstimatedCost;
        project.UpdatedAtUtc = now;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await ReleaseAsync(reservation.ReservationId, CancellationToken.None);
            throw;
        }

        var providerAccepted = false;
        var providerCompleted = false;
        var projectActualCostBeforeProvider = project.ActualCost;
        try
        {
            var videoUrl = await inputStore.CreateProviderContentUrlAsync(
                session.LipSyncInputSessionId,
                LipSyncInputKinds.Video,
                session.ExpiresAtUtc,
                cancellationToken);
            var audioUrl = await inputStore.CreateProviderContentUrlAsync(
                session.LipSyncInputSessionId,
                LipSyncInputKinds.Audio,
                session.ExpiresAtUtc,
                cancellationToken);
            var result = await providerRouter.Resolve(provider.ProviderCode).SubmitAsync(provider, videoUrl, audioUrl, cancellationToken);
            providerAccepted = !string.IsNullOrWhiteSpace(result.ExternalRequestId);
            providerCompleted = result.Status == LipSyncStatuses.Completed;
            ApplyResult(requestLog, generation, result);
            ApplySceneTaskStatus(currentInputs.Scene, result.Status, result.ErrorCode, result.ErrorMessage, UtcNow());
            if (providerCompleted)
            {
                await outputStore.CacheAsync(requestLog.ProviderRequestId, result.OutputUrl ?? throw MissingOutput(provider.ProviderCode), cancellationToken);
                CompleteCost(project, requestLog, generation, quote, result);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            if (providerCompleted)
            {
                await SettleAsync(requestLog, quote, CancellationToken.None);
            }
            else if (IsFailure(result.Status))
            {
                await ReleaseAsync(reservation.ReservationId, CancellationToken.None);
            }
            return ToResponse(generation, requestLog);
        }
        catch (Exception exception) when (IsUncertain(exception) || providerAccepted)
        {
            if (providerCompleted)
            {
                project.ActualCost = projectActualCostBeforeProvider;
                requestLog.ActualCost = 0;
            }
            requestLog.Status = LipSyncStatuses.Unknown;
            requestLog.ErrorCode = providerCompleted
                ? "provider_output_download_failed"
                : providerAccepted
                    ? "provider_status_persist_failed"
                    : "provider_submission_unknown";
            requestLog.ErrorMessage = providerCompleted
                ? "Provider đã hoàn tất nhưng server chưa lưu được output; hệ thống sẽ thử lại."
                : providerAccepted
                    ? "Fal đã nhận tác vụ nhưng server chưa lưu được trạng thái mới; worker sẽ tiếp tục polling bằng request_id hiện có."
                    : "Không thể xác nhận Fal đã nhận tác vụ hay chưa. Idempotency key được giữ để tránh gửi trùng.";
            requestLog.NextPollAtUtc = string.IsNullOrWhiteSpace(requestLog.ExternalRequestId) ? null : UtcNow().AddSeconds(15);
            requestLog.CompletedAtUtc = null;
            requestLog.UpdatedAtUtc = UtcNow();
            generation.Status = LipSyncStatuses.Unknown;
            generation.CompletedAtUtc = null;
            ApplySceneTaskStatus(currentInputs.Scene, LipSyncStatuses.Unknown, requestLog.ErrorCode, requestLog.ErrorMessage, UtcNow());
            await dbContext.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning(exception, "Lip-sync submission {ProviderRequestId} entered Unknown state.", requestLog.ProviderRequestId);
            return ToResponse(generation, requestLog);
        }
        catch (Exception exception)
        {
            requestLog.Status = LipSyncStatuses.Failed;
            requestLog.ErrorCode = exception is ProviderHttpException providerException ? providerException.Code : "provider_request_failed";
            requestLog.ErrorMessage = Safe(exception.Message);
            requestLog.CompletedAtUtc = UtcNow();
            requestLog.UpdatedAtUtc = requestLog.CompletedAtUtc.Value;
            generation.Status = LipSyncStatuses.Failed;
            generation.CompletedAtUtc = requestLog.CompletedAtUtc;
            ApplySceneTaskStatus(currentInputs.Scene, LipSyncStatuses.Failed, requestLog.ErrorCode, requestLog.ErrorMessage, UtcNow());
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await ReleaseAsync(reservation.ReservationId, CancellationToken.None);
            throw exception is AccountApiException
                ? exception
                : new AccountApiException(
                    StatusCodes.Status502BadGateway,
                    requestLog.ErrorCode ?? "provider_request_failed",
                    requestLog.ErrorMessage ?? "The lip-sync provider request failed.");
        }
    }

    public async Task<LipSyncTaskResponse> GetStatusAsync(Guid providerRequestId, string userId, Guid deviceId, CancellationToken cancellationToken)
    {
        var pair = await LoadGenerationAsync(providerRequestId, cancellationToken);
        await RequireReadAccessAsync(pair.Request, userId, deviceId, cancellationToken);
        return ToResponse(pair.Generation, pair.Request);
    }

    public async Task<LipSyncTaskResponse> MaterializeAsync(
        MaterializeLipSyncOutputRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        if (request.OutputMediaAssetId == Guid.Empty || !IsSha256(request.OutputSha256) || request.DurationMs <= 0)
        {
            throw new ArgumentException("Metadata output lip-sync không hợp lệ.");
        }
        var access = await accessService.RequireAsync(userId, deviceId, request.OrganizationId, request.ProjectId, cancellationToken);
        var generation = await dbContext.LipSyncGenerations
            .Include(x => x.ProviderRequest)
            .Include(x => x.InputSession)
            .SingleOrDefaultAsync(
                x => x.LipSyncGenerationId == request.LipSyncGenerationId && x.ProjectId == request.ProjectId && x.SceneId == request.SceneId,
                cancellationToken) ?? throw NotFound();
        if (generation.ProviderRequest.OrganizationId != access.OrganizationId || generation.ProviderRequest.RequestedByUserId != userId ||
            generation.Status != LipSyncStatuses.Completed || generation.ProviderRequest.Status != LipSyncStatuses.Completed)
        {
            throw Conflict(LipSyncErrorCodes.ReviewRequired, "Output lip-sync chưa sẵn sàng để ghi nhận.");
        }
        await RequireCurrentInputsAsync(
            new CreateLipSyncInputSessionRequest(
                generation.ProjectId,
                generation.SceneId,
                generation.InputSession.ScenePlanVersion,
                generation.VideoGenerationId,
                generation.VoiceGenerationId,
                generation.VideoSha256,
                generation.AudioSha256,
                generation.PreparedVideoSha256,
                generation.PreparedAudioSha256,
                generation.RequestedDurationMs,
                access.OrganizationId),
            access.Project!,
            cancellationToken);
        var asset = await dbContext.MediaAssets.AsNoTracking().SingleOrDefaultAsync(
            x => x.MediaAssetId == request.OutputMediaAssetId &&
                 x.ProjectId == request.ProjectId &&
                 x.SceneId == request.SceneId &&
                 x.AssetType == "SceneVideoLipSynced" &&
                 x.Status == "Ready" &&
                 x.DeletedAtUtc == null,
            cancellationToken) ?? throw Conflict("lip_sync_output_invalid", "Không tìm thấy MediaAsset lip-sync hợp lệ.");
        if (!string.Equals(asset.Sha256, request.OutputSha256, StringComparison.OrdinalIgnoreCase) ||
            asset.DurationMs != request.DurationMs ||
            asset.SourceProviderRequestId != generation.ProviderRequestId ||
            Math.Abs(request.DurationMs - generation.RequestedDurationMs) > 500)
        {
            throw Conflict(LipSyncErrorCodes.InputChanged, "Output lip-sync không khớp generation hoặc thời lượng đã snapshot.");
        }
        generation.OutputMediaAssetId = asset.MediaAssetId;
        generation.OutputSha256 = asset.Sha256.ToLowerInvariant();
        generation.ActualDurationMs = asset.DurationMs;
        generation.Status = LipSyncStatuses.ReviewRequired;
        generation.CompletedAtUtc ??= UtcNow();
        var scene = await dbContext.Scenes.SingleAsync(x => x.SceneId == request.SceneId, cancellationToken);
        scene.Status = "AudioReviewRequired";
        scene.SpeechStatus = SceneSpeechStatuses.SpeechReadyForLipSync;
        scene.ApprovedRenderMediaAssetId = null;
        scene.LastErrorCode = null;
        scene.LastErrorMessage = null;
        scene.UpdatedAtUtc = UtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(generation, generation.ProviderRequest);
    }

    public Task<LipSyncTaskResponse> ApproveAsync(ReviewLipSyncOutputRequest request, string userId, Guid deviceId, CancellationToken cancellationToken) =>
        ReviewAsync(request, userId, deviceId, approve: true, cancellationToken);

    public Task<LipSyncTaskResponse> RejectAsync(ReviewLipSyncOutputRequest request, string userId, Guid deviceId, CancellationToken cancellationToken) =>
        ReviewAsync(request, userId, deviceId, approve: false, cancellationToken);

    private async Task<LipSyncTaskResponse> ReviewAsync(
        ReviewLipSyncOutputRequest request,
        string userId,
        Guid deviceId,
        bool approve,
        CancellationToken cancellationToken)
    {
        if (approve && !request.PlaybackConfirmed)
        {
            throw new ArgumentException("Hãy phát và kiểm tra video lip-sync trước khi duyệt.");
        }
        if (!approve && string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException("Hãy nhập lý do từ chối output lip-sync.");
        }
        var access = await accessService.RequireAsync(userId, deviceId, request.OrganizationId, request.ProjectId, cancellationToken);
        var generation = await dbContext.LipSyncGenerations
            .Include(x => x.ProviderRequest)
            .Include(x => x.OutputMediaAsset)
            .Include(x => x.InputSession)
            .SingleOrDefaultAsync(
            x => x.LipSyncGenerationId == request.LipSyncGenerationId && x.ProjectId == request.ProjectId && x.SceneId == request.SceneId,
            cancellationToken) ?? throw NotFound();
        if (generation.ProviderRequest.OrganizationId != access.OrganizationId || generation.ProviderRequest.RequestedByUserId != userId ||
            generation.Status != LipSyncStatuses.ReviewRequired || generation.OutputMediaAsset is null)
        {
            throw Conflict(LipSyncErrorCodes.ReviewRequired, "Output lip-sync không ở trạng thái chờ duyệt.");
        }
        var expected = DecodeRowVersion(request.ExpectedRowVersion);
        if (generation.RowVersion.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(generation.RowVersion, expected))
        {
            throw Conflict("lip_sync_generation_changed", "Output lip-sync đã thay đổi; hãy tải lại trước khi duyệt.");
        }
        dbContext.Entry(generation).Property(x => x.RowVersion).OriginalValue = expected;
        var scene = await dbContext.Scenes
            .Include(x => x.ApprovedGeneration)!.ThenInclude(x => x!.OutputMediaAsset)
            .Include(x => x.ApprovedVoiceGeneration)!.ThenInclude(x => x!.OutputMediaAsset)
            .SingleAsync(x => x.SceneId == request.SceneId && x.ProjectId == request.ProjectId, cancellationToken);
        if (scene.ScenePlanVersion != generation.InputSession.ScenePlanVersion)
        {
            throw Conflict(LipSyncErrorCodes.InputChanged, "Kế hoạch cảnh đã thay đổi; không thể duyệt output cũ.");
        }
        if (approve &&
            (scene.ApprovedGenerationId != generation.VideoGenerationId ||
             scene.ApprovedVoiceGenerationId != generation.VoiceGenerationId ||
             scene.ApprovedGeneration?.Status != "Approved" ||
             scene.ApprovedVoiceGeneration?.Status != "Approved" ||
             scene.ApprovedGeneration.OutputMediaAsset is null ||
             scene.ApprovedVoiceGeneration.OutputMediaAsset is null ||
             !string.Equals(scene.ApprovedGeneration.OutputMediaAsset.Sha256, generation.VideoSha256, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(scene.ApprovedVoiceGeneration.OutputMediaAsset.Sha256, generation.AudioSha256, StringComparison.OrdinalIgnoreCase) ||
             generation.OutputMediaAsset.Status != "Ready" ||
             generation.OutputMediaAsset.DeletedAtUtc is not null ||
             generation.OutputMediaAsset.SourceProviderRequestId != generation.ProviderRequestId ||
             generation.OutputMediaAsset.DurationMs is not { } outputDurationMs ||
             Math.Abs(outputDurationMs - generation.RequestedDurationMs) > 500 ||
             !string.Equals(generation.OutputMediaAsset.Sha256, generation.OutputSha256, StringComparison.OrdinalIgnoreCase)))
        {
            throw Conflict(LipSyncErrorCodes.InputChanged, "Lip-sync output is stale or no longer matches the approved source lineage.");
        }
        var now = UtcNow();
        generation.Status = approve ? LipSyncStatuses.Approved : LipSyncStatuses.Rejected;
        generation.ApprovedAtUtc = approve ? now : null;
        generation.ApprovedByUserId = approve ? userId : null;
        generation.ReviewReason = Safe(request.Reason);
        if (approve)
        {
            scene.ApprovedLipSyncGenerationId = generation.LipSyncGenerationId;
            scene.ApprovedRenderMediaAssetId = generation.OutputMediaAssetId;
            scene.SpeechStatus = SceneSpeechStatuses.SpeechApproved;
            scene.Status = "Approved";
            scene.LastErrorCode = null;
            scene.LastErrorMessage = null;
        }
        else
        {
            scene.ApprovedLipSyncGenerationId = null;
            scene.ApprovedRenderMediaAssetId = null;
            scene.SpeechStatus = SceneSpeechStatuses.SpeechReadyForLipSync;
            scene.Status = "PromptReady";
            scene.LastErrorCode = "lip_sync_rejected";
            scene.LastErrorMessage = "Output lip-sync đã bị từ chối; hãy tạo một attempt mới.";
        }
        scene.UpdatedAtUtc = now;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict("lip_sync_generation_changed", "Output lip-sync đã thay đổi; hãy tải lại trước khi duyệt.");
        }
        return ToResponse(generation, generation.ProviderRequest);
    }

    private async Task<LipSyncInputContext> RequireCurrentInputsAsync(
        CreateLipSyncInputSessionRequest request,
        Project project,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(project.SpeechProductionPolicy, SpeechProductionPolicies.CanonicalVoice, StringComparison.Ordinal))
        {
            throw Conflict(LipSyncErrorCodes.NotApplicable, "Project phải dùng Canonical Voice để tạo lip-sync.");
        }
        var scene = await dbContext.Scenes
            .Include(x => x.ApprovedGeneration)!.ThenInclude(x => x!.OutputMediaAsset)
            .Include(x => x.ApprovedVoiceGeneration)!.ThenInclude(x => x!.OutputMediaAsset)
            .SingleOrDefaultAsync(x => x.SceneId == request.SceneId && x.ProjectId == request.ProjectId, cancellationToken)
            ?? throw NotFound();
        var structure = await dbContext.Scripts.AsNoTracking()
            .Where(x => x.ScriptId == scene.ScriptId && x.ProjectId == request.ProjectId)
            .Select(x => x.StructureType)
            .SingleOrDefaultAsync(cancellationToken);
        if (structure != GenerationWorkflowTypes.OpenAiStructuredPlan || string.IsNullOrWhiteSpace(scene.Dialogue))
        {
            throw Conflict(LipSyncErrorCodes.NotApplicable, "MVP lip-sync chỉ áp dụng cho cảnh OnCameraDialogue của video dài.");
        }
        if (project.CurrentScenePlanVersion != request.ScenePlanVersion || scene.ScenePlanVersion != request.ScenePlanVersion)
        {
            throw Conflict(LipSyncErrorCodes.InputChanged, "Kế hoạch cảnh đã thay đổi.");
        }
        var video = scene.ApprovedGeneration;
        var voice = scene.ApprovedVoiceGeneration;
        if (video is null || video.VideoGenerationId != request.VideoGenerationId || video.Status != "Approved" ||
            video.OutputMediaAsset is null || video.OutputMediaAsset.AssetType != "SceneVideo" || video.OutputMediaAsset.Status != "Ready" || video.OutputMediaAsset.DeletedAtUtc is not null ||
            !string.Equals(video.OutputMediaAsset.Sha256, request.VideoSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict(LipSyncErrorCodes.InputChanged, "Clip nền đã duyệt không còn khớp snapshot lip-sync.");
        }
        if (voice is null || voice.VoiceGenerationId != request.VoiceGenerationId || voice.Status != "Approved" ||
            voice.OutputMediaAsset is null || voice.OutputMediaAsset.AssetType != "SceneVoice" || voice.OutputMediaAsset.Status != "Ready" || voice.OutputMediaAsset.DeletedAtUtc is not null ||
            !string.Equals(voice.OutputMediaAsset.Sha256, request.AudioSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict(LipSyncErrorCodes.InputChanged, "Canonical WAV đã duyệt không còn khớp snapshot lip-sync.");
        }
        if (request.DurationMs != scene.ContentDurationMs || request.DurationMs > _options.MaximumDurationSeconds * 1000L)
        {
            throw Conflict(LipSyncErrorCodes.InputChanged, "Thời lượng input lip-sync không khớp cảnh.");
        }
        return new LipSyncInputContext(scene, video, voice);
    }

    private async Task<ProviderRuntimeConfiguration> ResolveProviderAsync(Project project, Guid organizationId, bool requireEnabled, CancellationToken cancellationToken)
    {
        var hasSnapshot = HasLipSyncSnapshot(project);
        var hasPartialSnapshot = !string.IsNullOrWhiteSpace(project.LipSyncProviderCode) ||
                                 !string.IsNullOrWhiteSpace(project.LipSyncModelCode) ||
                                 !string.IsNullOrWhiteSpace(project.LipSyncPolicyVersion) ||
                                 project.LipSyncSnapshotAtUtc is not null;
        if (!hasSnapshot && hasPartialSnapshot)
        {
            throw new AccountApiException(StatusCodes.Status409Conflict, "lip_sync_snapshot_invalid", "Snapshot lip-sync của project không đầy đủ; cần sửa dữ liệu bằng quy trình vận hành có kiểm soát.");
        }
        var providerCode = hasSnapshot ? project.LipSyncProviderCode! : _options.ProviderCode;
        var modelCode = hasSnapshot ? project.LipSyncModelCode! : _options.ModelCode;
        var policyVersion = hasSnapshot ? project.LipSyncPolicyVersion! : FalLipSyncPolicy.PolicyVersion;
        if (providerCode != _options.ProviderCode || modelCode != _options.ModelCode || policyVersion != FalLipSyncPolicy.PolicyVersion)
        {
            throw new AccountApiException(StatusCodes.Status503ServiceUnavailable, "lip_sync_snapshot_unavailable", "Snapshot lip-sync của project không còn được server hỗ trợ; không tự động đổi model.");
        }
        return await providerResolver.ResolveModelAsync(organizationId, providerCode, "LipSync", modelCode, null, requireEnabled, cancellationToken);
    }

    private static bool HasLipSyncSnapshot(Project project) =>
        !string.IsNullOrWhiteSpace(project.LipSyncProviderCode) &&
        !string.IsNullOrWhiteSpace(project.LipSyncModelCode) &&
        !string.IsNullOrWhiteSpace(project.LipSyncPolicyVersion) &&
        project.LipSyncSnapshotAtUtc is not null;

    private LipSyncInputSessionResponse ToSessionResponse(LipSyncInputSession session, ProviderRuntimeConfiguration provider, AiCostQuote quote) =>
        new(
            session.LipSyncInputSessionId,
            session.ProjectId,
            session.SceneId,
            session.VideoGenerationId,
            session.VoiceGenerationId,
            session.Status,
            $"/api/generation/lip-sync/inputs/{session.LipSyncInputSessionId:D}/video",
            $"/api/generation/lip-sync/inputs/{session.LipSyncInputSessionId:D}/audio",
            provider.ProviderCode,
            provider.ModelCode,
            quote.EstimatedCost,
            quote.CurrencyCode,
            session.DurationMs,
            session.ExpiresAtUtc,
            session.VideoStorageKey is not null,
            session.AudioStorageKey is not null);

    private static LipSyncTaskResponse ToResponse(LipSyncGeneration generation, ProviderRequest request) =>
        new(
            generation.LipSyncGenerationId,
            request.ProviderRequestId,
            generation.LipSyncInputSessionId,
            generation.VideoGenerationId,
            generation.VoiceGenerationId,
            request.ProviderCode,
            request.ModelCode,
            generation.Status,
            Progress(generation.Status),
            request.Status == LipSyncStatuses.Completed ? $"/api/generation/lip-sync/{request.ProviderRequestId:D}/content" : null,
            request.ErrorCode,
            request.ErrorMessage,
            request.EstimatedCost,
            request.ActualCost,
            request.CurrencyCode,
            generation.RequestedDurationMs,
            generation.VideoSha256,
            generation.AudioSha256,
            generation.PreparedVideoSha256,
            generation.PreparedAudioSha256,
            Convert.ToBase64String(generation.RowVersion ?? []));

    private async Task<(LipSyncGeneration Generation, ProviderRequest Request)> LoadGenerationAsync(Guid providerRequestId, CancellationToken cancellationToken)
    {
        var generation = await dbContext.LipSyncGenerations.Include(x => x.ProviderRequest).SingleOrDefaultAsync(
            x => x.ProviderRequestId == providerRequestId && x.ProviderRequest.RequestKind == "LipSync",
            cancellationToken) ?? throw NotFound();
        return (generation, generation.ProviderRequest);
    }

    private async Task RequireReadAccessAsync(ProviderRequest request, string userId, Guid deviceId, CancellationToken cancellationToken)
    {
        var access = await accessService.RequireProjectAccessAsync(userId, deviceId, request.OrganizationId, request.ProjectId, cancellationToken);
        if (request.OrganizationId != access.OrganizationId || request.RequestedByUserId != userId)
        {
            throw NotFound();
        }
    }

    private static void ApplyResult(ProviderRequest request, LipSyncGeneration generation, LipSyncProviderTaskResult result)
    {
        request.ExternalRequestId = result.ExternalRequestId;
        request.Status = result.Status;
        request.ResponseJson = result.ResponseJson;
        request.ErrorCode = result.ErrorCode;
        request.ErrorMessage = Safe(result.ErrorMessage);
        request.NextPollAtUtc = IsActive(result.Status) ? DateTime.UtcNow.AddSeconds(10) : null;
        request.CompletedAtUtc = IsTerminal(result.Status) ? DateTime.UtcNow : null;
        request.UpdatedAtUtc = DateTime.UtcNow;
        generation.Status = result.Status;
        generation.ActualDurationMs = result.ActualDurationSeconds is { } seconds ? seconds * 1000L : null;
        generation.CompletedAtUtc = request.CompletedAtUtc;
    }

    private static void CompleteCost(Project project, ProviderRequest request, LipSyncGeneration generation, AiCostQuote quote, LipSyncProviderTaskResult result)
    {
        request.ActualCost = request.EstimatedCost;
        request.UsageJson = JsonSerializer.Serialize(new
        {
            durationSeconds = Math.Ceiling(generation.RequestedDurationMs / 1000m),
            outputDurationSeconds = result.ActualDurationSeconds,
            syncMode = FalLipSyncPolicy.SyncMode,
            usageSource = "rate_snapshot"
        }, JsonOptions);
        request.RateSnapshotJson = quote.RateSnapshotJson;
        project.ActualCost += request.ActualCost;
        project.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static void ApplySceneTaskStatus(
        Scene scene,
        string status,
        string? errorCode,
        string? errorMessage,
        DateTime now)
    {
        var hasActionableError = IsFailure(status) ||
                                 status == LipSyncStatuses.Unknown && !string.IsNullOrWhiteSpace(errorCode);
        scene.Status = status switch
        {
            LipSyncStatuses.Completed => "Generated",
            LipSyncStatuses.Failed or LipSyncStatuses.Cancelled or LipSyncStatuses.Expired => "PromptReady",
            _ => "WaitingProvider"
        };
        scene.ApprovedRenderMediaAssetId = null;
        scene.LastErrorCode = hasActionableError ? Safe(errorCode) : null;
        scene.LastErrorMessage = hasActionableError ? Safe(errorMessage) : null;
        scene.UpdatedAtUtc = now;
    }

    private async Task SettleAsync(ProviderRequest request, AiCostQuote quote, CancellationToken cancellationToken)
    {
        if (request.BudgetReservationId is not { } reservationId)
        {
            return;
        }
        try
        {
            await budgetService.SettleAsync(
                reservationId,
                request.ActualCost,
                request.OrganizationProviderCredentialId,
                JsonSerializer.Deserialize<JsonElement>(request.UsageJson ?? "{}"),
                JsonSerializer.Deserialize<JsonElement>(quote.RateSnapshotJson),
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not settle lip-sync reservation {ReservationId}; reconciliation is required.", reservationId);
        }
    }

    private async Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        try
        {
            await budgetService.ReleaseAsync(reservationId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not release lip-sync reservation {ReservationId}; reconciliation is required.", reservationId);
        }
    }

    private async Task EnsureEnabledAsync(CancellationToken cancellationToken)
    {
        var effectiveOptions = runtimeSettingsProvider is null
            ? _options
            : (await runtimeSettingsProvider.GetAsync(cancellationToken)).Options;
        if (!effectiveOptions.Enabled)
        {
            throw Conflict(LipSyncErrorCodes.Disabled, "Lip-sync đang tắt theo feature flag của server.");
        }
        if (!LipSyncOptions.IsValid(effectiveOptions))
        {
            throw new AccountApiException(
                StatusCodes.Status503ServiceUnavailable,
                LipSyncErrorCodes.NotConfigured,
                "Public HTTPS input URL cho lip-sync chưa được cấu hình an toàn.");
        }
    }

    private static void RequirePrice(AiCostQuote quote)
    {
        if (quote.EstimatedCost <= 0)
        {
            throw new AccountApiException(StatusCodes.Status503ServiceUnavailable, LipSyncErrorCodes.PricingNotConfigured, "Chưa cấu hình rate VideoSecond/Second cho Fal lipsync-2.");
        }
    }

    private static void ValidateSessionRequest(CreateLipSyncInputSessionRequest request)
    {
        if (request.ProjectId == Guid.Empty || request.SceneId == Guid.Empty || request.VideoGenerationId == Guid.Empty || request.VoiceGenerationId == Guid.Empty ||
            request.ScenePlanVersion <= 0 || request.DurationMs is < 1_000 or > 120_000 || !IsSha256(request.VideoSha256) || !IsSha256(request.AudioSha256) ||
            !IsSha256(request.PreparedVideoSha256) || !IsSha256(request.PreparedAudioSha256))
        {
            throw new ArgumentException("Snapshot input lip-sync không hợp lệ.");
        }
    }

    private static byte[] DecodeRowVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("RowVersion lip-sync không hợp lệ.");
        }
        try
        {
            var bytes = Convert.FromBase64String(value);
            return bytes.Length == 8 ? bytes : throw new FormatException();
        }
        catch (FormatException)
        {
            throw new ArgumentException("RowVersion lip-sync không hợp lệ.");
        }
    }

    private static bool IsSha256(string? value) =>
        value?.Length == 64 && value.All(Uri.IsHexDigit);

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static decimal Progress(string status) => status switch
    {
        LipSyncStatuses.Submitting => 2m,
        LipSyncStatuses.Submitted => 5m,
        LipSyncStatuses.Queued => 10m,
        LipSyncStatuses.Processing => 50m,
        LipSyncStatuses.Completed or LipSyncStatuses.ReviewRequired or LipSyncStatuses.Approved => 100m,
        _ => 0m
    };

    private static bool IsActive(string status) => status is LipSyncStatuses.Submitted or LipSyncStatuses.Queued or LipSyncStatuses.Processing or LipSyncStatuses.Unknown;
    private static bool IsTerminal(string status) => status is LipSyncStatuses.Completed or LipSyncStatuses.Failed or LipSyncStatuses.Cancelled or LipSyncStatuses.Expired;
    private static bool IsFailure(string status) => status is LipSyncStatuses.Failed or LipSyncStatuses.Cancelled or LipSyncStatuses.Expired;
    private static bool IsUncertain(Exception exception) => exception is HttpRequestException or TaskCanceledException || exception is ProviderHttpException { StatusCode: >= HttpStatusCode.InternalServerError };
    private static string? Safe(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= 4000 ? value : value[..4000];
    private static ProviderHttpException MissingOutput(string providerCode) => new(providerCode, "provider_output_missing", "Provider hoàn tất nhưng không trả output lip-sync.");
    private static AccountApiException NotFound() => new(StatusCodes.Status404NotFound, "lip_sync_not_found", "Không tìm thấy tác vụ lip-sync.");
    private static AccountApiException Conflict(string code, string message) => new(StatusCodes.Status409Conflict, code, message);
    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record LipSyncInputContext(Scene Scene, VideoGeneration VideoGeneration, VoiceGeneration VoiceGeneration);
}

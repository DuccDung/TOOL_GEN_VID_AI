using System.Data;
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
using TOOL_SHARED.Contracts.Organizations;
using TOOL_SHARED.Contracts.Projects;

namespace TOOL_SERVER.Generation;

public interface ISceneFirstFrameService
{
    Task<SceneFirstFrameQuoteResponse> GetQuoteAsync(Guid projectId, Guid sceneId, Guid? organizationId, string userId, Guid deviceId, CancellationToken cancellationToken);
    Task<GenerateSceneFirstFrameResponse> GenerateAsync(GenerateSceneFirstFrameRequest request, string userId, Guid deviceId, CancellationToken cancellationToken);
    Task<SceneFirstFrameListResponse> ListAsync(Guid projectId, Guid sceneId, Guid? organizationId, string userId, Guid deviceId, CancellationToken cancellationToken);
    Task<ProjectSceneFirstFrameListResponse> ListProjectAsync(Guid projectId, Guid? organizationId, string userId, Guid deviceId, CancellationToken cancellationToken);
    Task<SceneFirstFrameSummary> MaterializeAsync(Guid projectId, Guid sceneId, MaterializeSceneFirstFrameRequest request, string userId, Guid deviceId, CancellationToken cancellationToken);
    Task<SceneFirstFrameSummary> ApproveAsync(Guid projectId, Guid sceneId, Guid frameId, ChangeSceneFirstFrameStatusRequest request, string userId, Guid deviceId, CancellationToken cancellationToken);
    Task<SceneFirstFrameSummary> RejectAsync(Guid projectId, Guid sceneId, Guid frameId, ChangeSceneFirstFrameStatusRequest request, string userId, Guid deviceId, CancellationToken cancellationToken);
    Task<SceneFirstFrameVideoInputValidation> ValidateForVideoAsync(Guid projectId, Guid sceneId, string aspectRatio, SceneFirstFrameInput? input, CancellationToken cancellationToken);
}

public sealed record SceneFirstFrameVideoInputValidation(
    Guid SceneFirstFrameId,
    string MimeType,
    string Base64Data,
    string Sha256);

internal sealed class SceneFirstFrameService(
    VideoFactoryDbContext dbContext,
    IGenerationAccessService accessService,
    IProjectVideoPolicyResolver videoPolicyResolver,
    IProviderRuntimeResolver providerResolver,
    IOpenAiImageClient imageClient,
    IAiCostEstimator costEstimator,
    IAiBudgetService budgetService,
    IOptions<OpenAiImageOptions> imageOptions,
    ILogger<SceneFirstFrameService> logger,
    TimeProvider timeProvider) : ISceneFirstFrameService
{
    private const long MaximumFirstFrameBytes = 8L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenAiImageOptions _imageOptions = ValidateOptions(imageOptions.Value);

    public async Task<SceneFirstFrameQuoteResponse> GetQuoteAsync(
        Guid projectId,
        Guid sceneId,
        Guid? organizationId,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var preflight = await RequirePreflightAsync(
            projectId,
            sceneId,
            organizationId,
            userId,
            deviceId,
            null,
            null,
            cancellationToken);
        var provider = await ResolveImageProviderAsync(preflight.Access.OrganizationId, cancellationToken);
        var prompt = SceneFirstFramePromptComposer.Compose(preflight.PromptInput);
        var quote = await QuoteAsync(provider, prompt, cancellationToken);
        return new SceneFirstFrameQuoteResponse(
            provider.ProviderCode,
            provider.ModelCode,
            preflight.Project.AspectRatio,
            preflight.Width,
            preflight.Height,
            quote.EstimatedCost,
            quote.CurrencyCode,
            preflight.Character?.Reference!.CharacterReferenceId,
            preflight.Character?.Name,
            preflight.Scene.ScenePlanVersion,
            preflight.Prompt.ScenePromptId,
            preflight.Prompt.Version);
    }

    public async Task<GenerateSceneFirstFrameResponse> GenerateAsync(
        GenerateSceneFirstFrameRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        ValidateGenerateRequest(request);
        var preflight = await RequirePreflightAsync(
            request.ProjectId,
            request.SceneId,
            request.OrganizationId,
            userId,
            deviceId,
            request.ScenePlanVersion,
            request.ScenePromptVersion,
            cancellationToken);
        var sourceImage = ValidateSourceImage(request.CharacterReference, preflight.Character);
        var prompt = SceneFirstFramePromptComposer.Compose(preflight.PromptInput);
        var promptHash = Sha256Hex(prompt);
        var sceneFingerprint = SceneFingerprint(preflight.Scene);
        var assetVersionHash = AssetVersionHash(preflight.Assets);
        var safeRequest = new
        {
            request.ProjectId,
            request.SceneId,
            request.ScenePlanVersion,
            ScenePromptId = preflight.Prompt.ScenePromptId,
            request.ScenePromptVersion,
            ScenePromptHash = preflight.Prompt.PromptHash,
            request.Attempt,
            SourceCharacterReferenceId = preflight.Character?.Reference!.CharacterReferenceId,
            SourceCharacterReferenceSha256 = preflight.Character?.Reference!.Sha256,
            AssetVersions = preflight.Assets.Select(x => new { x.ProjectAssetId, x.ProjectAssetVersionId, x.Version }).ToArray(),
            AssetVersionHash = assetVersionHash,
            SceneFingerprint = sceneFingerprint,
            preflight.Project.AspectRatio,
            OutputSize = $"{preflight.Width}x{preflight.Height}",
            PromptTemplateVersion = SceneFirstFramePromptComposer.TemplateVersion,
            PromptHash = promptHash,
            _imageOptions.Quality,
            OutputFormat = "png"
        };
        var requestJson = JsonSerializer.Serialize(safeRequest, JsonOptions);
        var requestHash = Sha256Hex(requestJson);
        var existing = await dbContext.ProviderRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganizationId == preflight.Access.OrganizationId &&
                     x.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);
        if (existing is not null)
        {
            EnsureIdempotentReplay(existing, request, requestHash);
            if (existing.Status == "Completed" && !string.IsNullOrWhiteSpace(existing.ResponseJson))
            {
                var retained = await dbContext.GeneratedImageOutputs.AsNoTracking().AnyAsync(
                    x => x.ProviderRequestId == existing.ProviderRequestId && x.ExpiresAtUtc > UtcNow(),
                    cancellationToken);
                if (!retained)
                {
                    throw Gone("scene_first_frame_output_expired", "Output first-frame trên server đã hết hạn. Hãy sinh lại first-frame.");
                }
                return JsonSerializer.Deserialize<GenerateSceneFirstFrameResponse>(existing.ResponseJson, JsonOptions)
                    ?? throw Conflict("generation_result_invalid", "Metadata first-frame đã lưu không hợp lệ.");
            }
            throw ExistingRequestError(existing);
        }

        var provider = await ResolveImageProviderAsync(preflight.Access.OrganizationId, cancellationToken);
        var quote = await QuoteAsync(provider, prompt, cancellationToken);
        var now = UtcNow();
        var requestLog = new ProviderRequest
        {
            ProviderRequestId = Guid.NewGuid(),
            OrganizationId = preflight.Access.OrganizationId,
            RequestedByUserId = userId,
            OrganizationProviderCredentialId = provider.OrganizationProviderCredentialId,
            ProjectId = request.ProjectId,
            SceneId = request.SceneId,
            CharacterId = preflight.Character?.CharacterId,
            ProviderId = provider.ProviderId,
            ProviderModelId = provider.ProviderModelId,
            RequestKind = "Image",
            ProviderCode = provider.ProviderCode,
            ModelCode = provider.ModelCode,
            IdempotencyKey = request.IdempotencyKey,
            RequestHash = requestHash,
            Status = "Created",
            RequestJson = requestJson,
            EstimatedCost = quote.EstimatedCost,
            CurrencyCode = quote.CurrencyCode,
            RateSnapshotJson = quote.RateSnapshotJson,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var reservation = await budgetService.ReserveAsync(
            preflight.Access.OrganizationId,
            userId,
            request.ProjectId,
            requestLog.ProviderRequestId,
            request.IdempotencyKey,
            provider.ProviderCode,
            provider.ModelCode,
            quote.EstimatedCost,
            cancellationToken);
        requestLog.BudgetReservationId = reservation.ReservationId;
        dbContext.ProviderRequests.Add(requestLog);
        dbContext.ProviderRequestAssetVersions.AddRange(preflight.Assets.Select((asset, index) =>
            new ProviderRequestAssetVersion
            {
                ProviderRequestId = requestLog.ProviderRequestId,
                ProjectAssetVersionId = asset.ProjectAssetVersionId,
                AppliedOrder = checked((short)index)
            }));
        preflight.Project.EstimatedCost += quote.EstimatedCost;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await budgetService.ReleaseAsync(reservation.ReservationId, CancellationToken.None);
            throw;
        }

        OpenAiImageResult? providerResult = null;
        try
        {
            requestLog.Status = "Submitting";
            requestLog.SubmittedAtUtc = UtcNow();
            requestLog.UpdatedAtUtc = requestLog.SubmittedAtUtc.Value;
            await dbContext.SaveChangesAsync(cancellationToken);

            providerResult = await imageClient.GenerateSceneFirstFrameAsync(
                provider,
                prompt,
                preflight.Project.AspectRatio,
                sourceImage,
                cancellationToken);
            var actualCost = providerResult.InputTokens > 0 || providerResult.OutputTokens > 0
                ? await costEstimator.CalculateOpenAiActualAsync(
                    quote.RateSnapshotJson,
                    providerResult.InputTokens,
                    providerResult.OutputTokens,
                    cancellationToken)
                : quote.EstimatedCost;
            var completedAt = UtcNow();
            var expiresAt = completedAt.AddHours(_imageOptions.RetentionHours);
            var response = new GenerateSceneFirstFrameResponse(
                requestLog.ProviderRequestId,
                provider.ProviderCode,
                provider.ModelCode,
                $"/api/generation/images/scene-first-frames/{requestLog.ProviderRequestId:D}/content",
                providerResult.Image.MimeType,
                providerResult.Image.Sha256,
                providerResult.Image.Width,
                providerResult.Image.Height,
                providerResult.Image.Bytes.LongLength,
                providerResult.InputTokens,
                providerResult.OutputTokens,
                actualCost,
                quote.CurrencyCode,
                expiresAt);

            dbContext.GeneratedImageOutputs.Add(new GeneratedImageOutput
            {
                ProviderRequestId = requestLog.ProviderRequestId,
                Payload = providerResult.Image.Bytes,
                MimeType = providerResult.Image.MimeType,
                Sha256 = providerResult.Image.Sha256,
                SizeBytes = providerResult.Image.Bytes.LongLength,
                Width = providerResult.Image.Width,
                Height = providerResult.Image.Height,
                CreatedAtUtc = completedAt,
                ExpiresAtUtc = expiresAt
            });
            requestLog.ExternalRequestId = string.IsNullOrWhiteSpace(providerResult.ProviderRequestId)
                ? null
                : providerResult.ProviderRequestId;
            requestLog.Status = "Completed";
            requestLog.ResponseJson = JsonSerializer.Serialize(response, JsonOptions);
            requestLog.InputTokens = providerResult.InputTokens;
            requestLog.OutputTokens = providerResult.OutputTokens;
            requestLog.UsageJson = JsonSerializer.Serialize(new
            {
                providerResult.InputTokens,
                providerResult.OutputTokens,
                size = $"{preflight.Width}x{preflight.Height}",
                mode = sourceImage is null ? "generate" : "edit",
                _imageOptions.Quality,
                outputFormat = "png"
            }, JsonOptions);
            requestLog.ActualCost = actualCost;
            requestLog.CompletedAtUtc = completedAt;
            requestLog.UpdatedAtUtc = completedAt;
            preflight.Project.ActualCost += actualCost;
            preflight.Project.UpdatedAtUtc = completedAt;
            await dbContext.SaveChangesAsync(cancellationToken);
            await TrySettleAsync(
                reservation.ReservationId,
                actualCost,
                provider.OrganizationProviderCredentialId,
                new { providerResult.InputTokens, providerResult.OutputTokens },
                quote.RateSnapshotJson,
                cancellationToken);
            return response;
        }
        catch (Exception exception)
        {
            requestLog.Status = "Failed";
            requestLog.ErrorCode = SafeCode(exception is ProviderHttpException providerException
                ? providerException.Code
                : "scene_first_frame_generation_failed");
            requestLog.ErrorMessage = SafeMessage(exception.Message);
            requestLog.CompletedAtUtc = UtcNow();
            requestLog.UpdatedAtUtc = requestLog.CompletedAtUtc.Value;
            try
            {
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception saveException)
            {
                logger.LogError(saveException, "Could not persist failed scene first-frame request {ProviderRequestId}.", requestLog.ProviderRequestId);
            }
            if (providerResult is null)
            {
                await budgetService.ReleaseAsync(reservation.ReservationId, CancellationToken.None);
            }
            else
            {
                var actualCost = providerResult.InputTokens > 0 || providerResult.OutputTokens > 0
                    ? await costEstimator.CalculateOpenAiActualAsync(
                        quote.RateSnapshotJson,
                        providerResult.InputTokens,
                        providerResult.OutputTokens,
                        CancellationToken.None)
                    : quote.EstimatedCost;
                await TrySettleAsync(
                    reservation.ReservationId,
                    actualCost,
                    provider.OrganizationProviderCredentialId,
                    new { providerResult.InputTokens, providerResult.OutputTokens, resultAccepted = false },
                    quote.RateSnapshotJson,
                    CancellationToken.None);
            }
            throw GenerationService.ToApiException(exception);
        }
    }

    public async Task<SceneFirstFrameListResponse> ListAsync(
        Guid projectId,
        Guid sceneId,
        Guid? organizationId,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await accessService.RequireProjectAccessAsync(userId, deviceId, organizationId, projectId, cancellationToken);
        await RequireSceneAsync(projectId, sceneId, cancellationToken);
        var frames = await LoadFramesAsync(
            dbContext.SceneFirstFrames.Where(x => x.SceneId == sceneId),
            cancellationToken);
        return new SceneFirstFrameListResponse(projectId, sceneId, frames);
    }

    public async Task<ProjectSceneFirstFrameListResponse> ListProjectAsync(
        Guid projectId,
        Guid? organizationId,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await accessService.RequireProjectAccessAsync(userId, deviceId, organizationId, projectId, cancellationToken);
        var frames = await LoadFramesAsync(
            dbContext.SceneFirstFrames.Where(x => x.Scene.ProjectId == projectId),
            cancellationToken);
        return new ProjectSceneFirstFrameListResponse(projectId, frames);
    }

    private async Task<IReadOnlyList<SceneFirstFrameSummary>> LoadFramesAsync(
        IQueryable<SceneFirstFrame> query,
        CancellationToken cancellationToken)
    {
        var frames = await query
            .Include(x => x.MediaAsset)
            .OrderBy(x => x.SceneId)
            .ThenByDescending(x => x.Version)
            .ToListAsync(cancellationToken);
        var changed = false;
        var evaluated = new List<(SceneFirstFrame Frame, Freshness Freshness)>(frames.Count);
        foreach (var frame in frames)
        {
            var freshness = await EvaluateFreshnessAsync(frame, cancellationToken);
            if (!freshness.IsCurrent && frame.Status is SceneFirstFrameStatuses.PendingReview or SceneFirstFrameStatuses.Approved)
            {
                frame.Status = SceneFirstFrameStatuses.Invalidated;
                frame.InvalidatedAtUtc ??= UtcNow();
                changed = true;
            }
            evaluated.Add((frame, freshness));
        }
        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return evaluated.Select(x => ToSummary(x.Frame, x.Freshness)).ToArray();
    }

    public async Task<SceneFirstFrameSummary> MaterializeAsync(
        Guid projectId,
        Guid sceneId,
        MaterializeSceneFirstFrameRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        if (request.ProviderRequestId == Guid.Empty || request.SizeBytes <= 0 || request.SizeBytes > MaximumFirstFrameBytes)
        {
            throw new ArgumentException("Metadata materialize first-frame không hợp lệ.");
        }
        var access = await accessService.RequireAsync(userId, deviceId, request.OrganizationId, projectId, cancellationToken);
        var scene = await RequireSceneAsync(projectId, sceneId, cancellationToken);
        var relativePath = NormalizeRelativePath(request.RelativePath, projectId, sceneId);
        var providerRequest = await dbContext.ProviderRequests
            .Include(x => x.GeneratedImageOutput)
            .SingleOrDefaultAsync(
                x => x.ProviderRequestId == request.ProviderRequestId &&
                     x.ProjectId == projectId && x.SceneId == sceneId &&
                     x.OrganizationId == access.OrganizationId &&
                     x.RequestedByUserId == userId &&
                     x.RequestKind == "Image" && x.Status == "Completed",
                cancellationToken)
            ?? throw NotFound("scene_first_frame_request_not_found", "Không tìm thấy output first-frame đã tạo.");
        var output = providerRequest.GeneratedImageOutput
            ?? throw Gone("scene_first_frame_output_expired", "Output first-frame trên server đã hết hạn.");
        if (output.ExpiresAtUtc <= UtcNow())
        {
            throw Gone("scene_first_frame_output_expired", "Output first-frame trên server đã hết hạn.");
        }
        if (request.MimeType != output.MimeType ||
            !string.Equals(request.Sha256, output.Sha256, StringComparison.OrdinalIgnoreCase) ||
            request.SizeBytes != output.SizeBytes || request.Width != output.Width || request.Height != output.Height)
        {
            throw Conflict("scene_first_frame_materialize_mismatch", "File đã tải không khớp output first-frame trên server.");
        }
        GeneratedImageValidator.ValidateSceneFirstFrame(output.Payload, checked((int)MaximumFirstFrameBytes), access.Project!.AspectRatio);

        var existing = await dbContext.SceneFirstFrames
            .Include(x => x.MediaAsset)
            .SingleOrDefaultAsync(x => x.GeneratedByProviderRequestId == providerRequest.ProviderRequestId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.MediaAsset.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(existing.MediaAsset.Sha256, output.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw Conflict("scene_first_frame_already_materialized", "Output này đã được materialize vào một file khác.");
            }
            return ToSummary(existing, await EvaluateFreshnessAsync(existing, cancellationToken));
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var version = (await dbContext.SceneFirstFrames
            .Where(x => x.SceneId == sceneId)
            .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var metadata = ReadGenerationMetadata(providerRequest.RequestJson);
        var now = UtcNow();
        var mediaAsset = new MediaAsset
        {
            MediaAssetId = Guid.NewGuid(),
            ProjectId = projectId,
            SceneId = sceneId,
            AssetType = "SceneFirstFrame",
            DisplayName = $"First-frame cảnh {scene.SequenceNumber}, bản {version}",
            RelativePath = relativePath,
            MimeType = output.MimeType,
            SizeBytes = output.SizeBytes,
            Sha256 = output.Sha256,
            Width = output.Width,
            Height = output.Height,
            Status = "Ready",
            SourceType = "Generated",
            SourceProviderCode = providerRequest.ProviderCode,
            SourceExternalRequestId = providerRequest.ExternalRequestId,
            SourceProviderRequestId = providerRequest.ProviderRequestId,
            MetadataJson = JsonSerializer.Serialize(new
            {
                sceneFirstFrameVersion = version,
                metadata.PromptTemplateVersion,
                metadata.PromptHash,
                metadata.AssetVersionHash
            }, JsonOptions),
            CreatedAtUtc = now,
            VerifiedAtUtc = now,
            RowVersion = new byte[8]
        };
        var frame = new SceneFirstFrame
        {
            SceneFirstFrameId = Guid.NewGuid(),
            SceneId = sceneId,
            MediaAssetId = mediaAsset.MediaAssetId,
            MediaAsset = mediaAsset,
            Version = version,
            Status = SceneFirstFrameStatuses.PendingReview,
            SourceCharacterReferenceId = metadata.SourceCharacterReferenceId,
            GeneratedByProviderRequestId = providerRequest.ProviderRequestId,
            ScenePlanVersion = metadata.ScenePlanVersion,
            ScenePromptId = metadata.ScenePromptId,
            ScenePromptVersion = metadata.ScenePromptVersion,
            AspectRatio = metadata.AspectRatio,
            PromptTemplateVersion = metadata.PromptTemplateVersion,
            CreatedByUserId = userId,
            CreatedAtUtc = now,
            RowVersion = new byte[8]
        };
        dbContext.MediaAssets.Add(mediaAsset);
        dbContext.SceneFirstFrames.Add(frame);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return ToSummary(frame, await EvaluateFreshnessAsync(frame, cancellationToken));
    }

    public Task<SceneFirstFrameSummary> ApproveAsync(
        Guid projectId,
        Guid sceneId,
        Guid frameId,
        ChangeSceneFirstFrameStatusRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(projectId, sceneId, frameId, request, userId, deviceId, approve: true, cancellationToken);

    public Task<SceneFirstFrameSummary> RejectAsync(
        Guid projectId,
        Guid sceneId,
        Guid frameId,
        ChangeSceneFirstFrameStatusRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(projectId, sceneId, frameId, request, userId, deviceId, approve: false, cancellationToken);

    public async Task<SceneFirstFrameVideoInputValidation> ValidateForVideoAsync(
        Guid projectId,
        Guid sceneId,
        string aspectRatio,
        SceneFirstFrameInput? input,
        CancellationToken cancellationToken)
    {
        if (input is null)
        {
            throw new AccountApiException(
                StatusCodes.Status422UnprocessableEntity,
                "fal_first_frame_required",
                "Cảnh này chưa có first-frame đúng tỷ lệ đã duyệt. Hãy tạo và duyệt first-frame trước khi gửi sang Veo.");
        }
        if (input.SceneFirstFrameId == Guid.Empty || input.MimeType is not ("image/png" or "image/jpeg") ||
            string.IsNullOrWhiteSpace(input.Base64Data) || input.Base64Data.Length > 12_000_000 || input.Sha256.Length != 64)
        {
            throw new ArgumentException("First-frame gửi sang Veo không hợp lệ.");
        }
        var frame = await dbContext.SceneFirstFrames
            .Include(x => x.MediaAsset)
            .SingleOrDefaultAsync(
                x => x.SceneFirstFrameId == input.SceneFirstFrameId && x.SceneId == sceneId &&
                     x.Scene.ProjectId == projectId,
                cancellationToken)
            ?? throw NotFound("scene_first_frame_not_found", "Không tìm thấy first-frame trong cảnh.");
        if (frame.Status != SceneFirstFrameStatuses.Approved)
        {
            throw Conflict("scene_first_frame_not_approved", "Hãy duyệt first-frame trước khi tạo video Veo.");
        }
        var freshness = await EvaluateFreshnessAsync(frame, cancellationToken);
        if (!freshness.IsCurrent)
        {
            frame.Status = SceneFirstFrameStatuses.Invalidated;
            frame.InvalidatedAtUtc ??= UtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
            throw Conflict("scene_first_frame_stale", freshness.Reason ?? "First-frame đã lỗi thời. Hãy sinh lại.");
        }
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(input.Base64Data);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("First-frame không đúng Base64.", exception);
        }
        var validated = GeneratedImageValidator.ValidateSceneFirstFrame(bytes, checked((int)MaximumFirstFrameBytes), aspectRatio);
        if (input.MimeType != validated.MimeType || frame.MediaAsset.MimeType != validated.MimeType ||
            frame.MediaAsset.SizeBytes != bytes.LongLength ||
            !string.Equals(input.Sha256, validated.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(frame.MediaAsset.Sha256, validated.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict("scene_first_frame_file_mismatch", "File first-frame local không khớp bản đã duyệt.");
        }
        return new SceneFirstFrameVideoInputValidation(
            frame.SceneFirstFrameId,
            validated.MimeType,
            input.Base64Data,
            validated.Sha256);
    }

    private async Task<SceneFirstFrameSummary> ChangeStatusAsync(
        Guid projectId,
        Guid sceneId,
        Guid frameId,
        ChangeSceneFirstFrameStatusRequest request,
        string userId,
        Guid deviceId,
        bool approve,
        CancellationToken cancellationToken)
    {
        await accessService.RequireAsync(userId, deviceId, request.OrganizationId, projectId, cancellationToken);
        var expectedRowVersion = ParseRowVersion(request.RowVersion);
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var frame = await dbContext.SceneFirstFrames
            .Include(x => x.MediaAsset)
            .SingleOrDefaultAsync(
                x => x.SceneFirstFrameId == frameId && x.SceneId == sceneId &&
                     x.Scene.ProjectId == projectId,
                cancellationToken)
            ?? throw NotFound("scene_first_frame_not_found", "Không tìm thấy first-frame trong cảnh.");
        if (!frame.RowVersion.SequenceEqual(expectedRowVersion))
        {
            throw Conflict("scene_first_frame_concurrency_conflict", "First-frame đã được thay đổi. Hãy tải lại cảnh.");
        }
        if (frame.Status != SceneFirstFrameStatuses.PendingReview)
        {
            throw Conflict("scene_first_frame_status_conflict", "Chỉ first-frame đang chờ duyệt mới có thể đổi trạng thái này.");
        }

        var freshness = await EvaluateFreshnessAsync(frame, cancellationToken);
        if (!freshness.IsCurrent)
        {
            frame.Status = SceneFirstFrameStatuses.Invalidated;
            frame.InvalidatedAtUtc = UtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            throw Conflict("scene_first_frame_stale", freshness.Reason ?? "First-frame đã lỗi thời. Hãy sinh lại.");
        }
        if (approve)
        {
            var oldApproved = await dbContext.SceneFirstFrames
                .Where(x => x.SceneId == sceneId && x.Status == SceneFirstFrameStatuses.Approved && x.SceneFirstFrameId != frameId)
                .ToListAsync(cancellationToken);
            foreach (var old in oldApproved)
            {
                old.Status = SceneFirstFrameStatuses.Superseded;
            }
            frame.Status = SceneFirstFrameStatuses.Approved;
            frame.ApprovedByUserId = userId;
            frame.ApprovedAtUtc = UtcNow();
        }
        else
        {
            frame.Status = SceneFirstFrameStatuses.Rejected;
        }
        if (dbContext.Database.IsRelational())
        {
            dbContext.Entry(frame).Property(x => x.RowVersion).OriginalValue = expectedRowVersion;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return ToSummary(frame, freshness);
    }

    private async Task<FirstFramePreflight> RequirePreflightAsync(
        Guid projectId,
        Guid sceneId,
        Guid? organizationId,
        string userId,
        Guid deviceId,
        int? expectedPlanVersion,
        int? expectedPromptVersion,
        CancellationToken cancellationToken)
    {
        var access = await accessService.RequireAsync(userId, deviceId, organizationId, projectId, cancellationToken);
        var project = access.Project!;
        var scene = await dbContext.Scenes
            .Include(x => x.ScenePrompts)
            .SingleOrDefaultAsync(x => x.SceneId == sceneId && x.ProjectId == projectId, cancellationToken)
            ?? throw NotFound("scene_not_found", "Không tìm thấy cảnh trong dự án.");
        var prompt = scene.ScenePrompts
            .Where(x => x.Status is "Approved" or "Ready")
            .OrderByDescending(x => x.Version)
            .FirstOrDefault()
            ?? throw Conflict("scene_prompt_not_ready", "Cảnh chưa có prompt được duyệt để tạo first-frame.");
        if (project.CurrentScenePlanVersion is not { } currentPlan ||
            scene.ScenePlanVersion != currentPlan ||
            (expectedPlanVersion is not null && expectedPlanVersion != currentPlan))
        {
            throw Conflict("scene_plan_changed", "Kế hoạch cảnh đã thay đổi. Hãy tải lại dự án.");
        }
        if (expectedPromptVersion is not null && expectedPromptVersion != prompt.Version)
        {
            throw Conflict("scene_prompt_changed", "Prompt cảnh đã thay đổi. Hãy tải lại dự án.");
        }
        var structureType = await dbContext.Scripts.AsNoTracking()
            .Where(x => x.ScriptId == scene.ScriptId && x.ProjectId == projectId)
            .Select(x => x.StructureType)
            .SingleOrDefaultAsync(cancellationToken);
        var videoSnapshot = await videoPolicyResolver.ResolveAsync(
            project,
            access.OrganizationId,
            structureType == GenerationWorkflowTypes.OpenAiStructuredPlan
                ? OrganizationVideoPolicyScopes.LongForm
                : OrganizationVideoPolicyScopes.Default,
            cancellationToken);
        if (!FalVeoPolicy.AppliesToLongForm(videoSnapshot.ProviderCode, structureType))
        {
            throw new AccountApiException(
                StatusCodes.Status422UnprocessableEntity,
                "scene_first_frame_not_required",
                "First-frame riêng chỉ áp dụng cho dự án video dài đang dùng Fal/Veo.");
        }
        var dimensions = project.AspectRatio switch
        {
            "16:9" => (Width: 1280, Height: 720),
            "9:16" => (Width: 720, Height: 1280),
            _ => throw new AccountApiException(
                StatusCodes.Status422UnprocessableEntity,
                "scene_first_frame_aspect_ratio_invalid",
                "Fal/Veo chỉ hỗ trợ first-frame tỷ lệ 16:9 hoặc 9:16 trong workflow này.")
        };
        var characterIds = ParseGuidList(scene.CharacterIdsJson);
        if (characterIds.Count > 1)
        {
            throw new AccountApiException(
                StatusCodes.Status422UnprocessableEntity,
                "scene_first_frame_character_limit_exceeded",
                "Cảnh on-camera phải có đúng một nhân vật; B-roll không được gắn nhân vật.");
        }
        FirstFrameCharacterSnapshot? character = null;
        if (characterIds.Count == 1)
        {
            character = await LoadCharacterAsync(projectId, characterIds[0], cancellationToken);
        }
        var assets = await LoadAssetsAsync(projectId, sceneId, cancellationToken);
        var promptInput = new SceneFirstFramePromptInput(
            project.AspectRatio,
            scene.VisualDescription,
            scene.CameraDirection,
            scene.Lighting,
            scene.Motion,
            scene.Emotion,
            character is null
                ? null
                : new SceneFirstFrameCharacterPrompt(
                    character.Name,
                    character.Role,
                    character.VisualIdentity,
                    character.ProfileJson,
                    character.WardrobeJson,
                    character.ForbiddenChangesJson),
            assets.Select(x => new SceneFirstFrameAssetPrompt(x.AssetType, x.Name, x.CanonicalDescription)).ToArray(),
            scene.Narration,
            scene.Dialogue,
            prompt.FinalPrompt,
            prompt.NegativePrompt);
        return new FirstFramePreflight(access, project, scene, prompt, character, assets, promptInput, dimensions.Width, dimensions.Height);
    }

    private async Task<FirstFrameCharacterSnapshot> LoadCharacterAsync(Guid projectId, Guid characterId, CancellationToken cancellationToken)
    {
        var character = await dbContext.Characters.AsNoTracking()
            .Where(x => x.CharacterId == characterId && x.ProjectId == projectId)
            .Select(x => new FirstFrameCharacterSnapshot(
                x.CharacterId,
                x.Version,
                x.Name,
                x.Role,
                x.VisualIdentity,
                x.ProfileJson,
                x.WardrobeJson,
                x.ForbiddenChangesJson,
                x.Status,
                x.CharacterReferences
                    .Where(reference => reference.IsPrimary && reference.ApprovalStatus == "Approved" &&
                                        reference.MediaAsset.Status == "Ready" && reference.MediaAsset.DeletedAtUtc == null)
                    .OrderByDescending(reference => reference.CreatedAtUtc)
                    .Select(reference => new FirstFrameReferenceSnapshot(
                        reference.CharacterReferenceId,
                        reference.MediaAsset.MimeType,
                        reference.MediaAsset.Sha256,
                        reference.MediaAsset.SizeBytes,
                        reference.MediaAsset.Width,
                        reference.MediaAsset.Height))
                    .FirstOrDefault()))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw Conflict("character_not_ready", "Không tìm thấy nhân vật đã gắn với cảnh.");
        if (character.Status != "Approved" || character.Reference is null)
        {
            throw Conflict("character_not_ready", "Hãy khóa nhân vật và duyệt ảnh tham chiếu trước khi tạo first-frame.");
        }
        return character;
    }

    private async Task<IReadOnlyList<FirstFrameAssetSnapshot>> LoadAssetsAsync(Guid projectId, Guid sceneId, CancellationToken cancellationToken)
    {
        var assignments = await dbContext.SceneAssetAssignments.AsNoTracking()
            .Include(x => x.ProjectAsset).ThenInclude(x => x.Versions)
            .Where(x => x.SceneId == sceneId && x.Scene.ProjectId == projectId && x.ProjectAsset.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        var result = new List<FirstFrameAssetSnapshot>(assignments.Count);
        foreach (var assignment in assignments)
        {
            var asset = assignment.ProjectAsset;
            if (asset.Status != ProjectAssetStatuses.Locked || asset.CurrentVersion <= 0)
            {
                throw Conflict("scene_asset_not_locked", $"Tài sản “{asset.Name}” chưa được khóa.");
            }
            var version = asset.Versions.SingleOrDefault(x => x.Version == asset.CurrentVersion)
                ?? throw Conflict("scene_asset_version_missing", $"Không tìm thấy phiên bản đã khóa của tài sản “{asset.Name}”.");
            result.Add(new FirstFrameAssetSnapshot(
                asset.ProjectAssetId,
                version.ProjectAssetVersionId,
                version.Version,
                version.AssetType,
                version.Name,
                version.CanonicalDescription));
        }
        return result.OrderBy(x => x.AssetType, StringComparer.Ordinal).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static OpenAiImageEditInput? ValidateSourceImage(
        SceneFirstFrameCharacterInput? input,
        FirstFrameCharacterSnapshot? character)
    {
        if (character is null)
        {
            if (input is not null)
            {
                throw new ArgumentException("Cảnh B-roll không được gửi ảnh nhân vật.");
            }
            return null;
        }
        var expected = character.Reference!;
        if (input is null || input.CharacterReferenceId != expected.CharacterReferenceId)
        {
            throw Conflict("character_reference_required", "Cần gửi đúng ảnh primary đã duyệt của nhân vật.");
        }
        if (input.MimeType != expected.MimeType || input.MimeType is not ("image/png" or "image/jpeg") ||
            !string.Equals(input.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Metadata ảnh tham chiếu nhân vật không hợp lệ.");
        }
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(input.Base64Data);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Ảnh tham chiếu nhân vật không đúng Base64.", exception);
        }
        if (bytes.LongLength != expected.SizeBytes || bytes.LongLength is <= 0 or > 10 * 1024 * 1024)
        {
            throw new ArgumentException("Dung lượng ảnh tham chiếu nhân vật không hợp lệ.");
        }
        var info = GeneratedImageValidator.ReadImageInfo(bytes);
        if (info.MimeType != input.MimeType || info.Width != 1024 || info.Height != 1024 ||
            expected.Width != info.Width || expected.Height != info.Height ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expected.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Ảnh tham chiếu không khớp bản primary 1024x1024 đã duyệt.");
        }
        return new OpenAiImageEditInput(bytes, input.MimeType, input.MimeType == "image/png" ? "reference.png" : "reference.jpg");
    }

    private async Task<Freshness> EvaluateFreshnessAsync(SceneFirstFrame frame, CancellationToken cancellationToken)
    {
        var current = await dbContext.Scenes.AsNoTracking()
            .Where(x => x.SceneId == frame.SceneId)
            .Select(x => new
            {
                x.Project.CurrentScenePlanVersion,
                x.Project.AspectRatio,
                x.ScenePlanVersion,
                x.CharacterIdsJson,
                SceneFingerprint = new { x.VisualDescription, x.CameraDirection, x.Lighting, x.Motion, x.Emotion },
                Prompt = x.ScenePrompts.Where(p => p.Status == "Approved" || p.Status == "Ready")
                    .OrderByDescending(p => p.Version)
                    .Select(p => new { p.ScenePromptId, p.Version, p.PromptHash })
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (current is null || current.CurrentScenePlanVersion != frame.ScenePlanVersion || current.ScenePlanVersion != frame.ScenePlanVersion)
        {
            return new(false, "Kế hoạch cảnh đã thay đổi.");
        }
        if (current.Prompt is null || current.Prompt.ScenePromptId != frame.ScenePromptId || current.Prompt.Version != frame.ScenePromptVersion)
        {
            return new(false, "Prompt cảnh đã thay đổi.");
        }
        if (!string.Equals(current.AspectRatio, frame.AspectRatio, StringComparison.Ordinal))
        {
            return new(false, "Tỷ lệ dự án đã thay đổi.");
        }
        if (frame.MediaAsset.Status != "Ready" || frame.MediaAsset.DeletedAtUtc is not null ||
            frame.MediaAsset.SizeBytes is <= 0 or > MaximumFirstFrameBytes ||
            !HasExpectedDimensions(frame.MediaAsset.Width, frame.MediaAsset.Height, frame.AspectRatio))
        {
            return new(false, "File first-frame không còn hợp lệ.");
        }
        var request = await dbContext.ProviderRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProviderRequestId == frame.GeneratedByProviderRequestId, cancellationToken);
        if (request is null || request.Status != "Completed" || string.IsNullOrWhiteSpace(request.ResponseJson))
        {
            return new(false, "Không còn snapshot request đã tạo first-frame.");
        }
        GenerateSceneFirstFrameResponse generated;
        try
        {
            generated = ReadGenerationResponse(request.ResponseJson);
        }
        catch (AccountApiException)
        {
            return new(false, "Snapshot output first-frame không còn hợp lệ.");
        }
        if (generated.ProviderRequestId != request.ProviderRequestId ||
            frame.MediaAsset.MimeType != generated.MimeType ||
            frame.MediaAsset.SizeBytes != generated.SizeBytes ||
            frame.MediaAsset.Width != generated.Width || frame.MediaAsset.Height != generated.Height ||
            !string.Equals(frame.MediaAsset.Sha256, generated.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return new(false, "Media asset không còn khớp output first-frame đã tạo.");
        }
        GenerationMetadata metadata;
        try
        {
            metadata = ReadGenerationMetadata(request.RequestJson);
        }
        catch (AccountApiException)
        {
            return new(false, "Snapshot nguồn first-frame không còn hợp lệ.");
        }
        if (!string.Equals(current.Prompt.PromptHash, metadata.ScenePromptHash, StringComparison.OrdinalIgnoreCase) ||
            metadata.SourceCharacterReferenceId != frame.SourceCharacterReferenceId)
        {
            return new(false, "Prompt hoặc nguồn nhân vật của cảnh đã thay đổi.");
        }
        var currentSceneFingerprint = Sha256Hex(JsonSerializer.Serialize(current.SceneFingerprint, JsonOptions));
        if (!string.Equals(currentSceneFingerprint, metadata.SceneFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return new(false, "Nội dung hình ảnh của cảnh đã thay đổi.");
        }
        List<Guid> currentCharacterIds;
        try
        {
            currentCharacterIds = ParseGuidList(current.CharacterIdsJson);
        }
        catch (AccountApiException)
        {
            return new(false, "Danh sách nhân vật của cảnh không còn hợp lệ.");
        }
        if (frame.SourceCharacterReferenceId is { } sourceReference)
        {
            if (currentCharacterIds.Count != 1)
            {
                return new(false, "Nhân vật của cảnh đã thay đổi.");
            }
            var stillPrimary = await dbContext.CharacterReferences.AsNoTracking().AnyAsync(
                x => x.CharacterReferenceId == sourceReference && x.CharacterId == currentCharacterIds[0] &&
                     x.IsPrimary && x.ApprovalStatus == "Approved" &&
                     x.MediaAsset.Status == "Ready" && x.MediaAsset.DeletedAtUtc == null &&
                     x.MediaAsset.Sha256 == metadata.SourceCharacterReferenceSha256,
                cancellationToken);
            if (!stillPrimary)
            {
                return new(false, "Ảnh primary của nhân vật đã thay đổi.");
            }
        }
        else if (currentCharacterIds.Count != 0)
        {
            return new(false, "Cảnh B-roll đã được gắn nhân vật.");
        }
        var snapshotIds = await dbContext.ProviderRequestAssetVersions.AsNoTracking()
            .Where(x => x.ProviderRequestId == request.ProviderRequestId)
            .OrderBy(x => x.AppliedOrder)
            .Select(x => x.ProjectAssetVersionId)
            .ToListAsync(cancellationToken);
        var currentIds = await dbContext.SceneAssetAssignments.AsNoTracking()
            .Where(x => x.SceneId == frame.SceneId && x.ProjectAsset.Status == ProjectAssetStatuses.Locked)
            .Select(x => x.ProjectAsset.Versions
                .Where(v => v.Version == x.ProjectAsset.CurrentVersion)
                .Select(v => v.ProjectAssetVersionId)
                .Single())
            .ToListAsync(cancellationToken);
        if (!snapshotIds.Order().SequenceEqual(currentIds.Order()))
        {
            return new(false, "Tài sản đã khóa của cảnh đã thay đổi.");
        }
        return new(true, null);
    }

    private static SceneFirstFrameSummary ToSummary(SceneFirstFrame frame, Freshness freshness) =>
        new(
            frame.SceneFirstFrameId,
            frame.SceneId,
            frame.MediaAssetId,
            frame.GeneratedByProviderRequestId ?? Guid.Empty,
            frame.Version,
            frame.Status,
            frame.SourceCharacterReferenceId,
            frame.ScenePlanVersion,
            frame.ScenePromptId,
            frame.ScenePromptVersion,
            frame.AspectRatio,
            frame.PromptTemplateVersion,
            frame.MediaAsset.RelativePath,
            frame.MediaAsset.MimeType,
            frame.MediaAsset.Sha256,
            frame.MediaAsset.SizeBytes,
            frame.MediaAsset.Width ?? 0,
            frame.MediaAsset.Height ?? 0,
            freshness.IsCurrent,
            freshness.Reason,
            Convert.ToBase64String(frame.RowVersion),
            frame.CreatedAtUtc,
            frame.ApprovedAtUtc,
            frame.InvalidatedAtUtc);

    private async Task<ProviderRuntimeConfiguration> ResolveImageProviderAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var provider = await providerResolver.ResolveAsync(organizationId, ProviderCodes.OpenAi, "Image", null, cancellationToken);
        if (!string.Equals(provider.ModelCode, "gpt-image-2", StringComparison.Ordinal))
        {
            throw new AccountApiException(
                StatusCodes.Status503ServiceUnavailable,
                "openai_image_model_not_configured",
                "Model ảnh đang hoạt động phải là gpt-image-2.");
        }
        return provider;
    }

    private async Task<AiCostQuote> QuoteAsync(ProviderRuntimeConfiguration provider, string prompt, CancellationToken cancellationToken)
    {
        var quote = await costEstimator.QuoteOpenAiImageAsync(
            provider.ProviderModelId,
            prompt.Length,
            _imageOptions.EstimatedInputTokens,
            _imageOptions.EstimatedOutputTokens,
            cancellationToken);
        if (quote.EstimatedCost <= 0)
        {
            throw new AccountApiException(
                StatusCodes.Status503ServiceUnavailable,
                "pricing_not_configured",
                "Chưa cấu hình đơn giá AI hợp lệ cho model này.");
        }
        return quote;
    }

    private async Task TrySettleAsync(
        Guid reservationId,
        decimal actualCost,
        Guid? credentialId,
        object usage,
        string rateSnapshotJson,
        CancellationToken cancellationToken)
    {
        try
        {
            await budgetService.SettleAsync(
                reservationId,
                actualCost,
                credentialId,
                usage,
                JsonSerializer.Deserialize<JsonElement>(rateSnapshotJson),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not settle scene first-frame reservation {ReservationId}.", reservationId);
        }
    }

    private async Task<Scene> RequireSceneAsync(Guid projectId, Guid sceneId, CancellationToken cancellationToken)
    {
        return await dbContext.Scenes.SingleOrDefaultAsync(
                   x => x.SceneId == sceneId && x.ProjectId == projectId,
                   cancellationToken)
               ?? throw NotFound("scene_not_found", "Không tìm thấy cảnh trong dự án.");
    }

    private static GenerationMetadata ReadGenerationMetadata(string requestJson)
    {
        try
        {
            using var document = JsonDocument.Parse(requestJson);
            var root = document.RootElement;
            return new GenerationMetadata(
                root.GetProperty("scenePlanVersion").GetInt32(),
                root.GetProperty("scenePromptId").GetGuid(),
                root.GetProperty("scenePromptVersion").GetInt32(),
                root.GetProperty("scenePromptHash").GetString() ?? string.Empty,
                root.GetProperty("aspectRatio").GetString() ?? string.Empty,
                root.GetProperty("promptTemplateVersion").GetString() ?? string.Empty,
                root.GetProperty("promptHash").GetString() ?? string.Empty,
                root.GetProperty("sceneFingerprint").GetString() ?? string.Empty,
                root.GetProperty("assetVersionHash").GetString() ?? string.Empty,
                root.TryGetProperty("sourceCharacterReferenceId", out var source) && source.ValueKind == JsonValueKind.String
                    ? source.GetGuid()
                    : null,
                root.TryGetProperty("sourceCharacterReferenceSha256", out var sourceHash) && sourceHash.ValueKind == JsonValueKind.String
                    ? sourceHash.GetString()
                    : null);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw Conflict("scene_first_frame_snapshot_invalid", "Snapshot tạo first-frame không hợp lệ.");
        }
    }

    private static GenerateSceneFirstFrameResponse ReadGenerationResponse(string responseJson)
    {
        try
        {
            return JsonSerializer.Deserialize<GenerateSceneFirstFrameResponse>(responseJson, JsonOptions)
                   ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw Conflict("scene_first_frame_snapshot_invalid", "Snapshot output first-frame không hợp lệ.");
        }
    }

    private static string NormalizeRelativePath(string value, Guid projectId, Guid sceneId)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains(':'))
        {
            throw new ArgumentException("Đường dẫn first-frame phải là đường dẫn tương đối trong workspace.");
        }
        var normalized = value.Replace('\\', '/').Trim('/');
        if (normalized.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new ArgumentException("Đường dẫn first-frame không được thoát khỏi workspace.");
        }
        var prefix = $"projects/{projectId:N}/scenes/{sceneId:N}/first-frames/";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || normalized.Length == prefix.Length)
        {
            throw new ArgumentException("Đường dẫn first-frame không thuộc đúng project/scene.");
        }
        return normalized;
    }

    private static byte[] ParseRowVersion(string value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            return bytes.Length == 8 ? bytes : throw new FormatException();
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("RowVersion first-frame không hợp lệ.", nameof(value), exception);
        }
    }

    private static void ValidateGenerateRequest(GenerateSceneFirstFrameRequest request)
    {
        if (request.ProjectId == Guid.Empty || request.SceneId == Guid.Empty ||
            request.ScenePlanVersion <= 0 || request.ScenePromptVersion <= 0 || request.Attempt <= 0)
        {
            throw new ArgumentException("Project, scene, version hoặc attempt không hợp lệ.");
        }
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 450 ||
            request.IdempotencyKey.Contains('\r') || request.IdempotencyKey.Contains('\n'))
        {
            throw new ArgumentException("Idempotency key không hợp lệ.");
        }
    }

    private static void EnsureIdempotentReplay(ProviderRequest existing, GenerateSceneFirstFrameRequest request, string requestHash)
    {
        if (existing.ProjectId != request.ProjectId || existing.SceneId != request.SceneId ||
            !string.Equals(existing.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict("idempotency_key_conflict", "Idempotency key đã được dùng cho một request khác.");
        }
    }

    private static AccountApiException ExistingRequestError(ProviderRequest request) =>
        request.Status == "Failed"
            ? new AccountApiException(
                StatusCodes.Status502BadGateway,
                request.ErrorCode ?? "scene_first_frame_generation_failed",
                request.ErrorMessage ?? "Request tạo first-frame trước đó đã thất bại.")
            : Conflict("generation_in_progress", "Request tạo first-frame đang được xử lý.");

    private static List<Guid> ParseGuidList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            throw Conflict("scene_character_snapshot_invalid", "Danh sách nhân vật của cảnh không hợp lệ.");
        }
    }

    private static string SceneFingerprint(Scene scene) => Sha256Hex(JsonSerializer.Serialize(new
    {
        scene.VisualDescription,
        scene.CameraDirection,
        scene.Lighting,
        scene.Motion,
        scene.Emotion
    }, JsonOptions));

    private static string AssetVersionHash(IReadOnlyList<FirstFrameAssetSnapshot> assets) =>
        Sha256Hex(string.Join('|', assets.Select(x => $"{x.ProjectAssetVersionId:D}:{x.Version}")));

    private static bool HasExpectedDimensions(int? width, int? height, string aspectRatio) =>
        aspectRatio switch
        {
            "16:9" => width == 1280 && height == 720,
            "9:16" => width == 720 && height == 1280,
            _ => false
        };

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string SafeCode(string value) => value.Length <= 100 ? value : value[..100];
    private static string SafeMessage(string value) => value.Length <= 4000 ? value : value[..4000];
    private static AccountApiException NotFound(string code, string message) => new(StatusCodes.Status404NotFound, code, message);
    private static AccountApiException Conflict(string code, string message) => new(StatusCodes.Status409Conflict, code, message);
    private static AccountApiException Gone(string code, string message) => new(StatusCodes.Status410Gone, code, message);
    private static OpenAiImageOptions ValidateOptions(OpenAiImageOptions options) { options.Validate(); return options; }
    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record FirstFramePreflight(
        GenerationAccessContext Access,
        Project Project,
        Scene Scene,
        ScenePrompt Prompt,
        FirstFrameCharacterSnapshot? Character,
        IReadOnlyList<FirstFrameAssetSnapshot> Assets,
        SceneFirstFramePromptInput PromptInput,
        int Width,
        int Height);

    private sealed record FirstFrameCharacterSnapshot(
        Guid CharacterId,
        int Version,
        string Name,
        string? Role,
        string? VisualIdentity,
        string ProfileJson,
        string? WardrobeJson,
        string? ForbiddenChangesJson,
        string Status,
        FirstFrameReferenceSnapshot? Reference);

    private sealed record FirstFrameReferenceSnapshot(Guid CharacterReferenceId, string MimeType, string Sha256, long SizeBytes, int? Width, int? Height);
    private sealed record FirstFrameAssetSnapshot(Guid ProjectAssetId, Guid ProjectAssetVersionId, int Version, string AssetType, string Name, string CanonicalDescription);
    private sealed record Freshness(bool IsCurrent, string? Reason);
    private sealed record GenerationMetadata(int ScenePlanVersion, Guid ScenePromptId, int ScenePromptVersion, string ScenePromptHash, string AspectRatio, string PromptTemplateVersion, string PromptHash, string SceneFingerprint, string AssetVersionHash, Guid? SourceCharacterReferenceId, string? SourceCharacterReferenceSha256);
}

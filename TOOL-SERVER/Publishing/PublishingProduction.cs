using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Models;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Publishing;

namespace TOOL_SERVER.Publishing;

internal interface IPublishingProduction { Task StepAsync(PublishingRun run, CancellationToken ct); }

internal sealed class PublishingProduction(PublishingDbContext db, VideoFactoryDbContext video,
    PublishingService schedules, IShortVideoOutfitService outfits, IGenerationService generation,
    PublishingMedia media, TimeProvider time) : IPublishingProduction
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task StepAsync(PublishingRun run, CancellationToken ct)
    {
        var input = PublishingService.Read<PublishingScheduleInput>(run.InputJson);
        if (run.Status == "Queued")
        {
            if (run.ProductionConsentAtUtc == default) throw PublishingCalendar.Error("publishing_consent_required", "Lượt chạy thiếu snapshot quyền tạo tự động.", 409);
            await media.PreflightAsync(ct);
            await schedules.ValidateTargetsAsync(input.Targets, run.UserId, true, ct);
            await CreateProjectAsync(run, input, ct);
            var character = (await schedules.ImageAsync(input.CharacterImageId, run.OrganizationId, run.UserId, "Character", ct)).Info;
            var product = (await schedules.ImageAsync(input.ProductImageId, run.OrganizationId, run.UserId, "Product", ct)).Info;
            var existing = await outfits.GetAsync(run.ProjectId!.Value, run.OrganizationId, run.UserId, run.DeviceId, ct);
            if (existing.Revision == 0)
                await outfits.SaveAsync(new(run.ProjectId.Value, run.OrganizationId, 0, character, product, input.Description, input.Description), run.UserId, run.DeviceId, ct);
            else if (existing.Revision != 1 || existing.Character != character || existing.Outfit != product || existing.Background != input.Description || existing.Motion != input.Description)
                throw PublishingCalendar.Error("publishing_project_changed", "Dự án đã thay đổi ngoài lịch; không tiếp tục tạo tự động.", 409);
            run.Status = "PreparingImage"; await SaveAsync(run, ct); return;
        }
        var project = run.ProjectId!.Value;
        if (run.Status == "PreparingImage")
        {
            var quote = await outfits.QuoteAsync(new(project, run.OrganizationId, 1, "Image"), run.UserId, run.DeviceId, ct);
            RequireQuoteWithinLimit(quote, run.EstimatedCost, input.MaximumCostPerRun);
            run.ImageQuoteId = quote.QuoteId; run.EstimatedCost += quote.EstimatedCost;
            run.Status = "SubmittingImage"; await SaveAsync(run, ct);
            var character = await schedules.ImageAsync(input.CharacterImageId, run.OrganizationId, run.UserId, "Character", ct);
            var product = await schedules.ImageAsync(input.ProductImageId, run.OrganizationId, run.UserId, "Product", ct);
            await outfits.ComposeAsync(new(project, run.OrganizationId, quote.QuoteId, character, product), run.UserId, run.DeviceId, ct);
            run.Status = "ImageReady"; await SaveAsync(run, ct); return;
        }
        if (run.Status == "SubmittingImage")
        {
            // An interrupted submission is reconciled by its durable operation, never by issuing another quote.
            var operation = await video.ShortVideoOperations.AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == run.ImageQuoteId, ct);
            if (operation is { Status: "PendingReview" or "Approved" }) run.Status = "ImageReady";
            else { run.Status = "NeedsAttention"; run.ErrorCode = "publishing_image_reconciliation"; run.Message = "Lần tạo ảnh bị gián đoạn. Cần đối soát thao tác cũ trước khi phát sinh chi phí mới."; }
            await SaveAsync(run, ct); return;
        }
        if (run.Status == "ImageReady")
        {
            // This approval is delegated by the schedule's explicit production consent. It is not a human visual review.
            await outfits.ApproveAsync(new(project, run.OrganizationId, run.ImageQuoteId!.Value, 1, true), run.UserId, run.DeviceId, ct);
            var actual = await video.ProviderRequests.Where(x => x.ProviderRequestId == run.ImageQuoteId).Select(x => x.ActualCost).SingleAsync(ct);
            run.EstimatedCost = Math.Max(run.EstimatedCost, actual);
            run.Status = "PreparingVideo"; await SaveAsync(run, ct); return;
        }
        if (run.Status == "PreparingVideo")
        {
            var quote = await outfits.QuoteAsync(new(project, run.OrganizationId, 1, "Video"), run.UserId, run.DeviceId, ct);
            RequireQuoteWithinLimit(quote, run.EstimatedCost, input.MaximumCostPerRun);
            run.VideoQuoteId = quote.QuoteId; run.EstimatedCost += quote.EstimatedCost;
            run.Status = "SubmittingVideo"; await SaveAsync(run, ct);
            var image = await video.GeneratedImageOutputs.AsNoTracking().SingleAsync(x => x.ProviderRequestId == run.ImageQuoteId && x.ExpiresAtUtc > Now, ct);
            var frame = await video.SceneFirstFrames.AsNoTracking().SingleAsync(x => x.SceneId == run.SceneId && x.GeneratedByProviderRequestId == run.ImageQuoteId && x.Status == "Approved", ct);
            var payload = Convert.ToBase64String(image.Payload);
            var response = await generation.SubmitVideoAsync(new(project, run.SceneId!.Value, $"outfit-video:{quote.QuoteId:N}", run.OrganizationId,
                ScenePlanVersion: 1, ScenePromptVersion: 1, FirstFrame: new(frame.SceneFirstFrameId, image.MimeType, payload, image.Sha256),
                ShortVideoComposition: new(run.ImageQuoteId!.Value, quote.QuoteId, 1, image.MimeType, payload, image.Sha256)), run.UserId, run.DeviceId, ct);
            run.ProviderRequestId = response.ProviderRequestId; run.Status = "Generating"; await SaveAsync(run, ct); return;
        }
        if (run.Status == "SubmittingVideo")
        {
            var key = $"outfit-video:{run.VideoQuoteId:N}";
            var request = await video.ProviderRequests.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == run.OrganizationId && x.IdempotencyKey == key && x.ProjectId == project && x.RequestedByUserId == run.UserId, ct);
            if (request is not null) { run.ProviderRequestId = request.ProviderRequestId; run.Status = "Generating"; }
            else { run.Status = "NeedsAttention"; run.ErrorCode = "publishing_video_reconciliation"; run.Message = "Lần tạo video bị gián đoạn. Không gửi thêm request cho đến khi đối soát xong."; }
            await SaveAsync(run, ct); return;
        }
        if (run.Status == "Generating")
        {
            var request = await video.ProviderRequests.AsNoTracking().SingleAsync(x => x.ProviderRequestId == run.ProviderRequestId && x.ProjectId == project && x.RequestedByUserId == run.UserId, ct);
            if (request.Status == "Completed") { run.Status = "Validating"; await SaveAsync(run, ct); }
            else if (request.Status is "Failed" or "Cancelled" or "Expired")
            { run.Status = "Failed"; run.ErrorCode = "publishing_generation_failed"; run.Message = "Provider không tạo được video. Xem thao tác gốc để đối soát chi phí."; await SaveAsync(run, ct); }
            return;
        }
        if (run.Status == "Validating")
        {
            var result = await media.PrepareAsync(run, ct);
            run.MediaSha256 = result.Sha256; run.MediaSizeBytes = result.SizeBytes;
            run.Status = input.Targets.Any(x => x.Platform == "TikTok") ? "AwaitingReview" : "ReadyToPublish";
            run.Message = run.Status == "AwaitingReview" ? "Video đã tạo. Xem trước và xác nhận cài đặt TikTok trước khi đăng." : null;
            await SaveAsync(run, ct);
        }
    }

    internal static void RequireQuoteWithinLimit(ShortVideoQuote quote, decimal used, decimal maximum)
    {
        if (quote.CurrencyCode != "USD" || quote.EstimatedCost <= 0 || used < 0 || used + quote.EstimatedCost > maximum)
            throw PublishingCalendar.Error("publishing_cost_limit", "Báo giá mới vượt giới hạn USD đã cho phép cho lượt này. Không gửi yêu cầu AI tiếp theo.", 409);
    }

    private async Task SaveAsync(PublishingRun run, CancellationToken ct) { run.UpdatedAtUtc = Now; await db.SaveChangesAsync(ct); }

    private async Task CreateProjectAsync(PublishingRun run, PublishingScheduleInput input, CancellationToken ct)
    {
        // IDs are persisted before workflow writes; a crash between contexts reuses the same project.
        run.ProjectId ??= run.RunId; run.SceneId ??= Guid.NewGuid(); await db.SaveChangesAsync(ct);
        if (await video.Projects.AnyAsync(x => x.ProjectId == run.ProjectId && x.OrganizationId == run.OrganizationId && x.RemoteUserId == run.UserId, ct)) return;
        var projectId = run.ProjectId.Value; var scriptId = Guid.NewGuid(); var styleId = Guid.NewGuid(); var now = Now;
        var content = input.Description;
        var canonical = PublishingService.Write(new { workflow = "scheduled-product-v1", content, durationSeconds = input.DurationSeconds,
            providerDurationSeconds = input.DurationSeconds, aspectRatio = input.AspectRatio, nativeAudio = true, outputAudioEnabled = true,
            muteOutputAudio = false, speechMode = "None", schedulingConsent = run.ScheduleId, schedulingRevision = run.ScheduleRevision });
        var project = new Project { ProjectId = projectId, OrganizationId = run.OrganizationId, CreatedByUserId = run.UserId, RemoteUserId = run.UserId,
            RemoteDeviceId = run.DeviceId, Name = input.Title, Topic = content, LanguageCode = "vi-VN", Platform = "YouTubeShorts", AspectRatio = input.AspectRatio,
            TargetDurationSeconds = input.DurationSeconds, OutputWidth = input.AspectRatio == "9:16" ? 720 : 1280,
            OutputHeight = input.AspectRatio == "9:16" ? 1280 : 720, OutputFrameRate = 30, Status = "ScenePlanning", CurrentScriptVersion = 1,
            CurrentStyleVersion = 1, CurrentScenePlanVersion = 1, CurrencyCode = "USD", WorkspaceRelativePath = $"publishing/{projectId:N}",
            CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[8] };
        var script = new Script { ScriptId = scriptId, ProjectId = projectId, Version = 1, StructureType = "DirectShortVideo", Title = input.Title,
            FullText = content, StoryBeatsJson = "[]", NarrationJson = "[]", DialogueJson = "[]", Status = "Approved", CreatedAtUtc = now, ApprovedAtUtc = now, RowVersion = new byte[8] };
        var style = new StyleProfile { StyleProfileId = styleId, ProjectId = projectId, Version = 1, Name = "Scheduled product video",
            VisualStyleJson = "{}", CameraStyleJson = "{}", LightingStyleJson = "{}", EnvironmentJson = "{}", NegativeRulesJson = "[]", Status = "Approved", CreatedAtUtc = now, ApprovedAtUtc = now, RowVersion = new byte[8] };
        var scene = new Scene { SceneId = run.SceneId.Value, ProjectId = projectId, ScriptId = scriptId, StyleProfileId = styleId,
            ScenePlanVersion = 1, SequenceNumber = 1, StoryPurpose = "Video nhân vật giới thiệu sản phẩm theo lịch đã xác nhận.",
            VisualDescription = content, ContentDurationMs = input.DurationSeconds * 1000L, GenerationDurationMs = input.DurationSeconds * 1000L,
            TimelineEndMs = input.DurationSeconds * 1000L, CharacterIdsJson = "[]", EntryStateJson = "{}", ExitStateJson = "{}",
            RequiredCapabilitiesJson = PublishingService.Write(new { shortVideoMode = "CharacterOutfit", scheduledProduct = true, textToVideo = false,
                requiresFirstFrame = true, maxDurationSeconds = 8, requestedDurationSeconds = input.DurationSeconds, providerDurationSeconds = input.DurationSeconds,
                aspectRatio = input.AspectRatio, nativeAudio = true, outputAudioEnabled = true, muteOutputAudio = false, speechMode = "None" }),
            Status = "PromptReady", CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[8] };
        var prompt = new ScenePrompt { ScenePromptId = Guid.NewGuid(), SceneId = scene.SceneId, Version = 1, PromptTemplateName = "scheduled-product",
            PromptTemplateVersion = "1", CanonicalInputJson = canonical, FinalPrompt = content, NegativePrompt = "distorted anatomy, duplicate products, flicker, watermark",
            PromptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content + canonical))).ToLowerInvariant(), Status = "Approved", CreatedAtUtc = now, ApprovedAtUtc = now, RowVersion = new byte[8] };
        video.AddRange(project, script, style, scene, prompt); await video.SaveChangesAsync(ct);
    }
}

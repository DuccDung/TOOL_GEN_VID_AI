using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Models;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_SERVER.Generation;

internal sealed record TextVeoQuoteData(Guid FrameId, string FrameHash, Guid PromptId, int PromptVersion,
    string PromptHash, int PlanVersion, string Model, string Resolution, bool NativeAudio, int Duration, string AspectRatio,
    Guid ProviderId, Guid ModelId, Guid CredentialId, decimal Cost, string Currency, string RateSnapshot);

internal sealed partial class ShortVideoOutfitService
{
    public async Task<ShortVideoQuote> QuoteTextVideoAsync(ShortVideoQuoteRequest request, string user, Guid device, CancellationToken ct)
    {
        var allowed = await access.RequireAsync(user, device, request.OrganizationId, request.ProjectId, ct);
        var project = allowed.Project!;
        var scene = await db.Scenes.Include(x => x.ScenePrompts).SingleOrDefaultAsync(x => x.ProjectId == request.ProjectId && x.ScenePlanVersion == project.CurrentScenePlanVersion, ct);
        if (scene is null || IsOutfit(scene.RequiredCapabilitiesJson) || !await db.Scripts.AnyAsync(x => x.ScriptId == scene.ScriptId && x.StructureType == GenerationWorkflowTypes.DirectShortVideo, ct))
            throw Error("short_video_mode_invalid", "Báo giá này chỉ dành cho video ngắn từ nội dung.");
        await NoPendingVideoAsync(scene.SceneId, ct);
        var snapshot = await policies.ResolveAsync(project, request.OrganizationId, OrganizationVideoPolicyScopes.LongForm, ct);
        ShortVideoVeoPolicy.Validate(snapshot, project);
        var frame = await db.SceneFirstFrames.Include(x => x.MediaAsset).SingleOrDefaultAsync(x => x.SceneId == scene.SceneId && x.Status == SceneFirstFrameStatuses.Approved, ct)
            ?? throw Error("fal_first_frame_required", "Hãy duyệt ảnh đầu vào Veo trước khi lấy báo giá video.");
        var prompt = scene.ScenePrompts.Where(x => x.Status is "Approved" or "Ready").OrderByDescending(x => x.Version).First();
        var provider = await providers.ResolveModelAsync(request.OrganizationId, snapshot.ProviderCode, "Video", snapshot.ModelCode, null, true, ct);
        var price = await costs.QuoteVideoAsync(snapshot.ProviderCode, provider.ProviderModelId, project.TargetDurationSeconds, snapshot.Resolution, snapshot.NativeAudio, snapshot.Capabilities.FramesPerSecond, ct);
        var budget = await budgets.GetSnapshotAsync(request.OrganizationId, ct);
        if (price.EstimatedCost <= 0 || price.CurrencyCode != budget.CurrencyCode) throw Error("pricing_not_configured", "Chưa có giá Veo hợp lệ.");
        if (budget.HardLimit <= 0 || budget.RemainingBudget < price.EstimatedCost) throw Error("organization_budget_exceeded", "Ngân sách không đủ để tạo video Veo.");
        var data = new TextVeoQuoteData(frame.SceneFirstFrameId, frame.MediaAsset.Sha256!, prompt.ScenePromptId, prompt.Version, prompt.PromptHash,
            scene.ScenePlanVersion, snapshot.ModelCode, snapshot.Resolution, snapshot.NativeAudio, project.TargetDurationSeconds, project.AspectRatio,
            provider.ProviderId, provider.ProviderModelId, provider.OrganizationProviderCredentialId, price.EstimatedCost, price.CurrencyCode, price.RateSnapshotJson);
        var operation = new ShortVideoOperation { OperationId = Guid.NewGuid(), ProjectId = project.ProjectId, SceneId = scene.SceneId,
            OrganizationId = request.OrganizationId, UserId = user, Kind = "Video", Revision = prompt.Version,
            QuoteJson = JsonSerializer.Serialize(data, Json), CreatedAtUtc = Now, ExpiresAtUtc = Now.AddMinutes(10) };
        db.ShortVideoOperations.Add(operation); await db.SaveChangesAsync(ct);
        return new(operation.OperationId, "Video", price.EstimatedCost, price.CurrencyCode, snapshot.ModelCode, snapshot.Resolution, snapshot.NativeAudio, operation.ExpiresAtUtc, prompt.Version);
    }

    internal async Task<(AiCostQuote Quote, Guid CredentialId)> ValidateTextQuoteAsync(SubmitVideoRequest request, Project project,
        ProjectVideoSnapshot snapshot, string user, CancellationToken ct)
    {
        if (request.ShortVideoQuoteId is not { } quoteId || request.IdempotencyKey != $"short-video:{quoteId:N}" || request.FirstFrame is null)
            throw Error("short_video_quote_required", "Hãy lấy báo giá và xác nhận chi phí video Veo trước.");
        var operation = await Operation(quoteId, project.ProjectId, project.OrganizationId!.Value, user, "Video", ct);
        var data = JsonSerializer.Deserialize<TextVeoQuoteData>(operation.QuoteJson, Json)!;
        var prompt = await db.ScenePrompts.AsNoTracking().SingleOrDefaultAsync(x => x.SceneId == request.SceneId && x.ScenePromptId == data.PromptId, ct);
        if (operation.SceneId != request.SceneId || prompt is null || prompt.PromptHash != data.PromptHash || prompt.Version != request.ScenePromptVersion ||
            data.PlanVersion != request.ScenePlanVersion || data.FrameId != request.FirstFrame.SceneFirstFrameId || data.FrameHash != request.FirstFrame.Sha256 ||
            data.Model != snapshot.ModelCode || data.Resolution != snapshot.Resolution || data.NativeAudio != snapshot.NativeAudio ||
            data.Duration != project.TargetDurationSeconds || data.AspectRatio != project.AspectRatio)
            throw Error("short_video_quote_changed", "Ảnh, nội dung hoặc model đã đổi. Hãy lấy báo giá mới.");
        var submitted = await db.ProviderRequests.AnyAsync(x => x.OrganizationId == operation.OrganizationId && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (!submitted && (operation.ExpiresAtUtc <= Now || operation.Status != "Quoted"))
            throw Error("short_video_quote_expired", "Báo giá đã dùng hoặc hết hạn. Hãy lấy báo giá mới.");
        return (new(data.Cost, data.Currency, data.RateSnapshot), data.CredentialId);
    }

    internal async Task ClaimTextQuoteAsync(Guid quoteId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var operation = await db.ShortVideoOperations.SingleAsync(x => x.OperationId == quoteId, ct);
        await db.Entry(operation).ReloadAsync(ct);
        if (operation.Status != "Quoted" || operation.ExpiresAtUtc <= Now) throw Error("short_video_quote_expired", "Báo giá đã dùng hoặc hết hạn.");
        await NoPendingVideoAsync(operation.SceneId, ct);
        if (await db.ShortVideoOperations.AnyAsync(x => x.SceneId == operation.SceneId && x.OperationId != quoteId && x.Kind == "Video" && (x.Status == "Submitting" || x.Status == "Unknown"), ct))
            throw Error("short_video_operation_pending", "Một thao tác video trước đang xử lý hoặc chưa rõ kết quả.");
        operation.Status = "Submitting";
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}

using System.Text.Json;
using TOOL_LOCAL.Generation;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_LOCAL.WebView;

internal sealed partial class DashboardBridge
{
    private readonly ShortVideoWorkflowService? _shortVideoOutfit;
    private readonly Func<string?>? _shortVideoImageSelector;
    private readonly Dictionary<Guid, (Guid Project, Guid Org, string User, ShortVideoQuote Quote)> _outfitQuotes = [];

    private async Task HandleShortVideoOutfitAsync(WebMessageRequest request, CancellationToken ct)
    {
        var service = _shortVideoOutfit ?? throw new ArgumentException("Chức năng phối đồ chưa được bật.");
        var action = request.Payload.Deserialize<ShortVideoOutfitAction>(_jsonOptions) ?? throw new ArgumentException("Yêu cầu không hợp lệ.");
        var (project, user) = CurrentProjectOwner();
        var org = _generationClient.SelectedOrganizationId ?? throw new ArgumentException("Hãy chọn tổ chức.");
        if (action.ProjectId != project || action.OrganizationId != org) throw new ArgumentException("Dự án hoặc tổ chức đã đổi.");
        bool Current() => _selectedProjectId == project && _generationClient.SelectedOrganizationId == org && _sessionManager.Current?.User.UserId == user;
        void Reply(string type, object? payload) { if (Current()) Post(new(type, request.RequestId, payload)); }
        async Task State(CancellationToken token) => Reply("outfit.state", await service.GetAsync(project, user, org, token));
        if (request.Type == "outfit.get") { await State(ct); return; }
        await RunExclusiveGenerationAsync(request.RequestId, async token =>
        {
            switch (request.Type)
            {
                case "outfit.text-quote":
                    var textQuote = await _generationClient.QuoteShortTextVideoAsync(new(project, org, action.Revision, "Video"), token);
                    _outfitQuotes[textQuote.QuoteId] = (project, org, user, textQuote);
                    Reply("outfit.text-quote", textQuote); return;
                case "outfit.text-video":
                    if (!action.Confirmed || action.QuoteId is not { } textQuoteId || !_outfitQuotes.TryGetValue(textQuoteId, out var textBound) ||
                        textBound.Project != project || textBound.Org != org || textBound.User != user || textBound.Quote.Kind != "Video")
                        throw new ArgumentException("Hãy lấy báo giá Veo và xác nhận chi phí trước.");
                    await _mediaToolPreflight.RequireReadyAsync(token);
                    await service.PrepareTextVideoAsync(project, user, org, textBound.Quote, token);
                    await _generationService.GenerateVideosAsync(project, user, null, null, token);
                    Reply("outfit.text-done", new { projectId = project }); return;
                case "outfit.text-resume":
                    await service.RequireTextResumeAsync(project, user, org, token);
                    await _generationService.GenerateVideosAsync(project, user, null, null, token, resumeOnly: true);
                    Reply("outfit.text-done", new { projectId = project }); return;
                case "outfit.migrate":
                    if (!action.Confirmed) throw new ArgumentException("Hãy xác nhận chuyển dự án sang Veo.");
                    var migration = await _generationClient.MigrateShortVideoToVeoAsync(new(project, org, action.ExpectedProviderCode, action.DurationSeconds, action.AspectRatio), token);
                    foreach (var id in _outfitQuotes.Where(x => x.Value.Project == project).Select(x => x.Key).ToArray()) _outfitQuotes.Remove(id);
                    Reply("outfit.migrated", migration);
                    return;
                case "outfit.import":
                    if (action.Kind is not ("Character" or "Outfit")) throw new ArgumentException("Loại ảnh không hợp lệ.");
                    var path = _shortVideoImageSelector?.Invoke();
                    if (!string.IsNullOrWhiteSpace(path)) Reply("outfit.imported", new { kind = action.Kind, image = await service.ImportAsync(project, user, org, path, token) });
                    else Reply("outfit.cancelled", new { projectId = project });
                    return;
                case "outfit.save":
                    await service.SaveAsync(new(project, org, action.Revision, action.Character ?? throw new ArgumentException("Thiếu ảnh nhân vật."), action.Outfit ?? throw new ArgumentException("Thiếu ảnh trang phục."), action.Background ?? "", action.Motion ?? ""), user, token);
                    break;
                case "outfit.quote":
                    var quote = await _generationClient.QuoteOutfitAsync(new(project, org, action.Revision, action.Kind ?? ""), token);
                    foreach (var expired in _outfitQuotes.Where(x => x.Value.Quote.ExpiresAtUtc < DateTime.UtcNow).Select(x => x.Key).ToArray()) _outfitQuotes.Remove(expired);
                    _outfitQuotes[quote.QuoteId] = (project, org, user, quote);
                    Reply("outfit.quote", quote); return;
                case "outfit.compose":
                case "outfit.video":
                    if (!action.Confirmed || action.QuoteId is not { } quoteId || !_outfitQuotes.TryGetValue(quoteId, out var bound) || bound.Project != project || bound.Org != org || bound.User != user ||
                        bound.Quote.Revision != action.Revision || bound.Quote.Kind != (request.Type == "outfit.compose" ? "Image" : "Video"))
                        throw new ArgumentException("Hãy lấy báo giá và xác nhận chi phí trước.");
                    if (request.Type == "outfit.compose") await service.ComposeAsync(project, user, org, quoteId, token);
                    else
                    {
                        await _mediaToolPreflight.RequireReadyAsync(token);
                        await service.SaveVideoQuoteAsync(project, user, org, bound.Quote, token);
                        await service.PrepareVideoAsync(project, user, org, token);
                        await _generationService.GenerateVideosAsync(project, user, null, null, token);
                    }
                    break;
                case "outfit.resume":
                    await _generationService.GenerateVideosAsync(project, user, null, null, token, resumeOnly: true); break;
                case "outfit.approve":
                case "outfit.reject":
                    if (!action.Confirmed || action.CompositionId is null) throw new ArgumentException("Hãy xem và xác nhận ảnh trước.");
                    var currentView = await service.GetAsync(project, user, org, token);
                    if (request.Type == "outfit.approve" && currentView.CompositionPreview is null) throw new ArgumentException("Hãy tải và xem ảnh hợp lệ trước khi duyệt.");
                    await _generationClient.ApproveOutfitAsync(new(project, org, action.CompositionId.Value, action.Revision, request.Type == "outfit.approve"), token); break;
                case "outfit.approve-video":
                    if (!action.Confirmed) throw new ArgumentException("Hãy xem video trước khi duyệt.");
                    var dashboard = await _projectService.GetDashboardAsync(project, user, token) ?? throw new ArgumentException("Không tìm thấy dự án.");
                    await _projectService.ApproveSceneNativeAudioAsync(project, user, dashboard.Scenes.Single().SceneId, true, token); break;
                case "outfit.render":
                    await _projectRenderService.RenderFinalVideoAsync(project, user, token); break;
                default: throw new ArgumentException("Thao tác phối đồ không hợp lệ.");
            }
            await State(token);
        }, ct);
    }
}

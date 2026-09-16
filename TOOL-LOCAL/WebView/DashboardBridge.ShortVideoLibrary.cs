using System.Text.Json;
using TOOL_LOCAL.Projects;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_LOCAL.WebView;

internal sealed partial class DashboardBridge
{
    private readonly SemaphoreSlim _shortLibraryLock = new(1, 1);

    private async Task HandleShortVideoLibraryAsync(WebMessageRequest request, CancellationToken ct)
    {
        if (_shortVideoOutfit?.Enabled != true) throw new ArgumentException("Chức năng nhân vật và trang phục chưa được bật.");
        var action = request.Payload.Deserialize<ShortVideoLibraryAction>(_jsonOptions) ?? throw new ArgumentException("Yêu cầu thư viện không hợp lệ.");
        var session = _sessionManager.Current ?? throw new ArgumentException("Hãy đăng nhập lại.");
        var user = session.User.UserId;
        var org = _generationClient.SelectedOrganizationId ?? throw new ArgumentException("Hãy chọn tổ chức.");
        var selected = _selectedProjectId;
        void RequireCurrent()
        {
            if (action.OrganizationId != org || _generationClient.SelectedOrganizationId != org || _sessionManager.Current?.User.UserId != user || _selectedProjectId != selected ||
                (action.ProjectId.HasValue && action.ProjectId != selected) || _licenseManager.IsLocked)
                throw new ArgumentException("Phiên, tổ chức hoặc dự án đã thay đổi. Hãy tải lại trang.");
        }
        RequireCurrent();
        await _shortLibraryLock.WaitAsync(ct);
        try
        {
            RequireCurrent();
            var library = _shortVideoOutfit.Library;
            if (action.ProjectId.HasValue)
            {
                var project = await _projectService.GetDashboardAsync(action.ProjectId.Value, user, ct);
                if (project?.Project.OrganizationId != org || project.ShortVideoMode != ShortVideoModes.CharacterOutfit) throw new ArgumentException("Không tìm thấy dự án phối đồ trong tổ chức này.");
            }
            object? result;
            switch (request.Type)
            {
                case "short-library.get":
                    result = await library.GetAsync(user, org, action.ProjectId, ct); break;
                case "short-library.pick":
                    var path = _shortVideoImageSelector?.Invoke(); RequireCurrent();
                    result = string.IsNullOrWhiteSpace(path) ? null : await library.PickAsync(user, org, path, ct); break;
                case "short-library.commit":
                    result = await library.CommitAsync(user, org, action.Kind ?? "", action.Name ?? "", action.UploadId ?? throw new ArgumentException("Hãy chọn ảnh."), action.AssetId, action.Version, ct); break;
                case "short-library.rename":
                    result = await library.RenameAsync(user, org, action.AssetId ?? Guid.Empty, action.Version, action.Name ?? "", ct); break;
                case "short-library.delete":
                    result = await library.DeleteAsync(user, org, action.AssetId ?? Guid.Empty, action.Version, ct); break;
                case "short-library.save-draft":
                    if (_generationRunning) throw new ArgumentException("Hãy chờ tác vụ hiện hành hoàn tất.");
                    result = await library.SaveDraftAsync(user, org, action.ProjectId, action.Revision, action.Draft ?? throw new ArgumentException("Thiếu bản nháp."), ct); break;
                case "short-library.copy":
                    if (_generationRunning || action.ProjectId is null) throw new ArgumentException("Hãy chờ tác vụ hiện hành và mở dự án cần sao chép.");
                    var next = action.Draft ?? throw new ArgumentException("Thiếu bản nháp.");
                    if (next.Character is null || next.Outfit is null) throw new ArgumentException("Hãy lưu cả hai ảnh vào thư viện trước khi tạo bản sao.");
                    var newDraft = (await library.GetAsync(user, org, null, ct)).Draft;
                    result = await library.SaveDraftAsync(user, org, null, newDraft.Revision, next with { ServerRevision = 0 }, ct); break;
                case "short-library.import-current":
                    if (action.ProjectId is not { } sourceProject || action.Kind is not ("Character" or "Outfit")) throw new ArgumentException("Hãy mở dự án và chọn loại ảnh.");
                    var state = await _shortVideoOutfit.GetAsync(sourceProject, user, org, ct);
                    var info = (action.Kind == "Character" ? state.State.Character : state.State.Outfit) ?? throw new ArgumentException("Dự án chưa có ảnh này.");
                    result = await library.StageAsync(user, org, action.Kind == "Character" ? "Nhân vật đang dùng" : "Trang phục đang dùng",
                        await _shortVideoOutfit.ReadSourceAsync(sourceProject, user, org, info, ct), ct); break;
                case "short-library.apply":
                case "short-library.create":
                    result = null;
                    Guid? appliedProject = null;
                    await RunExclusiveGenerationAsync(request.RequestId, async token =>
                    {
                        RequireCurrent();
                        var saved = (await library.GetAsync(user, org, action.ProjectId, token)).Draft;
                        var draft = saved.Draft ?? throw new ArgumentException("Hãy lưu bản nháp trước.");
                        if (saved.Revision != action.Revision) throw new ArgumentException("Bản nháp đã thay đổi. Hãy tải lại trạng thái.");
                        var background = string.IsNullOrWhiteSpace(draft.Background) ? draft.Content.Trim() : draft.Background.Trim();
                        var motion = string.IsNullOrWhiteSpace(draft.Motion) ? draft.Content.Trim() : draft.Motion.Trim();
                        if (string.IsNullOrWhiteSpace(draft.Content) || background.Length is < 1 or > 1500 || motion.Length is < 1 or > 2000)
                            throw new ArgumentException("Hãy nhập nội dung; bối cảnh tối đa 1.500 ký tự. Nội dung dài hơn cần bối cảnh riêng.");
                        Guid target;
                        if (request.Type == "short-library.create")
                        {
                            if (action.ProjectId.HasValue || draft.Character is null || draft.Outfit is null) throw new ArgumentException("Hãy chọn đủ nhân vật và trang phục cho bản nháp mới.");
                            await RequireProjectCreationOrganizationAsync(org, token); RequireCurrent();
                            // Validate full-resolution inputs before reserving or creating a project.
                            await library.ReadAsync(user, org, draft.Character, "Character", token);
                            await library.ReadAsync(user, org, draft.Outfit, "Outfit", token);
                            saved = await library.ReserveProjectAsync(user, org, saved.Revision, token);
                            target = saved.CreatedProjectId!.Value;
                            await _projectService.CreateShortVideoAsync(new(draft.Content, draft.AspectRatio, draft.DurationSeconds, draft.AudioEnabled, org, ShortVideoModes.CharacterOutfit, target), session.User, session.DeviceId, token);
                        }
                        else target = action.ProjectId ?? throw new ArgumentException("Hãy mở dự án hiện hành.");
                        appliedProject = target;
                        RequireCurrent();
                        var current = await _shortVideoOutfit.GetAsync(target, user, org, token);
                        var character = current.State.Character; var outfit = current.State.Outfit;
                        if (draft.Character is { } characterRef)
                        {
                            var image = await library.ReadAsync(user, org, characterRef, "Character", token);
                            character = (await _shortVideoOutfit.AttachAsync(target, user, org, image.Bytes, image.Asset.Image, token)).Info;
                        }
                        if (draft.Outfit is { } outfitRef)
                        {
                            var image = await library.ReadAsync(user, org, outfitRef, "Outfit", token);
                            outfit = (await _shortVideoOutfit.AttachAsync(target, user, org, image.Bytes, image.Asset.Image, token)).Info;
                        }
                        var matches = character == current.State.Character && outfit == current.State.Outfit && background == current.State.Background && motion == current.State.Motion;
                        if (!matches)
                        {
                            if (current.State.Revision != draft.ServerRevision) throw new ArgumentException("Thiết lập dự án đã thay đổi. Hãy tải lại trước khi lưu.");
                            await _shortVideoOutfit.SaveAsync(new(target, org, current.State.Revision, character ?? throw new ArgumentException("Thiếu nhân vật."), outfit ?? throw new ArgumentException("Thiếu trang phục."), background, motion), user, token);
                            current = await _shortVideoOutfit.GetAsync(target, user, org, token);
                        }
                        RequireCurrent();
                        draft = draft with { ServerRevision = current.State.Revision };
                        if (request.Type == "short-library.create") await library.CompleteProjectAsync(user, org, target, draft, token);
                        else await library.SaveDraftAsync(user, org, target, saved.Revision, draft, token);
                        result = new { projectId = target, view = current, library = await library.GetAsync(user, org, target, token) };
                        _selectedProjectId = target;
                    }, ct);
                    if (_generationClient.SelectedOrganizationId == org && _sessionManager.Current?.User.UserId == user && _selectedProjectId == appliedProject)
                    {
                        if (request.Type == "short-library.create" && _selectedProjectId is { } createdProject)
                        {
                            ShortVideoQuote? createdQuote = null; string? quoteMessage = null;
                            try
                            {
                                var createdState = await _shortVideoOutfit.GetAsync(createdProject, user, org, ct);
                                createdQuote = await _generationClient.QuoteOutfitAsync(new(createdProject, org, createdState.State.Revision, "Image"), ct);
                                _outfitQuotes[createdQuote.QuoteId] = (createdProject, org, user, createdQuote);
                            }
                            catch (TOOL_LOCAL.Authentication.AccountClientException error) when (error.StatusCode != 401)
                            { quoteMessage = "Đã lưu dự án. Chưa lấy được báo giá: " + error.Message; }
                            if (_generationClient.SelectedOrganizationId == org && _sessionManager.Current?.User.UserId == user && _selectedProjectId == createdProject)
                                Post(new("short-library.created", request.RequestId, new ShortVideoCreatedNotice(createdProject, org, createdQuote, quoteMessage)));
                        }
                        if (_generationClient.SelectedOrganizationId == org && _sessionManager.Current?.User.UserId == user && _selectedProjectId == appliedProject)
                            Post(new("short-library.result", request.RequestId, result));
                    }
                    return;
                default: throw new ArgumentException("Thao tác thư viện không hợp lệ.");
            }
            RequireCurrent();
            Post(new("short-library.result", request.RequestId, result));
        }
        catch (InvalidDataException)
        { throw new ArgumentException("Ảnh không hợp lệ hoặc dữ liệu ảnh đã thay đổi. Hãy chọn lại ảnh PNG/JPEG, tối đa 10 MiB và 16 megapixel."); }
        catch (IOException)
        { throw new ArgumentException("Không thể đọc hoặc lưu ảnh/bản nháp. Hãy kiểm tra dung lượng ổ đĩa, quyền truy cập và thử lại; lựa chọn hiện tại vẫn được giữ."); }
        catch (Microsoft.Data.Sqlite.SqliteException)
        { throw new ArgumentException("Không thể lưu hoặc mở thư viện trên máy. Hãy kiểm tra dung lượng ổ đĩa và đóng cửa sổ taphoatool khác đang dùng cùng thư viện rồi thử lại."); }
        finally { _shortLibraryLock.Release(); }
    }
}

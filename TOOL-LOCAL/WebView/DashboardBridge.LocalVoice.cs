using System.Text.Json;
using TOOL_LOCAL.LocalVoice;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_LOCAL.WebView;

internal sealed partial class DashboardBridge
{
    private readonly LocalVoiceService? _localVoice;

    private async Task HandleLocalVoiceAsync(WebMessageRequest request, CancellationToken token)
    {
        if (_localVoice is null) throw new ArgumentException("Bộ xử lý giọng local chưa được cấu hình.");
        var action = request.Payload.Deserialize<LocalVoiceActionRequest>(_jsonOptions)
            ?? throw new ArgumentException("Thiếu yêu cầu giọng local.");
        var user = _sessionManager.Current?.User.UserId ?? throw new ArgumentException("Hãy đăng nhập lại.");
        var org = _generationClient.SelectedOrganizationId ?? throw new ArgumentException("Hãy chọn organization.");
        if (action.ProjectId == Guid.Empty || action.ProjectId != _selectedProjectId)
            throw new ArgumentException("Project của yêu cầu không còn được chọn.");
        if (_generationRunning && request.Type is not ("local-voice.get" or "local-voice.cancel"))
            throw new ArgumentException("Hãy chờ tác vụ tạo nội dung/video hoàn tất.");
        bool StillSelected() => !_disposed && _selectedProjectId == action.ProjectId &&
            _generationClient.SelectedOrganizationId == org && _sessionManager.Current?.User.UserId == user;
        void Changed()
        {
            if (StillSelected()) Post(new WebMessageResponse("local-voice.changed", null, new { projectId = action.ProjectId }));
        }
        try
        {
            switch (request.Type)
            {
                case "local-voice.get": break;
                case "local-voice.enable":
                    await _localVoice.EnableAsync(action.ProjectId, user, org, action.Confirmed, token); break;
                case "local-voice.install":
                    await _localVoice.InstallAsync(action.ProjectId, org, action.Confirmed, token); break;
                case "local-voice.prepare-anchor":
                    if (!action.Confirmed) throw new ArgumentException("Cần xác nhận nguồn chỉ có một người nói và bạn có quyền sử dụng giọng.");
                    await _localVoice.RunAsync(action.ProjectId, user, org,
                        [action.SceneId ?? throw new ArgumentException("Hãy chọn cảnh làm mẫu.")], true, Changed, token); break;
                case "local-voice.convert":
                    await _localVoice.RunAsync(action.ProjectId, user, org, action.SceneIds ?? [], false, Changed, token); break;
                case "local-voice.approve":
                case "local-voice.reject":
                    await _localVoice.ReviewAsync(action.ProjectId, user, org,
                        action.JobId ?? action.AnchorId ?? throw new ArgumentException("Hãy chọn kết quả."),
                        request.Type == "local-voice.approve", action.Confirmed, action.Reason, token); break;
                case "local-voice.use-native":
                    await _localVoice.UseNativeAsync(action.ProjectId, user, org,
                        action.SceneId ?? throw new ArgumentException("Hãy chọn cảnh."), action.Confirmed, action.Reason, token); break;
                case "local-voice.cancel": _localVoice.Cancel(action.ProjectId); break;
                case "local-voice.cleanup":
                    var removed = await _localVoice.CleanupAsync(action.ProjectId, user, org, action.Confirmed, token);
                    if (StillSelected()) Post(new WebMessageResponse("operation.notice", request.RequestId,
                        new { message = $"Đã xóa {removed} file trung gian của kết quả đã duyệt/từ chối; giữ clip gốc, mẫu và kết quả. Có thể tạo lại file trung gian bằng xử lý local." }));
                    break;
                default: throw new ArgumentException("Thao tác giọng local không được hỗ trợ.");
            }
            if (StillSelected())
            {
                Post(new WebMessageResponse("local-voice.state", request.RequestId,
                    await _localVoice.GetAsync(action.ProjectId, user, org, token)));
                if (request.Type is not ("local-voice.get" or "local-voice.cancel")) await RefreshAsync(null, token);
            }
        }
        catch (OperationCanceledException) { if (StillSelected()) PostError(request.RequestId, "local_voice_cancelled", "Đã hủy tác vụ giọng local."); }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or JsonException)
        { if (StillSelected()) PostError(request.RequestId, "local_voice_failed", "Không hoàn tất xử lý giọng local. Kiểm tra component, nguồn và mẫu giọng rồi thử lại."); }
    }
}

using System.Diagnostics;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Generation;
using TOOL_LOCAL.TikTok;
using TOOL_LOCAL.WebView;
using TOOL_SHARED.Contracts.Publishing;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_LOCAL.Publishing;

internal sealed class PublishingWebBridge(IGenerationClient api, AccountSessionManager session,
    LicenseSessionManager license, TikTokMediaService media, ITikTokGatewayClient tikTok,
    Func<string?> selectImage, Action<string> post) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TikTokMediaPreviewService _preview = new(media);
    private readonly HashSet<string> _previewUrls = [];
    private string? _user;
    private Guid? _organization;
    private PublishingState? _state;
    private Guid? _previewRunId;
    private string? _previewSha;
    private bool _disposed;

    internal TikTokPreviewResponse? Open(Uri uri, string method, string? range)
    {
        if (_disposed || _user != session.Current?.User.UserId || _organization != api.SelectedOrganizationId || license.IsLocked || !_previewUrls.Contains(uri.AbsoluteUri)) return null;
        return _preview.Open(uri, method, range);
    }

    public async Task<bool> TryHandleAsync(string json, CancellationToken ct)
    {
        if (_disposed || string.IsNullOrWhiteSpace(json) || json.Length > 128 * 1024) return false;
        WebMessageRequest? request;
        try { request = JsonSerializer.Deserialize<WebMessageRequest>(json, Json); }
        catch (JsonException) { return false; }
        if (request?.Type.StartsWith("publishing.", StringComparison.Ordinal) != true) return false;
        var acquired = false;
        try
        {
            if (!Guid.TryParse(request.RequestId, out _)) throw new ArgumentException("Yêu cầu lên lịch thiếu mã đối chiếu hợp lệ.");
            if (!session.IsAuthenticated || api.SelectedOrganizationId is not { } org || session.Current is null) throw new ArgumentException("Chọn tổ chức và đăng nhập trước khi lên lịch.");
            var user = session.Current.User.UserId;
            var payload = request.Payload.Deserialize<PublishingWebPayload>(Json) ?? throw new ArgumentException("Thiếu dữ liệu lên lịch.");
            if (payload.OrganizationId != org) throw new ArgumentException("Tổ chức đã thay đổi. Hãy tải lại lịch.");
            if (!await _gate.WaitAsync(0, ct)) throw new ArgumentException("Đang xử lý thao tác lịch khác. Vui lòng chờ.");
            acquired = true;
            if (_user != user || _organization != org) Reset();
            _user = user; _organization = org;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            linked.CancelAfter(TimeSpan.FromMinutes(5)); ct = linked.Token;
            await license.EnsureAccessAsync(ct);
            object? data = null;
            switch (request.Type)
            {
                case "publishing.state.get":
                    _state = await api.GetPublishingStateAsync(org, ct); data = _state; break;
                case "publishing.image.select":
                    if (payload.Role is not ("Character" or "Product")) throw new ArgumentException("Vai trò ảnh không hợp lệ.");
                    var path = selectImage();
                    if (path is null) break;
                    var bytes = ShortVideoWorkflowService.NormalizeImage(await ShortVideoWorkflowService.ReadBoundedAsync(path, ct));
                    var info = ShortVideoWorkflowService.Inspect(bytes);
                    var uploaded = await api.UploadPublishingImageAsync(new(org, payload.Role, new(info, Convert.ToBase64String(bytes))), ct);
                    using (var source = new MemoryStream(bytes))
                    using (var bitmap = Image.FromStream(source))
                    using (var thumb = bitmap.GetThumbnailImage(Math.Max(1, bitmap.Width * 180 / Math.Max(bitmap.Width, bitmap.Height)),
                        Math.Max(1, bitmap.Height * 180 / Math.Max(bitmap.Width, bitmap.Height)), null, IntPtr.Zero))
                    using (var output = new MemoryStream())
                    {
                        thumb.Save(output, ImageFormat.Png);
                        var url = media.SetAvatar(uploaded.ImageId, new(output.ToArray(), "image/png"));
                        _previewUrls.Add(url); data = new { image = uploaded, previewUrl = url };
                    }
                    break;
                case "publishing.schedule.save":
                    if (payload.Input is null || payload.ScheduleId is null || payload.ExpectedRevision is null) throw new ArgumentException("Thiếu dữ liệu lịch.");
                    data = await api.SavePublishingScheduleAsync(new(org, payload.ScheduleId.Value, payload.ExpectedRevision.Value, payload.Input), ct); break;
                case "publishing.schedule.change":
                    if (payload.ScheduleId is null || payload.ExpectedRevision is null || payload.Action is null) throw new ArgumentException("Thiếu thao tác lịch.");
                    data = await api.ChangePublishingScheduleAsync(new(org, payload.ScheduleId.Value, payload.ExpectedRevision.Value, payload.Action, payload.ConfirmAutomaticGeneration), ct); break;
                case "publishing.oauth.connect":
                    if (payload.Platform is not ("Facebook" or "YouTube")) throw new ArgumentException("Kết nối TikTok tại mục Đăng TikTok.");
                    var oauth = await api.StartPublishingOAuthAsync(new(org, payload.Platform), ct);
                    var uri = new Uri(oauth.AuthorizationUrl);
                    if (uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length > 0 || uri.Host is not ("accounts.google.com" or "www.facebook.com")) throw new InvalidDataException("Địa chỉ kết nối không hợp lệ.");
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); break;
                case "publishing.connection.disconnect":
                    await api.DisconnectPublishingAsync(payload.ConnectionId ?? throw new ArgumentException("Thiếu tài khoản."), ct); break;
                case "publishing.run.action":
                    await api.PublishingRunActionAsync(new(org, payload.RunId ?? throw new ArgumentException("Thiếu lượt chạy."), payload.Action ?? ""), ct); break;
                case "publishing.run.preview":
                    var state = await api.GetPublishingStateAsync(org, ct);
                    var run = state.Runs.SingleOrDefault(x => x.RunId == payload.RunId) ?? throw new ArgumentException("Lượt chạy không thuộc danh sách hiện hành.");
                    if (run.MediaSha256 is null) throw new ArgumentException("Video chưa sẵn sàng để xem trước.");
                    var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoMaker", "Publishing", "Preview",
                        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user)))[..20], org.ToString("N"));
                    ValidateCacheDirectory(folder); Directory.CreateDirectory(folder);
                    var videoPath = Path.Combine(folder, run.RunId.ToString("N") + ".mp4");
                    PrunePreviewCache(folder);
                    if (File.Exists(videoPath) && (File.GetAttributes(videoPath) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Cache video không hợp lệ.");
                    if (File.Exists(videoPath + ".part") && (File.GetAttributes(videoPath + ".part") & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Cache video tạm không hợp lệ.");
                    await api.DownloadPublishingPreviewAsync(org, run.RunId, run.MediaSha256, videoPath, ct);
                    var selected = await media.SelectAsync(videoPath, ct);
                    _previewUrls.Add(selected.PreviewUrl); _previewRunId = run.RunId; _previewSha = run.MediaSha256;
                    var creators = new List<TikTokCreatorInfoResponse>();
                    foreach (var target in run.Input.Targets.Where(x => x.Platform == "TikTok"))
                        creators.Add((await tikTok.GetCreatorInfoAsync(ct, target.ConnectionId)) with { AvatarUrl = null });
                    data = new { run, previewUrl = selected.PreviewUrl, creators }; break;
                case "publishing.run.review":
                    if (payload.RunId != _previewRunId || _previewSha is null || payload.MediaSha256 != _previewSha || payload.Targets is null)
                        throw new ArgumentException("Xem trước đúng video hiện hành trước khi xác nhận đăng.");
                    _ = media.RequireCurrent();
                    await api.ReviewPublishingRunAsync(new(org, payload.RunId!.Value, _previewSha, payload.Approve, payload.Targets), ct); break;
                case "publishing.post.open":
                    _state = await api.GetPublishingStateAsync(org, ct);
                    var targetPost = _state.Runs.SelectMany(x => x.Deliveries).SingleOrDefault(x => x.DeliveryId == payload.DeliveryId && x.Status == "Completed")?.PostUrl;
                    if (!Uri.TryCreate(targetPost, UriKind.Absolute, out var postUri) || postUri.Scheme != "https" || postUri.Port != 443 ||
                        postUri.UserInfo.Length > 0 || postUri.Host is not ("www.facebook.com" or "www.youtube.com" or "www.tiktok.com")) throw new ArgumentException("Chưa có đường dẫn bài đăng hợp lệ.");
                    Process.Start(new ProcessStartInfo(postUri.AbsoluteUri) { UseShellExecute = true }); break;
                default: throw new ArgumentException("Thao tác lịch không được hỗ trợ.");
            }
            if (_organization != api.SelectedOrganizationId || _user != session.Current?.User.UserId) { Reset(); throw new ArgumentException("Ngữ cảnh đã thay đổi. Tải lại lịch."); }
            post(JsonSerializer.Serialize(new WebMessageResponse("publishing.result", request.RequestId, new { organizationId = org, action = request.Type, data }), Json));
        }
        catch (Exception ex) when (ex is AccountClientException or ArgumentException or InvalidDataException or IOException or HttpRequestException or OperationCanceledException or JsonException or TikTokDesktopException or NotSupportedException)
        {
            var code = ex is AccountClientException apiError ? apiError.Code : "publishing_operation_failed";
            var message = ex is AccountClientException or ArgumentException or InvalidDataException or TikTokDesktopException ? ex.Message :
                ex is OperationCanceledException ? "Thao tác đã dừng. Làm mới để kiểm tra kết quả đã lưu trên server." : "Không thể thực hiện thao tác lịch. Kiểm tra kết nối server và thử lại.";
            post(JsonSerializer.Serialize(new WebMessageResponse("publishing.error", request.RequestId, Error: new(code, message)), Json));
        }
        catch (Exception)
        { post(JsonSerializer.Serialize(new WebMessageResponse("publishing.error", request.RequestId, Error: new("publishing_operation_failed", "Thao tác lịch chưa hoàn tất. Làm mới để kiểm tra kết quả trên server.")), Json)); }
        finally { if (acquired) _gate.Release(); }
        return true;
    }

    private void Reset() { _previewUrls.Clear(); media.Clear(); media.RetainAvatars([]); _state = null; _previewRunId = null; _previewSha = null; }
    private static void PrunePreviewCache(string folder)
    {
        ValidateCacheDirectory(folder);
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*.mp4*", SearchOption.TopDirectoryOnly).Take(200))
        {
            var name = Path.GetFileName(file);
            var suffix = name.EndsWith(".mp4.part", StringComparison.Ordinal) ? ".mp4.part" : name.EndsWith(".mp4", StringComparison.Ordinal) ? ".mp4" : null;
            if (suffix is null || !Guid.TryParseExact(name[..^suffix.Length], "N", out _) || File.GetLastWriteTimeUtc(file) >= DateTime.UtcNow.AddDays(-7)) continue;
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
            try { File.Delete(file); } catch (IOException) { } // A preview still playing is retained until the next sweep.
        }
    }
    private static void ValidateCacheDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0)
            throw new InvalidDataException("Thư mục xem trước phải nằm trên ổ đĩa local.");
        for (DirectoryInfo? parent = new(path); parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Thư mục xem trước không được đi qua liên kết filesystem.");
    }
    public void Dispose() { _disposed = true; _lifetime.Cancel(); Reset(); }
}

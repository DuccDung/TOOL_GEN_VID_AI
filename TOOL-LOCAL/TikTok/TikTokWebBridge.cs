using System.Diagnostics;
using System.Text.Json;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.WebView;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_LOCAL.TikTok;

internal sealed class TikTokWebBridge : IDisposable
{
    private const string MessagePrefix = "tiktok.";
    private const int MaxMessageLength = 32 * 1024;
    private readonly bool _enabled;
    private readonly LicenseSessionManager _licenseManager;
    private readonly ITikTokGatewayClient _gatewayClient;
    private readonly TikTokOAuthCoordinator _oauthCoordinator;
    private readonly TikTokMediaService _mediaService;
    private readonly TikTokUploadService _uploadService;
    private readonly Func<string?> _videoFileSelector;
    private readonly Action<string> _postJson;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private CancellationTokenSource? _activeOperation;
    private string? _activeRequestId;
    private bool _disposed;

    public TikTokWebBridge(
        bool enabled,
        LicenseSessionManager licenseManager,
        ITikTokGatewayClient gatewayClient,
        TikTokOAuthCoordinator oauthCoordinator,
        TikTokMediaService mediaService,
        TikTokUploadService uploadService,
        Func<string?> videoFileSelector,
        Action<string> postJson)
    {
        _enabled = enabled;
        _licenseManager = licenseManager;
        _gatewayClient = gatewayClient;
        _oauthCoordinator = oauthCoordinator;
        _mediaService = mediaService;
        _uploadService = uploadService;
        _videoFileSelector = videoFileSelector;
        _postJson = postJson;
    }

    public async Task<bool> TryHandleAsync(string json, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxMessageLength) return false;
        WebMessageRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<WebMessageRequest>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }
        if (request?.Type?.StartsWith(MessagePrefix, StringComparison.Ordinal) != true) return false;
        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            PostError(null, "tiktok_request_id_required", "Yêu cầu TikTok phải có mã đối chiếu.");
            return true;
        }
        if (!_enabled)
        {
            PostError(request.RequestId, "tiktok_feature_disabled", "Tính năng đăng TikTok chưa được bật trên máy này.");
            return true;
        }
        if (_licenseManager.IsLocked && RequiresActiveLicense(request.Type))
        {
            PostError(request.RequestId, "license_required", _licenseManager.Current?.AccessMessage ?? "Bạn cần gói sử dụng còn hiệu lực.");
            return true;
        }

        try
        {
            switch (request.Type)
            {
                case "tiktok.state.get":
                case "tiktok.refresh":
                    await PostStateAsync(request.RequestId, cancellationToken);
                    break;
                case "tiktok.oauth.connect":
                    await RunExclusiveAsync(request.RequestId, token => ConnectAsync(request, token), cancellationToken);
                    break;
                case "tiktok.oauth.disconnect":
                    await RunExclusiveAsync(request.RequestId, token => DisconnectAsync(request, token), cancellationToken);
                    break;
                case "tiktok.creator.get":
                    await PostCreatorAsync(request, cancellationToken);
                    break;
                case "tiktok.history.get":
                    var historyInput = request.Payload.Deserialize<TikTokHistoryWebRequest>(_jsonOptions)
                        ?? throw new TikTokDesktopException("tiktok_invalid_payload", "Thiếu dữ liệu lịch sử.");
                    var history = await _gatewayClient.GetHistoryAsync(historyInput.ConnectionId, historyInput.Page, cancellationToken);
                    Post(new WebMessageResponse("tiktok.history", request.RequestId, history));
                    break;
                case "tiktok.media.select":
                    await RunExclusiveAsync(request.RequestId, token => SelectMediaAsync(request.RequestId, token), cancellationToken);
                    break;
                case "tiktok.media.clear":
                    await RunExclusiveAsync(request.RequestId, _ =>
                    {
                        _mediaService.Clear();
                        Post(new WebMessageResponse("tiktok.media.cleared", request.RequestId));
                        return Task.CompletedTask;
                    }, cancellationToken);
                    break;
                case "tiktok.publish.start":
                    await RunExclusiveAsync(
                        request.RequestId,
                        token => PublishAsync(request, request.RequestId, token),
                        cancellationToken);
                    break;
                case "tiktok.publish.status.get":
                    await PostPublishStatusAsync(request, request.RequestId, cancellationToken);
                    break;
                case "tiktok.policy.open":
                    OpenPolicy(request);
                    Post(new WebMessageResponse("tiktok.policy.opened", request.RequestId));
                    break;
                case "tiktok.operation.cancel":
                    var cancel = request.Payload.Deserialize<TikTokCancelWebRequest>(_jsonOptions);
                    if (cancel?.OperationRequestId == _activeRequestId) _activeOperation?.Cancel();
                    Post(new WebMessageResponse("tiktok.operation.cancelled", request.RequestId));
                    break;
                default:
                    PostError(request.RequestId, "tiktok_operation_not_supported", "Thao tác TikTok này chưa được hỗ trợ.");
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            PostError(request.RequestId, "tiktok_operation_cancelled", "Thao tác TikTok đã được hủy.");
        }
        catch (AccountClientException exception)
        {
            PostError(request.RequestId, exception.Code, exception.Message, exception.Errors);
        }
        catch (TikTokDesktopException exception)
        {
            PostError(request.RequestId, exception.Code, exception.Message);
        }
        catch (MediaToolUnavailableException exception)
        {
            PostError(request.RequestId, exception.Code, exception.Message);
        }
        catch (JsonException)
        {
            PostError(request.RequestId, "tiktok_invalid_payload", "Dữ liệu TikTok không đúng định dạng.");
        }
        catch (Exception)
        {
            PostError(request.RequestId, "tiktok_operation_failed", "Không thể hoàn tất thao tác TikTok.");
        }
        return true;
    }

    private async Task PostStateAsync(string requestId, CancellationToken cancellationToken)
    {
        var state = await _gatewayClient.GetStateAsync(cancellationToken);
        Post(new WebMessageResponse("tiktok.state", requestId, await WithAvatarsAsync(state, cancellationToken)));
    }

    private async Task ConnectAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        var input = request.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? new TikTokConnectWebRequest() : request.Payload.Deserialize<TikTokConnectWebRequest>(_jsonOptions)!;
        var state = await _oauthCoordinator.ConnectAsync(cancellationToken, input.TargetConnectionId);
        Post(new WebMessageResponse("tiktok.state", request.RequestId, await WithAvatarsAsync(state, cancellationToken)));
    }

    private async Task DisconnectAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        var id = RequireConnectionId(request);
        await _gatewayClient.DisconnectAsync(cancellationToken, id);
        await PostStateAsync(request.RequestId!, cancellationToken);
    }

    private async Task PostCreatorAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        var id = RequireConnectionId(request);
        var creator = await _gatewayClient.GetCreatorInfoAsync(cancellationToken, id);
        if (creator.ConnectionId != id) throw new TikTokDesktopException("tiktok_account_mismatch", "Server trả về tài khoản TikTok không khớp.");
        creator = creator with { AvatarUrl = await AvatarUrlAsync(id, creator.AvatarUrl, cancellationToken) };
        Post(new WebMessageResponse("tiktok.creator", request.RequestId, creator));
    }

    private async Task<TikTokFeatureStateResponse> WithAvatarsAsync(TikTokFeatureStateResponse state, CancellationToken token)
    {
        var accounts = state.Connections ?? (state.Connection is { } single ? [single] : []);
        _mediaService.RetainAvatars(accounts.Where(x => x.Status == "Connected").Select(x => x.ConnectionId));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        var mapped = await Task.WhenAll(accounts.Select(async (account, index) => account with
        {
            AvatarUrl = account.Status == "Connected" && index < 16
                ? await AvatarUrlAsync(account.ConnectionId, account.AvatarUrl, deadline.Token) : null
        }));
        return state with { Connections = mapped, Connection = mapped.FirstOrDefault(x => x.ConnectionId == state.Connection?.ConnectionId) };
    }

    private async Task<string?> AvatarUrlAsync(Guid id, string? source, CancellationToken token)
    {
        if (source is null) return null;
        var existing = _mediaService.GetAvatarUrl(id);
        if (existing is not null) return existing;
        var content = await _gatewayClient.GetAvatarAsync(id, token);
        return content is null ? null : _mediaService.SetAvatar(id, content);
    }

    private Guid RequireConnectionId(WebMessageRequest request)
    {
        var input = request.Payload.Deserialize<TikTokConnectionWebRequest>(_jsonOptions);
        if (input is null || input.ConnectionId == Guid.Empty)
            throw new TikTokDesktopException("tiktok_connection_required", "Hãy chọn tài khoản TikTok.");
        return input.ConnectionId;
    }

    private async Task SelectMediaAsync(string requestId, CancellationToken cancellationToken)
    {
        var path = _videoFileSelector();
        if (string.IsNullOrWhiteSpace(path))
        {
            Post(new WebMessageResponse("tiktok.media.cancelled", requestId));
            return;
        }
        var media = await _mediaService.SelectAsync(path, cancellationToken);
        Post(new WebMessageResponse("tiktok.media.selected", requestId, ToWebMedia(media)));
    }

    private async Task PublishAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload.Deserialize<TikTokPublishWebRequest>(_jsonOptions)
            ?? throw new TikTokDesktopException("tiktok_invalid_payload", "Thiếu thông tin bài đăng TikTok.");
        if (payload.ConnectionId == Guid.Empty || payload.ClientRequestId == Guid.Empty || payload.MediaId == Guid.Empty)
            throw new TikTokDesktopException("tiktok_invalid_payload", "Thiếu tài khoản, video hoặc mã lần đăng TikTok.");
        if (!payload.ConsentConfirmed)
            throw new TikTokDesktopException("tiktok_consent_required", "Bạn phải xác nhận trước khi gửi video tới TikTok.");
        if (payload.CommercialContent && !payload.BrandContent && !payload.BrandOrganic)
            throw new TikTokDesktopException("tiktok_commercial_disclosure_required", "Hãy chọn nội dung quảng bá thương hiệu của bạn, bên thứ ba hoặc cả hai.");
        if (!payload.CommercialContent && (payload.BrandContent || payload.BrandOrganic))
            throw new TikTokDesktopException("tiktok_commercial_disclosure_invalid", "Khai báo nội dung thương mại không hợp lệ.");
        if (payload.BrandContent && payload.PrivacyLevel == "SELF_ONLY")
            throw new TikTokDesktopException("tiktok_branded_content_privacy_invalid", "Nội dung hợp tác trả phí không thể đăng ở chế độ Chỉ mình tôi.");
        var media = _mediaService.RequireCurrent();
        if (media.MediaId != payload.MediaId)
            throw new TikTokDesktopException("tiktok_video_changed", "Video đã chọn đã thay đổi. Hãy tải lại trước khi đăng.");
        var initialized = await _gatewayClient.InitializePublishAsync(
            new InitializeTikTokPublishRequest(
                payload.ClientRequestId,
                payload.Title.Trim(),
                payload.PrivacyLevel,
                !payload.AllowComment,
                !payload.AllowDuet,
                !payload.AllowStitch,
                payload.BrandContent,
                payload.BrandOrganic,
                payload.IsAiGenerated,
                media.SizeBytes,
                media.DurationSeconds,
                media.MimeType,
                payload.ConnectionId),
            cancellationToken);
        if (initialized.BlockedCreator is { } blockedCreator)
        {
            if (blockedCreator.ConnectionId != payload.ConnectionId)
                throw new TikTokDesktopException("tiktok_account_mismatch", "Server trả về tài khoản TikTok không khớp.");
            Post(new WebMessageResponse("tiktok.creator", requestId, blockedCreator));
            return;
        }
        if (initialized.ConnectionId != payload.ConnectionId)
            throw new TikTokDesktopException("tiktok_account_mismatch", "Phiên đăng thuộc tài khoản TikTok khác.");
        Post(new WebMessageResponse(
            "tiktok.publish.initialized",
            requestId,
            new { publishJobId = initialized.PublishJobId, connectionId = payload.ConnectionId }));
        if (_mediaService.RequireCurrent().MediaId != media.MediaId)
            throw new TikTokDesktopException("tiktok_video_changed", "Video đã thay đổi trong lúc khởi tạo đăng.");
        var progress = new Progress<TikTokUploadProgress>(value =>
            Post(new WebMessageResponse(
                "tiktok.upload.progress",
                requestId,
                new TikTokUploadProgressWebResponse(
                    initialized.PublishJobId,
                    value.UploadedBytes,
                    value.TotalBytes,
                    value.Percent,
                    value.CompletedChunks,
                    value.TotalChunks,
                    payload.ConnectionId))));
        await _uploadService.UploadAsync(
            media,
            initialized.UploadUrl,
            initialized.ChunkSizeBytes,
            initialized.TotalChunkCount,
            progress,
            cancellationToken);
        Post(new WebMessageResponse(
            "tiktok.upload.completed",
            requestId,
            new { publishJobId = initialized.PublishJobId, connectionId = payload.ConnectionId }));
    }

    private async Task PostPublishStatusAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload.Deserialize<TikTokPublishStatusWebRequest>(_jsonOptions)
            ?? throw new TikTokDesktopException("tiktok_invalid_payload", "Thiếu mã phiên đăng TikTok.");
        if (payload.PublishJobId == Guid.Empty)
            throw new TikTokDesktopException("tiktok_invalid_payload", "Mã phiên đăng TikTok không hợp lệ.");
        var status = await _gatewayClient.GetPublishStatusAsync(payload.PublishJobId, cancellationToken);
        Post(new WebMessageResponse("tiktok.publish.status", requestId, status));
    }

    private void OpenPolicy(WebMessageRequest request)
    {
        var payload = request.Payload.Deserialize<TikTokPolicyWebRequest>(_jsonOptions)
            ?? throw new TikTokDesktopException("tiktok_invalid_payload", "Thiếu loại chính sách TikTok.");
        var url = payload.Policy switch
        {
            "music" => "https://www.tiktok.com/legal/page/global/music-usage-confirmation/en",
            "branded" => "https://www.tiktok.com/legal/page/global/bc-policy/en",
            _ => throw new TikTokDesktopException("tiktok_policy_invalid", "Chính sách TikTok không hợp lệ.")
        };
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new TikTokDesktopException("tiktok_browser_open_failed", "Không thể mở chính sách TikTok.", exception);
        }
    }

    private async Task RunExclusiveAsync(
        string requestId,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        if (!await _operationLock.WaitAsync(0, cancellationToken))
            throw new TikTokDesktopException("tiktok_operation_busy", "Một thao tác TikTok khác đang chạy.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeOperation = linked;
        _activeRequestId = requestId;
        try
        {
            await operation(linked.Token);
        }
        finally
        {
            if (ReferenceEquals(_activeOperation, linked)) { _activeOperation = null; _activeRequestId = null; }
            _operationLock.Release();
        }
    }

    private static TikTokMediaWebResponse ToWebMedia(TikTokMediaSelection media) =>
        new(
            media.MediaId,
            media.FileName,
            media.MimeType,
            media.SizeBytes,
            media.DurationSeconds,
            media.Width,
            media.Height,
            media.FramesPerSecond,
            media.VideoCodec,
            media.PreviewUrl);

    private static bool RequiresActiveLicense(string operation) =>
        operation is "tiktok.creator.get" or
            "tiktok.media.select" or
            "tiktok.publish.start";

    private void PostError(
        string? requestId,
        string code,
        string message,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        Post(new WebMessageResponse("tiktok.error", requestId, Error: new WebMessageError(code, message, errors)));

    private void Post(WebMessageResponse response)
    {
        if (!_disposed) _postJson(JsonSerializer.Serialize(response, _jsonOptions));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mediaService.RetainAvatars([]);
        _activeOperation?.Cancel();
        _activeOperation?.Dispose();
        _operationLock.Dispose();
    }
}

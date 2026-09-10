using System.Text.Json;
using TOOL_LOCAL.WebView;

namespace TOOL_LOCAL.Bilibili;

internal sealed class BilibiliWebBridge : IDisposable
{
    private readonly BilibiliService _service;
    private readonly Func<CancellationToken, Task> _authorize;
    private readonly Func<string?> _selectFolder;
    private readonly Action<string> _openFolder;
    private readonly Action<string> _post;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private bool _disposed;

    public BilibiliWebBridge(BilibiliService service, Func<CancellationToken, Task> authorize,
        Func<string?> selectFolder, Action<string> openFolder, Action<string> post)
    {
        _service = service; _authorize = authorize; _selectFolder = selectFolder; _openFolder = openFolder; _post = post;
        service.Changed += OnChanged;
    }

    public async Task<bool> TryHandleAsync(string json, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 1024 * 1024) return false;
        WebMessageRequest? request;
        try { request = JsonSerializer.Deserialize<WebMessageRequest>(json, _json); }
        catch (JsonException) { return false; }
        if (request?.Type?.StartsWith("bilibili.", StringComparison.Ordinal) != true) return false;
        if (!Guid.TryParse(request.RequestId, out _))
        {
            Reply("bilibili.error", request.RequestId, error: new("bilibili_request_invalid", "Mã yêu cầu không hợp lệ."));
            return true;
        }
        try
        {
            if (request.Type == "bilibili.cancel")
            {
                _service.Cancel(Read<BilibiliJobRequest>(request).JobId);
            }
            else
            {
                await _authorize(token);
                switch (request.Type)
                {
                    case "bilibili.state.get": await _service.RefreshAsync(token); break;
                    case "bilibili.install": await _service.InstallAsync(token); break;
                    case "bilibili.scan": await _service.ScanAsync(Read<BilibiliScanRequest>(request).Url, token); break;
                    case "bilibili.folder.select":
                        if (_service.Snapshot().Operation != "Idle") throw new BilibiliException("bilibili_busy", "Hãy chờ hoặc hủy thao tác đang chạy.");
                        var folder = _selectFolder();
                        if (!string.IsNullOrEmpty(folder)) _service.SetFolder(folder);
                        break;
                    case "bilibili.folder.open": _openFolder(_service.GetFolder(Read<BilibiliJobRequest>(request).JobId)); break;
                    case "bilibili.download":
                        var input = Read<BilibiliDownloadRequest>(request);
                        await _service.DownloadAsync(input.ScanId, input.EntryIds ?? [], input.Quality, token);
                        break;
                    case "bilibili.retry":
                        await _service.RetryAsync(Read<BilibiliJobRequest>(request).JobId ?? "", token);
                        break;
                    default: throw new BilibiliException("bilibili_unknown_action", "Thao tác Bilibili không hợp lệ.");
                }
            }
            Reply("bilibili.ack", request.RequestId);
        }
        catch (Exception exception)
        {
            Reply("bilibili.error", request.RequestId, error: new(
                exception is BilibiliException known ? known.Code : "bilibili_action_failed", BilibiliService.SafeMessage(exception)));
        }
        return true;
    }

    private T Read<T>(WebMessageRequest request) => request.Payload.Deserialize<T>(_json)
        ?? throw new BilibiliException("bilibili_payload_invalid", "Dữ liệu yêu cầu không hợp lệ.");
    private void OnChanged(BilibiliNotification notification) => Reply(notification.Type, null, notification.Payload);
    private void Reply(string type, string? id, object? payload = null, WebMessageError? error = null)
    {
        if (!_disposed) _post(JsonSerializer.Serialize(new WebMessageResponse(type, id, payload, error), _json));
    }
    public void Dispose() { _disposed = true; _service.Changed -= OnChanged; _service.Dispose(); }
}

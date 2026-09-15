using System.Text.Json;
using System.Text.Json.Serialization;
using TOOL_LOCAL.WebView;

namespace TOOL_LOCAL.SystemSetup;

internal sealed class SystemSetupBridge : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };
    private static readonly HashSet<string> Commands = new(StringComparer.Ordinal) {
        "system.setup.get", "system.setup.check", "system.setup.start", "system.setup.retry", "system.setup.cancel",
        "system.setup.exit" };
    private readonly SystemSetupCoordinator _coordinator;
    private readonly Action<string> _post;
    private readonly Action? _exitApplication;
    private readonly bool _startupRequired;
    internal static bool IsCommand(string type) => Commands.Contains(type);
    public SystemSetupBridge(
        SystemSetupCoordinator coordinator,
        Action<string> post,
        Action? exitApplication = null,
        bool startupRequired = false)
    {
        _coordinator = coordinator;
        _post = post;
        _exitApplication = exitApplication;
        _startupRequired = startupRequired;
        coordinator.Changed += OnChanged;
    }
    private SetupSnapshot ForClient(SetupSnapshot snapshot) =>
        snapshot with { StartupRequired = _startupRequired };
    private void OnChanged(string type, SetupSnapshot snapshot) => Post(type, null, ForClient(snapshot));
    private void Post(string type, string? requestId, object? payload = null, WebMessageError? error = null) =>
        _post(JsonSerializer.Serialize(new WebMessageResponse(type, requestId, payload, error), Json));
    public async Task<bool> TryHandleAsync(string json, CancellationToken token)
    {
        WebMessageRequest? request = null;
        try
        {
            if (json.Length > 8192) return false;
            // Identify the route first, then reject unknown/duplicate fields with a correlated error.
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String || !Commands.Contains(type.GetString()!)) return false;
            request = JsonSerializer.Deserialize<WebMessageRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (request is null || !Commands.Contains(request.Type)) return false;
            if (document.RootElement.EnumerateObject().Any(p => p.Name is not ("type" or "requestId" or "payload"))
                || HasDuplicates(document.RootElement)
                || (request.Payload.ValueKind == JsonValueKind.Object && HasDuplicates(request.Payload))) throw new JsonException();
            if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 128)
                throw new SetupException("system_setup_invalid_request", "Request Setup thiếu mã đối chiếu hợp lệ.");
            switch (request.Type)
            {
                case "system.setup.get":
                    if (request.Payload.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                        && (request.Payload.ValueKind != JsonValueKind.Object || request.Payload.EnumerateObject().Any()))
                        throw new JsonException();
                    Post("system.setup.status", request.RequestId, ForClient(_coordinator.GetSnapshot()));
                    break;
                case "system.setup.cancel":
                    var cancel = request.Payload.Deserialize<SystemSetupCancelRequest>(Json) ?? throw new JsonException();
                    Post("system.setup.status", request.RequestId, ForClient(_coordinator.Cancel(cancel.OperationId)));
                    break;
                case "system.setup.exit":
                    if (request.Payload.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                        && (request.Payload.ValueKind != JsonValueKind.Object || request.Payload.EnumerateObject().Any()))
                        throw new JsonException();
                    _coordinator.Invalidate();
                    _exitApplication?.Invoke();
                    break;
                default:
                    var input = request.Payload.Deserialize<SystemSetupRequest>(Json) ?? throw new JsonException();
                    await _coordinator.StartAsync(input, request.Type["system.setup.".Length..],
                        snapshot => Post("system.setup.accepted", request.RequestId, ForClient(snapshot)), token);
                    break;
            }
        }
        catch (JsonException)
        {
            if (request is null || !Commands.Contains(request.Type)) return false;
            Post("operation.error", request.RequestId, error: new("system_setup_invalid_request", "Dữ liệu Setup không hợp lệ."));
        }
        catch (Exception exception)
        {
            if (request is null || !Commands.Contains(request.Type)) return false;
            var code = SetupErrors.Code(exception);
            Post("operation.error", request.RequestId, error: new(code,
                exception is SetupException ? exception.Message : SetupErrors.Message(code)));
        }
        return true;
    }
    public void Dispose() => _coordinator.Changed -= OnChanged;
    private static bool HasDuplicates(JsonElement element) => element.EnumerateObject()
        .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
}

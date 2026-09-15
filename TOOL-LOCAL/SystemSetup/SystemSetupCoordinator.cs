namespace TOOL_LOCAL.SystemSetup;

internal sealed class SystemSetupCoordinator : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SystemSetupAuthorizer _authorizer;
    private readonly ISetupComponentAdapter[] _adapters;
    private readonly RuntimeUseGate _gate;
    private readonly SystemSetupJournal _journal;
    private readonly Func<bool> _accessValid;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<Guid, (SystemSetupRequest Request, string Mode, SetupOperation Operation, long Revision)> _history = new();
    private readonly Task _monitor;
    private SetupComponent[] _components;
    private string _generation = Guid.NewGuid().ToString("N");
    private string? _user;
    private Guid? _organization;
    private SetupOperation? _operation;
    private CancellationTokenSource? _cancellation;
    private Task _running = Task.CompletedTask;
    private long _revision;
    private bool _active;

    public SystemSetupCoordinator(SystemSetupAuthorizer authorizer, IEnumerable<ISetupComponentAdapter> adapters,
        RuntimeUseGate gate, SystemSetupJournal journal, Func<bool>? accessValid = null)
    {
        _authorizer = authorizer;
        _adapters = adapters.ToArray();
        _components = _adapters.Select(x => x is ISetupComponentStatusInspector inspector
            ? inspector.Inspect()
            : x.Component).ToArray();
        _gate = gate;
        _journal = journal;
        _accessValid = accessValid ?? (() => true);
        _monitor = Task.Run(MonitorAsync);
    }

    public event Action<string, SetupSnapshot>? Changed;
    public SetupSnapshot GetSnapshot()
    {
        lock (_sync) { RefreshContext(); return Snapshot(); }
    }
    private SetupSnapshot Snapshot() => new(_generation, _organization, _components.ToArray(), _operation, _revision);
    private void RefreshContext()
    {
        if (_user == _authorizer.UserId && _organization == _authorizer.OrganizationId) return;
        _cancellation?.Cancel();
        _user = _authorizer.UserId;
        _organization = _authorizer.OrganizationId;
        _generation = Guid.NewGuid().ToString("N");
        _revision++;
        _history.Clear();
        _operation = _user is not null && _organization is { } org ? _journal.Read(_user, org) : null;
    }

    public async Task StartAsync(SystemSetupRequest request, string mode, Action<SetupSnapshot> accepted, CancellationToken token)
    {
        string user;
        lock (_sync)
        {
            RefreshContext();
            Validate(request, mode);
            user = _user ?? throw new SetupException("system_setup_access_denied", "Hãy đăng nhập để Setup.");
        }
        await _authorizer.AuthorizeAsync(user, request.ExpectedOrganizationId, token);
        lock (_sync)
        {
            RefreshContext();
            Validate(request, mode);
            token.ThrowIfCancellationRequested();
            _shutdown.Token.ThrowIfCancellationRequested();
            if (!_accessValid()) throw new SetupException("system_setup_access_denied", "Phiên hoặc license không còn hiệu lực.");
            if (_history.TryGetValue(request.OperationId, out var old))
            {
                if (old.Mode != mode || !SameRequest(old.Request, request))
                    throw new SetupException("system_setup_conflict", "Mã lượt Setup đã được dùng với lựa chọn khác.");
                accepted(Snapshot() with { Operation = old.Operation, Revision = old.Revision });
                return;
            }
            if (_active)
                throw new SetupException("system_setup_busy", "Một lượt Setup đang chạy hoặc đang dọn dẹp sau khi hủy.");
            if (_operation?.OperationId == request.OperationId)
                throw new SetupException("system_setup_conflict", "Lượt đã lưu từ phiên trước cần mã operation mới khi thử lại.");
            if (_history.Count >= 128)
                throw new SetupException("system_setup_limit", "Đã đạt giới hạn lượt Setup trong phiên. Hãy khởi động lại ứng dụng.");
            if (mode == "retry" && (_operation is null || _operation.OperationId != request.PreviousOperationId
                || _operation.State is "Accepted" or "Running"
                || request.ComponentIds.Any(id => !_operation.ComponentIds.Contains(id))))
                throw new SetupException("system_setup_retry_invalid", "Lượt trước không còn phù hợp để thử lại.");
            if (request.ResourceWarningAccepted && (mode != "retry" || !request.ComponentIds.Contains("qwen")
                || !_components.Any(c => c.Id == "qwen" && c.ErrorCode == "TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED"
                    && c.ResourceProfileId is not null && c.ResourceProfileId == request.ConfirmedResourceProfileId)))
                throw new SetupException("system_setup_confirmation_invalid", "Chưa có cảnh báo tài nguyên cần xác nhận cho lượt này.");
            var lease = _gate.Acquire(exclusive: true);
            var operation = new SetupOperation(request.OperationId, mode, "Accepted", request.ComponentIds.ToArray(), 1);
            try { _journal.Write(user, request.ExpectedOrganizationId, operation); }
            catch { lease.Dispose(); throw; }
            _operation = operation;
            _revision++;
            _history.Add(request.OperationId, (request, mode, operation, _revision));
            _cancellation?.Dispose();
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            var generation = _generation;
            var cancellation = _cancellation.Token;
            _active = true;
            try { accepted(Snapshot()); }
            catch { /* A disconnected UI can recover the operation through GET. */ }
            _running = Task.Run(() => RunAsync(request, mode, user, generation, lease, cancellation));
        }
    }

    private void Validate(SystemSetupRequest request, string mode)
    {
        if (mode is not ("check" or "start" or "retry") || request.OperationId == Guid.Empty
            || request.ComponentIds is not { Length: > 0 and <= 4 }
            || request.ComponentIds.Distinct(StringComparer.Ordinal).Count() != request.ComponentIds.Length
            || request.ComponentIds.Any(id => !_adapters.Any(a => a.Component.Id == id)))
            throw new SetupException("system_setup_invalid_request", "Lựa chọn thành phần Setup không hợp lệ.");
        if (request.ContextGeneration != _generation || request.ExpectedOrganizationId != _organization)
            throw new SetupException("system_setup_context_changed", "Tổ chức đã thay đổi. Hãy tải lại trạng thái Setup.");
        if (request.ComponentIds.Any(id => _components.Any(c => c.Id == id && c.State == "DISABLED")))
            throw new SetupException("system_setup_disabled", "Thành phần đã bị tắt trong cấu hình ứng dụng.");
    }

    private static bool SameRequest(SystemSetupRequest a, SystemSetupRequest b) =>
        a.ExpectedOrganizationId == b.ExpectedOrganizationId && a.ContextGeneration == b.ContextGeneration
        && a.PreviousOperationId == b.PreviousOperationId && a.ResourceWarningAccepted == b.ResourceWarningAccepted
        && a.ConfirmedResourceProfileId == b.ConfirmedResourceProfileId
        && a.ComponentIds.Order().SequenceEqual(b.ComponentIds.Order());

    public SetupSnapshot Cancel(Guid operationId)
    {
        lock (_sync)
        {
            RefreshContext();
            if (_operation?.OperationId != operationId)
                throw new SetupException("system_setup_context_changed", "Lượt Setup không thuộc context hiện tại.");
            _cancellation?.Cancel();
            return Snapshot();
        }
    }
    public void Invalidate() { lock (_sync) _cancellation?.Cancel(); }

    public async Task PrepareLegacyAsync(string componentId, bool confirmResources,
        Action<SetupOperation> progress, CancellationToken token)
    {
        var snapshot = GetSnapshot();
        var id = Guid.NewGuid();
        var retry = confirmResources && snapshot.Operation is { State: not ("Accepted" or "Running") }
            && snapshot.Components.Any(c => c.Id == componentId && c.ErrorCode == "TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED");
        void ChangedForOperation(string type, SetupSnapshot value)
        {
            if (value.Operation?.OperationId == id) progress(value.Operation);
        }
        Changed += ChangedForOperation;
        try
        {
            await StartAsync(new(id, snapshot.OrganizationId ?? Guid.Empty, snapshot.ContextGeneration, [componentId],
                retry ? snapshot.Operation?.OperationId : null, retry,
                retry ? snapshot.Components.Single(c => c.Id == componentId).ResourceProfileId : null),
                retry ? "retry" : "start", _ => { }, token);
            Task running;
            lock (_sync) running = _running;
            using var registration = token.Register(() => { lock (_sync) { if (_operation?.OperationId == id) _cancellation?.Cancel(); } });
            await running.ConfigureAwait(false);
            var result = GetSnapshot();
            token.ThrowIfCancellationRequested();
            var component = result.Components.Single(c => c.Id == componentId);
            if (result.Operation?.OperationId != id || component.State != "READY")
                throw new SetupException(component.ErrorCode ?? "system_setup_failed", component.Message);
        }
        finally { Changed -= ChangedForOperation; }
    }

    private async Task RunAsync(SystemSetupRequest request, string mode, string user, string generation,
        IDisposable lease, CancellationToken token)
    {
        var cancelled = false;
        var visited = new HashSet<string>();
        try
        {
            foreach (var adapter in _adapters.Where(a => request.ComponentIds.Contains(a.Component.Id)))
            {
                token.ThrowIfCancellationRequested();
                Update(generation, op => op with { State = "Running", CurrentComponent = adapter.Component.Id,
                    Stage = "VERIFY", Percent = null, BytesProcessed = null, TotalBytes = null });
                SetupComponent result;
                try
                {
                    result = await adapter.RunAsync(mode != "check", request.ResourceWarningAccepted,
                        (stage, percent, bytes, total) => Update(generation, op => op with {
                            Stage = stage, Percent = percent, BytesProcessed = bytes, TotalBytes = total }), token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    var code = SetupErrors.Code(exception);
                    result = adapter.Component with { State = code == "TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED"
                        ? "NEEDS_VERIFICATION" : "REPAIR_REQUIRED", ErrorCode = code,
                        Message = SetupErrors.Message(code), CheckedAtUtc = DateTime.UtcNow };
                }
                token.ThrowIfCancellationRequested();
                visited.Add(adapter.Component.Id);
                lock (_sync)
                {
                    if (generation == _generation)
                        _components = _components.Select(c => c.Id == result.Id ? result : c).ToArray();
                }
            }
        }
        catch (OperationCanceledException) { cancelled = true; }
        catch { cancelled = token.IsCancellationRequested; }
        finally
        {
            SetupSnapshot? terminal = null;
            lock (_sync)
            {
                var old = _history.GetValueOrDefault(request.OperationId).Operation;
                var all = !cancelled && visited.Count == request.ComponentIds.Length
                    && _components.Where(c => request.ComponentIds.Contains(c.Id)).All(c => c.State == "READY");
                var any = visited.Any(id => _components.Any(c => c.Id == id && c.State == "READY"));
                var final = (old ?? new SetupOperation(request.OperationId, mode, "Running", request.ComponentIds, 1)) with {
                    State = cancelled ? "Cancelled" : all ? "Completed" : any ? "PartiallyCompleted" : "Failed",
                    Sequence = (old?.Sequence ?? 1) + 1, CurrentComponent = null, Stage = null, Percent = null,
                    AllSelectedReady = all, AllRequiredReady = !cancelled && _components.All(c => c.State == "READY") };
                try { _journal.Write(user, request.ExpectedOrganizationId, final); }
                catch { final = final with { State = "Failed", AllSelectedReady = false, AllRequiredReady = false }; }
                if (generation == _generation)
                {
                    _operation = final;
                    _revision++;
                    _history[request.OperationId] = (request, mode, final, _revision);
                    terminal = Snapshot();
                }
                lease.Dispose();
                _active = false;
            }
            if (terminal is not null) Emit("system.setup.completed", terminal);
        }
    }
    private void Update(string generation, Func<SetupOperation, SetupOperation> update)
    {
        SetupSnapshot snapshot;
        lock (_sync)
        {
            RefreshContext();
            if (_generation != generation || _operation is not { State: "Accepted" or "Running" } current) return;
            _operation = update(current) with { Sequence = current.Sequence + 1 };
            _revision++;
            var item = _history[current.OperationId];
            _history[current.OperationId] = (item.Request, item.Mode, _operation, _revision);
            snapshot = Snapshot();
        }
        Emit("system.setup.progress", snapshot);
    }
    private void Emit(string type, SetupSnapshot snapshot)
    {
        try { Changed?.Invoke(type, snapshot); }
        catch (Exception) { /* UI may have disconnected; GET restores the persisted terminal snapshot. */ }
    }
    private async Task MonitorAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(_shutdown.Token))
                lock (_sync) { RefreshContext(); if (!_accessValid()) _cancellation?.Cancel(); }
        }
        catch (OperationCanceledException) { }
    }
    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        Invalidate();
        await _monitor.ConfigureAwait(false);
        await _running.ConfigureAwait(false);
        _cancellation?.Dispose();
        _shutdown.Dispose();
    }
}

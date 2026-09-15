using TOOL_SHARED.Contracts.Updates;

namespace TOOL_LOCAL.SystemSetup;

internal sealed record StartupSetupInstallResult(
    SetupSnapshot Snapshot,
    bool RestartScheduled);

internal sealed class StartupSystemSetupWorkflow : IDisposable
{
    private readonly object _sync = new();
    private readonly SystemSetupCoordinator _coordinator;
    private readonly Func<IProgress<DesktopUpdateProgress>, CancellationToken, Task>? _repairApplication;
    private SetupSnapshot _snapshot;
    private bool _disposed;

    public StartupSystemSetupWorkflow(
        SystemSetupCoordinator coordinator,
        Func<IProgress<DesktopUpdateProgress>, CancellationToken, Task>? repairApplication = null)
    {
        _coordinator = coordinator;
        _repairApplication = repairApplication;
        _snapshot = coordinator.GetSnapshot();
        coordinator.Changed += OnCoordinatorChanged;
    }

    public event Action<SetupSnapshot>? SnapshotChanged;

    public event Action<DesktopUpdateProgress>? RepairProgress;

    public SetupSnapshot Snapshot
    {
        get
        {
            lock (_sync) return _snapshot;
        }
    }

    public static SetupComponent[] RequiredComponents(SetupSnapshot snapshot) =>
        snapshot.Components
            .Where(component => component.State != "DISABLED")
            .ToArray();

    public static bool IsReady(SetupSnapshot snapshot) =>
        RequiredComponents(snapshot).All(component => component.State == "READY");

    public static bool NeedsApplicationRepair(SetupSnapshot snapshot) =>
        RequiredComponents(snapshot).Any(component =>
            component.State == "REPAIR_REQUIRED" &&
            !component.CanInstall &&
            component.CanRepair);

    public static bool NeedsResourceConfirmation(SetupSnapshot snapshot) =>
        RequiredComponents(snapshot).Any(component =>
            component.Id == "qwen" &&
            component.State != "READY" &&
            component.ErrorCode == "TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED");

    public async Task<SetupSnapshot> CheckAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var snapshot = _coordinator.GetSnapshot();
        Publish(snapshot);
        var componentIds = RequiredComponents(snapshot)
            .Where(component => component.State != "READY")
            .Select(component => component.Id)
            .ToArray();
        if (componentIds.Length == 0) return snapshot;
        return await ExecuteAsync("check", componentIds, false, null, null, cancellationToken);
    }

    public async Task<StartupSetupInstallResult> InstallAsync(
        bool resourceWarningAccepted,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var snapshot = Snapshot;
        if (IsReady(snapshot)) return new(snapshot, false);

        if (NeedsApplicationRepair(snapshot))
        {
            if (_repairApplication is null)
                throw new SetupException(
                    "system_setup_repair_unavailable",
                    "Không có package cùng phiên bản để sửa OCR/FFmpeg. Hãy cài lại bản VideoMaker đầy đủ.");

            var progress = new Progress<DesktopUpdateProgress>(update =>
            {
                try { RepairProgress?.Invoke(update); }
                catch { /* A closing startup dialog must not interrupt the verified repair package. */ }
            });
            await _repairApplication(progress, cancellationToken);
            return new(Snapshot, true);
        }

        var missing = RequiredComponents(snapshot)
            .Where(component => component.State != "READY")
            .ToArray();
        if (missing.Length == 0) return new(snapshot, false);

        var previous = snapshot.Operation;
        var canRetry = previous is { State: not ("Accepted" or "Running") }
            && missing.All(component => previous.ComponentIds.Contains(component.Id, StringComparer.Ordinal));
        var confirmationRequired = NeedsResourceConfirmation(snapshot);
        if (resourceWarningAccepted && (!confirmationRequired || !canRetry))
            throw new SetupException(
                "system_setup_confirmation_invalid",
                "Cảnh báo tài nguyên không còn khớp lượt kiểm tra hiện tại.");

        var profileId = resourceWarningAccepted
            ? missing.Single(component => component.Id == "qwen").ResourceProfileId
            : null;
        var result = await ExecuteAsync(
            canRetry ? "retry" : "start",
            missing.Select(component => component.Id).ToArray(),
            resourceWarningAccepted,
            canRetry ? previous!.OperationId : null,
            profileId,
            cancellationToken);
        return new(result, false);
    }

    public void Cancel()
    {
        var operation = Snapshot.Operation;
        if (operation is not { State: "Accepted" or "Running" }) return;
        try { Publish(_coordinator.Cancel(operation.OperationId)); }
        catch (SetupException) { }
    }

    private async Task<SetupSnapshot> ExecuteAsync(
        string mode,
        string[] componentIds,
        bool resourceWarningAccepted,
        Guid? previousOperationId,
        string? confirmedResourceProfileId,
        CancellationToken cancellationToken)
    {
        var baseline = _coordinator.GetSnapshot();
        var organizationId = baseline.OrganizationId
            ?? throw new SetupException("system_setup_context_changed", "Hãy chọn tổ chức trước khi kiểm tra hệ thống.");
        var operationId = Guid.NewGuid();
        var completed = new TaskCompletionSource<SetupSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);

        void AwaitTerminal(string type, SetupSnapshot snapshot)
        {
            if (type == "system.setup.completed" &&
                snapshot.Operation?.OperationId == operationId)
                completed.TrySetResult(snapshot);
        }

        _coordinator.Changed += AwaitTerminal;
        try
        {
            var request = new SystemSetupRequest(
                operationId,
                organizationId,
                baseline.ContextGeneration,
                componentIds,
                previousOperationId,
                resourceWarningAccepted,
                confirmedResourceProfileId);
            await _coordinator.StartAsync(request, mode, Publish, cancellationToken);
            using var registration = cancellationToken.Register(() =>
            {
                try { _coordinator.Cancel(operationId); }
                catch (SetupException) { }
            });
            return await completed.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _coordinator.Changed -= AwaitTerminal;
        }
    }

    private void OnCoordinatorChanged(string type, SetupSnapshot snapshot) => Publish(snapshot);

    private void Publish(SetupSnapshot snapshot)
    {
        lock (_sync) _snapshot = snapshot;
        try { SnapshotChanged?.Invoke(snapshot); }
        catch { /* The dialog can recover from the latest Snapshot property. */ }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(StartupSystemSetupWorkflow));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _coordinator.Changed -= OnCoordinatorChanged;
    }
}

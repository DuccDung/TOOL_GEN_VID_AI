using TOOL_LOCAL.Vietsub.Domain;

namespace TOOL_LOCAL.Vietsub.Storage;

internal sealed class VietsubProjectSession : IAsyncDisposable
{
    private readonly VietsubProjectStore _store;
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private readonly TimeSpan _autosaveDelay;
    private CancellationTokenSource? _autosaveCancellation;
    private FileStream? _workspaceLock;
    private bool _disposed;
    private bool _started;

    public VietsubProjectSession(
        VietsubProjectStore store,
        VietsubProjectManifest manifest,
        TimeSpan? autosaveDelay = null)
    {
        _store = store;
        Manifest = manifest;
        _autosaveDelay = autosaveDelay ?? TimeSpan.FromMilliseconds(750);
    }

    public VietsubProjectManifest Manifest { get; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_started)
        {
            return;
        }

        _workspaceLock = _store.AcquireExclusiveLock(Manifest.ProjectId);
        try
        {
            Manifest.LastCleanShutdown = false;
            Manifest.LastOpenedAtUtc = DateTime.UtcNow;
            await _store.SaveAsync(Manifest, cancellationToken);
            _started = true;
        }
        catch
        {
            _workspaceLock.Dispose();
            _workspaceLock = null;
            throw;
        }
    }

    public async Task UpdateAsync(
        Action<VietsubProjectManifest> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ThrowIfDisposed();
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            update(Manifest);
            ScheduleAutosave();
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_started)
        {
            return;
        }

        CancelPendingAutosave();
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            await _store.SaveAsync(Manifest, cancellationToken);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_started)
        {
            return;
        }

        CancelPendingAutosave();
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            Manifest.LastCleanShutdown = true;
            await _store.SaveAsync(Manifest, cancellationToken);
            _started = false;
            _workspaceLock?.Dispose();
            _workspaceLock = null;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private void ScheduleAutosave()
    {
        CancelPendingAutosave();
        var cancellation = new CancellationTokenSource();
        _autosaveCancellation = cancellation;
        _ = AutosaveAfterDelayAsync(cancellation);
    }

    private async Task AutosaveAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(_autosaveDelay, cancellation.Token);
            await _mutationLock.WaitAsync(cancellation.Token);
            try
            {
                await _store.SaveAsync(Manifest, cancellation.Token);
            }
            finally
            {
                _mutationLock.Release();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_autosaveCancellation, cancellation))
            {
                _autosaveCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void CancelPendingAutosave()
    {
        var cancellation = Interlocked.Exchange(ref _autosaveCancellation, null);
        cancellation?.Cancel();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        CancelPendingAutosave();
        await _mutationLock.WaitAsync();
        try
        {
            if (_started)
            {
                Manifest.LastCleanShutdown = true;
                await _store.SaveAsync(Manifest);
            }
            _disposed = true;
        }
        finally
        {
            _workspaceLock?.Dispose();
            _workspaceLock = null;
            _mutationLock.Release();
            _mutationLock.Dispose();
        }
    }
}

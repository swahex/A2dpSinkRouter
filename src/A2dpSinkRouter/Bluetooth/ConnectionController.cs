using System.Diagnostics;

namespace A2dpSinkRouter.Bluetooth;

internal interface IPlaybackSession : IDisposable
{
    event Action? StateChanged;
    string State { get; }
    // Lifecycle checks must preserve native errors; UI reads may use a safe fallback.
    string ReadState() => State;
    Task OpenAsync(CancellationToken cancellation);
}

// One owned phone connection. Retired sessions cannot change the new state.
internal sealed class ConnectionController(Func<string, IPlaybackSession> create) : IAsyncDisposable
{
    private sealed class Entry(string id, IPlaybackSession session)
    {
        public string Id { get; } = id;
        public IPlaybackSession Session { get; } = session;
        public Action? Handler;
        public bool Connecting = true;
    }
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _stateLock = new();
    private Entry? _current;
    private string? _connectingId;
    private long? _releasedAt;
    private bool _disposed;
    public event Action<string, string>? StateChanged;

    public string State(string id)
    {
        lock (_stateLock)
            return _connectingId == id ? "正在连接…" : _current is { } entry && entry.Id == id
                ? entry.Connecting ? "正在连接…" : entry.Session.State : "未连接";
    }

    public async Task ConnectAsync(string id, CancellationToken cancellation)
    {
        await _operations.WaitAsync(cancellation);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellation.ThrowIfCancellationRequested();
            CloseCurrent();
            lock (_stateLock) _connectingId = id;
            try
            {
                StateChanged?.Invoke(id, "正在连接…");
                for (int attempt = 0; ; attempt++)
                {
                    // Dispose releases our reference, not a Bluetooth transport completion
                    // barrier. Allow the old sink to settle before enabling another one.
                    if (_releasedAt is long released)
                    {
                        var remaining = TimeSpan.FromMilliseconds(1500) - Stopwatch.GetElapsedTime(released);
                        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellation);
                    }
                    cancellation.ThrowIfCancellationRequested();
                    var entry = new Entry(id, create(id));
                    entry.Handler = () =>
                    {
                        lock (_stateLock)
                            if (ReferenceEquals(_current, entry) && !entry.Connecting)
                                StateChanged?.Invoke(id, entry.Session.State);
                    };
                    lock (_stateLock) _current = entry;
                    try
                    {
                        entry.Session.StateChanged += entry.Handler;
                        await entry.Session.OpenAsync(cancellation);
                        // Open completion and the native State update can arrive separately.
                        long openedAt = Stopwatch.GetTimestamp();
                        while (entry.Session.ReadState() != "已连接")
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (Stopwatch.GetElapsedTime(openedAt) >= TimeSpan.FromSeconds(1))
                                throw new ClosedDuringOpenException();
                            await Task.Delay(100, cancellation);
                        }
                        cancellation.ThrowIfCancellationRequested();
                        lock (_stateLock)
                        {
                            // Recheck after entering the publication lock, and publish
                            // that snapshot rather than taking another fallible UI read.
                            string state = entry.Session.ReadState();
                            if (state != "已连接") throw new ClosedDuringOpenException();
                            entry.Connecting = false;
                            _connectingId = null;
                            StateChanged?.Invoke(id, state);
                        }
                        break;
                    }
                    catch (ClosedDuringOpenException) when (attempt < 2)
                    {
                        // Only retry the known success-then-closed race, never cancellation,
                        // access denial, timeout, or an arbitrary native exception.
                        CloseCurrent(notify: false);
                    }
                }
            }
            catch { CloseCurrent(notify: false); throw; }
            finally
            {
                bool failed;
                lock (_stateLock) { _connectingId = null; failed = _current is null; }
                if (failed) StateChanged?.Invoke(id, "未连接");
            }
        }
        finally { _operations.Release(); }
    }

    public async Task DisconnectAsync(string id)
    {
        await _operations.WaitAsync();
        try { if (_current?.Id == id) CloseCurrent(); }
        finally { _operations.Release(); }
    }

    private sealed class ClosedDuringOpenException() : IOException("蓝牙连接在打开完成前已关闭，请重新连接。");

    private void CloseCurrent(bool notify = true)
    {
        Entry? entry;
        lock (_stateLock) { entry = _current; _current = null; }
        if (entry is null) return;
        try
        {
            try { entry.Session.StateChanged -= entry.Handler; }
            finally { entry.Session.Dispose(); }
        }
        finally
        {
            _releasedAt = Stopwatch.GetTimestamp();
            if (notify) StateChanged?.Invoke(entry.Id, "未连接");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _operations.WaitAsync();
        try { _disposed = true; CloseCurrent(); }
        finally { _operations.Release(); }
    }
}

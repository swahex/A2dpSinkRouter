namespace A2dpSinkRouter.Bluetooth;

internal interface IPlaybackSession : IDisposable
{
    event Action? StateChanged;
    string State { get; }
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
    private bool _disposed;
    public event Action<string, string>? StateChanged;

    public string State(string id)
    {
        lock (_stateLock)
            return _current is { } entry && entry.Id == id
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
                StateChanged?.Invoke(id, "正在连接…");
                await entry.Session.OpenAsync(cancellation);
                cancellation.ThrowIfCancellationRequested();
                lock (_stateLock)
                {
                    entry.Connecting = false;
                    if (entry.Session.State != "已连接")
                        throw new IOException("蓝牙连接在打开完成前已关闭，请重新连接。");
                    StateChanged?.Invoke(id, entry.Session.State);
                }
            }
            catch { CloseCurrent(); throw; }
        }
        finally { _operations.Release(); }
    }

    public async Task DisconnectAsync(string id)
    {
        await _operations.WaitAsync();
        try { if (_current?.Id == id) CloseCurrent(); }
        finally { _operations.Release(); }
    }

    private void CloseCurrent()
    {
        Entry? entry;
        lock (_stateLock) { entry = _current; _current = null; }
        if (entry is null) return;
        try
        {
            try { entry.Session.StateChanged -= entry.Handler; }
            finally { entry.Session.Dispose(); }
        }
        finally { StateChanged?.Invoke(entry.Id, "未连接"); }
    }

    public async ValueTask DisposeAsync()
    {
        await _operations.WaitAsync();
        try { _disposed = true; CloseCurrent(); }
        finally { _operations.Release(); }
    }
}

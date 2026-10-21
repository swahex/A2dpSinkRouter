using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace A2dpSinkRouter.Bluetooth;

internal sealed record BluetoothSource(string Id, string Name, Guid? ContainerId)
{
    public override string ToString() => Name;
}

internal sealed class BluetoothService : IAsyncDisposable
{
    private sealed class ConnectionEntry(AudioPlaybackConnection connection)
    {
        public AudioPlaybackConnection Connection { get; } = connection;
        public Windows.Foundation.TypedEventHandler<AudioPlaybackConnection, object>? Handler;
    }
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly ConcurrentDictionary<string, ConnectionEntry> _connections = new();
    private bool _disposed;
    public event Action<string, string>? StateChanged;

    public async Task<List<BluetoothSource>> DiscoverAsync(CancellationToken cancellation)
    {
        var devices = await DeviceInformation.FindAllAsync(AudioPlaybackConnection.GetDeviceSelector(),
            new[] { "System.Devices.ContainerId" }).AsTask(cancellation);
        return devices.Select(d => new BluetoothSource(d.Id, d.Name,
            d.Properties.TryGetValue("System.Devices.ContainerId", out object? value) && Guid.TryParse(value?.ToString(), out var g) ? g : null))
            .OrderBy(d => d.Name).ToList();
    }

    public string State(string id)
    {
        try { return _connections.TryGetValue(id, out var entry) ? Describe(entry.Connection.State) : "未连接"; }
        catch (COMException) { return "连接已关闭"; }
        catch (ObjectDisposedException) { return "连接已关闭"; }
    }

    public async Task ConnectAsync(BluetoothSource source, CancellationToken cancellation)
    {
        await _operations.WaitAsync(cancellation);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Close(source.Id);
            var connection = AudioPlaybackConnection.TryCreateFromId(source.Id)
                ?? throw new InvalidOperationException("无法为此设备创建 A2DP 接收连接，请先在 Windows 中完成蓝牙配对。");
            var entry = new ConnectionEntry(connection);
            entry.Handler = (sender, _) =>
            {
                try
                {
                    if (_connections.TryGetValue(source.Id, out var current) && ReferenceEquals(current, entry))
                        StateChanged?.Invoke(source.Id, Describe(sender.State));
                }
                catch (COMException) { /* A concurrent disconnect can close the sender. */ }
                catch (ObjectDisposedException) { }
            };
            _connections[source.Id] = entry;
            connection.StateChanged += entry.Handler;
            StateChanged?.Invoke(source.Id, "正在连接…");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(TimeSpan.FromSeconds(25));
                await connection.StartAsync().AsTask(timeout.Token);
                var result = await connection.OpenAsync().AsTask(timeout.Token);
                if (result.Status != AudioPlaybackConnectionOpenResultStatus.Success)
                    throw new InvalidOperationException($"蓝牙连接失败：{result.Status}，{result.ExtendedError?.Message}");
                StateChanged?.Invoke(source.Id, Describe(connection.State));
            }
            catch { Close(source.Id); throw; }
        }
        finally { _operations.Release(); }
    }

    public async Task DisconnectAsync(string id)
    {
        await _operations.WaitAsync();
        try { Close(id); }
        finally { _operations.Release(); }
    }

    private void Close(string id)
    {
        if (!_connections.TryRemove(id, out var entry)) return;
        try { if (entry.Handler is not null) entry.Connection.StateChanged -= entry.Handler; }
        finally { entry.Connection.Dispose(); }
        StateChanged?.Invoke(id, "未连接");
    }

    private static string Describe(AudioPlaybackConnectionState state) => state switch
    {
        AudioPlaybackConnectionState.Opened => "已连接",
        AudioPlaybackConnectionState.Closed => "未连接",
        _ => "等待连接"
    };

    public async ValueTask DisposeAsync()
    {
        await _operations.WaitAsync();
        try
        {
            _disposed = true;
            foreach (var id in _connections.Keys) Close(id);
        }
        finally { _operations.Release(); }
    }
}

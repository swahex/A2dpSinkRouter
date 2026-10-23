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
    private readonly ConnectionController _controller = new(id => new PlaybackSession(id));
    public event Action<string, string>? StateChanged
    {
        add => _controller.StateChanged += value;
        remove => _controller.StateChanged -= value;
    }
    public async Task<List<BluetoothSource>> DiscoverAsync(CancellationToken cancellation)
    {
        var devices = await DeviceInformation.FindAllAsync(AudioPlaybackConnection.GetDeviceSelector(),
            new[] { "System.Devices.ContainerId" }).AsTask(cancellation);
        return devices.Select(d => new BluetoothSource(d.Id, d.Name,
            d.Properties.TryGetValue("System.Devices.ContainerId", out object? value) && Guid.TryParse(value?.ToString(), out var g) ? g : null))
            .OrderBy(d => d.Name).ToList();
    }
    public string State(string id) => _controller.State(id);
    public Task ConnectAsync(BluetoothSource source, CancellationToken cancellation) => _controller.ConnectAsync(source.Id, cancellation);
    public Task DisconnectAsync(string id) => _controller.DisconnectAsync(id);
    public ValueTask DisposeAsync() => _controller.DisposeAsync();

    private sealed class PlaybackSession : IPlaybackSession
    {
        private readonly AudioPlaybackConnection _connection;
        private readonly Windows.Foundation.TypedEventHandler<AudioPlaybackConnection, object> _handler;
        public event Action? StateChanged;
        public PlaybackSession(string id)
        {
            _connection = AudioPlaybackConnection.TryCreateFromId(id)
                ?? throw new InvalidOperationException("无法为此设备创建 A2DP 接收连接，请先在 Windows 中完成蓝牙配对。");
            _handler = (_, _) => StateChanged?.Invoke();
            try { _connection.StateChanged += _handler; }
            catch { _connection.Dispose(); throw; }
        }
        public string State
        {
            get
            {
                try
                {
                    return _connection.State switch
                    {
                        AudioPlaybackConnectionState.Opened => "已连接",
                        AudioPlaybackConnectionState.Closed => "未连接",
                        _ => "等待连接"
                    };
                }
                catch (COMException) { return "未连接"; }
                catch (ObjectDisposedException) { return "未连接"; }
            }
        }
        public async Task OpenAsync(CancellationToken cancellation)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            await _connection.StartAsync().AsTask(timeout.Token);
            var result = await _connection.OpenAsync().AsTask(timeout.Token);
            if (result.Status != AudioPlaybackConnectionOpenResultStatus.Success)
                throw new InvalidOperationException($"蓝牙连接失败：{result.Status}，{result.ExtendedError?.Message}");
        }
        public void Dispose()
        {
            try { _connection.StateChanged -= _handler; }
            finally { _connection.Dispose(); }
        }
    }
}

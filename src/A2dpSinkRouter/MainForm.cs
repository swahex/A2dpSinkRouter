using System.Diagnostics;
using A2dpSinkRouter.Audio;
using A2dpSinkRouter.Bluetooth;
using A2dpSinkRouter.Infrastructure;
using A2dpSinkRouter.UI;
using static A2dpSinkRouter.Infrastructure.UiText;

namespace A2dpSinkRouter;

internal sealed partial class MainForm : Form
{
    private readonly BluetoothService _bluetooth = new();
    private readonly AudioRouter _router = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Settings _settings;
    private readonly ComboBox _phones = MakeCombo();
    private readonly ComboBox _inputs = MakeCombo();
    private readonly ComboBox _outputs = MakeCombo();
    private readonly Button _connect = MakeButton("连接 / 重新连接");
    private readonly Button _disconnect = MakeButton("断开手机");
    private readonly Button _refresh = MakeButton("刷新设备");
    private readonly Button _start = MakeButton("开始路由");
    private readonly Button _stop = MakeButton("停止路由");
    private readonly CheckBox _allInputs = new() { Text = "显示其他输入设备", AutoSize = true };
    private readonly ToolStripMenuItem _reconnect = new() { CheckOnClick = true };
    private readonly CheckBox _mute = new() { Text = "静音", AutoSize = true };
    private readonly CheckBox _exclusive = new() { Text = "独占输出（运行时占用此设备）", AutoSize = true };
    private readonly NumericUpDown _buffer = new() { Minimum = 5, Maximum = 100, Value = 12, Width = 75 };
    private readonly VolumeSlider _volume = new();
    private readonly Label _volumeText = new() { AutoSize = true, Text = "100%", Padding = new(0, 6, 12, 0) };
    private readonly Label _connectionState = MakeLabel("手机未连接");
    private readonly Label _routeState = MakeLabel("路由已停止");
    private readonly Label _stats = MakeLabel("缓冲和设备周期是本地指标，不代表蓝牙端到端延迟。");
    private readonly LevelMeter _meter = new();
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private List<AudioEndpoint> _endpoints = [];
    private bool _binding, _busy, _closing, _allowClose;
    private string? _sourceId;
    private string? _lastRouteError;
    private Task _operation = Task.CompletedTask;
    private DateTime _lastTelemetry = DateTime.MinValue;
    private readonly RouteStartGate _routeStart = new();
    private Task<List<AudioEndpoint>>? _endpointScan;
    private Task _endpointMonitor = Task.CompletedTask;
    private DateTime _nextEndpointCheck = DateTime.MinValue;
    private string? _endpointMonitorError;
    private string? _waitingMessage;
    private readonly bool _preview;
    private bool _exitRequested;

    public MainForm(bool preview = false, string? previewLanguage = null)
    {
        _preview = preview;
        string? loadError = null;
        try { _settings = preview ? new Settings() : Settings.Load(); }
        catch (Exception ex) { _settings = new(); loadError = ex.Message; }
        UiText.Language = previewLanguage ?? _settings.Language;
        Theme.Dark = _settings.Appearance == "dark";
        Text = "A2DP Sink Router 0.3.5";
        DoubleBuffered = true;
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new(1120, 880); MinimumSize = new(720, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new("Segoe UI", 10);
        BackColor = Theme.Background; ForeColor = Theme.Ink;
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("App.Icon")!)
        using (var embeddedIcon = new Icon(iconStream)) Icon = (Icon)embeddedIcon.Clone();
        BuildLayout();
        _allInputs.Checked = _settings.ShowAllInputs;
        _reconnect.Checked = _settings.ReconnectOnStart;
        _buffer.Value = _settings.BufferMs;
        _volume.Value = _settings.Volume;
        _mute.Checked = _settings.Muted;
        _exclusive.Checked = _settings.ExclusiveOutput;
        _closeToTray.Checked = _settings.CloseToTray;
        UpdateVolume();
        AddMenu("打开控制面板", "Open control panel", (_, _) => ShowPanel());
        AddMenu("停止路由 / 取消等待", "Stop route / cancel waiting", (_, _) => BeginOperation(StopRouteAsync));
        AddMenu("蓝牙设置", "Bluetooth settings", (_, _) => OpenBluetoothSettings());
        _trayMenu.Items.Add(new ToolStripSeparator());
        AddMenu("退出", "Exit", (_, _) => RequestExit());
        _tray = new NotifyIcon { Icon = Icon, Text = "A2DP Sink Router", ContextMenuStrip = _trayMenu, Visible = !preview };
        _tray.DoubleClick += (_, _) => ShowPanel();
        _connect.Click += (_, _) => BeginOperation(ConnectAsync);
        _disconnect.Click += (_, _) => BeginOperation(async () =>
        {
            await StopRouteAsync();
            if (_phones.SelectedItem is BluetoothSource phone) await _bluetooth.DisconnectAsync(phone.Id);
            UpdateConnectionState();
        });
        _refresh.Click += (_, _) => BeginOperation(RefreshDevicesAsync);
        _start.Click += (_, _) => BeginOperation(StartRouteAsync);
        _stop.Click += (_, _) => BeginOperation(StopRouteAsync);
        _phones.SelectedIndexChanged += (_, _) => SourceChanged();
        _allInputs.CheckedChanged += (_, _) => { if (!_binding) BindInputs((_inputs.SelectedItem as AudioEndpoint)?.Id); };
        _volume.ValueChanged += (_, _) => UpdateVolume();
        _mute.CheckedChanged += (_, _) => UpdateVolume();
        _closeToTray.CheckedChanged += (_, _) => SaveSelections();
        _reconnect.CheckedChanged += (_, _) => SaveSelections();
        _bluetooth.StateChanged += (id, state) =>
        {
            if (_closing || !IsHandleCreated) return;
            try { BeginInvoke(() => { if (_closing) return; Log(F("蓝牙：{0}", "Bluetooth: {0}", Connection(state))); UpdateConnectionState(); _nextEndpointCheck = DateTime.MinValue; }); }
            catch (InvalidOperationException) { }
        };
        _timer.Tick += (_, _) =>
        {
            UpdateStatus();
            if (!_busy && !_closing && !_resizingWindow && _endpointMonitor.IsCompleted && DateTime.UtcNow >= _nextEndpointCheck)
            {
                _nextEndpointCheck = DateTime.UtcNow.AddSeconds(1);
                _endpointMonitor = MonitorEndpointsAsync();
            }
        };
        if (!preview) _timer.Start();
        Resize += (_, _) => { if (!preview && WindowState == FormWindowState.Minimized) HideToTray(); };
        FormClosing += OnClosing;
        Shown += (_, _) =>
        {
            if (preview) { PopulatePreview(); return; }
            var area = Screen.FromControl(this).WorkingArea;
            Size = new(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
            BeginOperation(async () =>
            {
                await RefreshDevicesAsync();
                if (_reconnect.Checked && _phones.SelectedItem is BluetoothSource) await ConnectAsync();
            });
        };
        ApplyLanguage();
        ApplyAppearance();
        Log(T("就绪。连接手机，选择输入和输出，点击开始路由。", "Ready. Connect your phone, choose input and output, then start the route."));
        if (loadError is not null) Log(F("配置读取失败，已使用默认值：{0}", "Could not load settings; using defaults: {0}", loadError));
        UpdateButtons();
    }

    private void BeginOperation(Func<Task> action)
    {
        if (_busy || _closing) return;
        _operation = ExecuteAsync(action);
    }
    private async Task ExecuteAsync(Func<Task> action)
    {
        _busy = true; UpdateButtons();
        try { await action(); }
        catch (OperationCanceledException) { if (!_closing) Log(T("操作已取消或连接超时。", "The operation was cancelled or the connection timed out.")); }
        catch (Exception ex)
        {
            Log(F("操作失败：{0} (0x{1:X8})", "Operation failed: {0} (0x{1:X8})", Engine(ex.Message), ex.HResult));
            if (!_closing) MessageBox.Show(this, Engine(ex.Message), T("操作失败", "Operation failed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { _busy = false; if (!_closing) { UpdateButtons(); UpdateStatus(); } }
    }

    private async Task RefreshDevicesAsync()
    {
        string? phoneId = (_phones.SelectedItem as BluetoothSource)?.Id ?? _settings.LastBluetoothId;
        string? inputId = (_inputs.SelectedItem as AudioEndpoint)?.Id ?? _settings.InputId;
        string? outputId = (_outputs.SelectedItem as AudioEndpoint)?.Id ?? _settings.OutputId;
        var endpointsTask = ScanEndpointsAsync();
        var phonesTask = _bluetooth.DiscoverAsync(_lifetime.Token);
        await Task.WhenAll(endpointsTask, phonesTask);
        if (_closing) return;
        _endpoints = endpointsTask.Result;
        _binding = true;
        try
        {
            _phones.Items.Clear(); _phones.Items.AddRange(phonesTask.Result.Cast<object>().ToArray());
            _phones.SelectedItem = _phones.Items.Cast<BluetoothSource>().FirstOrDefault(p => p.Id == phoneId);
            if (_phones.SelectedIndex < 0 && phoneId is null && _phones.Items.Count == 1) _phones.SelectedIndex = 0;
            _sourceId = (_phones.SelectedItem as BluetoothSource)?.Id;
            _outputs.Items.Clear(); _outputs.Items.AddRange(_endpoints.Where(e => e.Flow == 0 && e.IsActive).Cast<object>().ToArray());
            _outputs.SelectedItem = _outputs.Items.Cast<AudioEndpoint>().FirstOrDefault(e => e.Id == outputId);
        }
        finally { _binding = false; }
        if (_sourceId is not null && _settings.Routes.TryGetValue(_sourceId, out var saved))
        {
            inputId ??= saved.InputId;
            if (outputId is null) _outputs.SelectedItem = _outputs.Items.Cast<AudioEndpoint>().FirstOrDefault(e => e.Id == saved.OutputId);
        }
        BindInputs(inputId);
        UpdateConnectionState();
        Log(F("设备已更新：{0} 个手机音源，{1} 个输入，{2} 个可用输出。", "Devices updated: {0} phone sources, {1} inputs, {2} available outputs.", _phones.Items.Count, _inputs.Items.Count, _outputs.Items.Count));
    }

    private async Task<List<AudioEndpoint>> ScanEndpointsAsync()
    {
        // Coalesce overlapping manual refresh, monitoring, and start checks. Called
        // from the UI thread; only the read-only enumeration runs on a worker.
        var scan = _endpointScan ??= Task.Run(EndpointCatalog.Enumerate, _lifetime.Token);
        try { return await scan; }
        finally { if (ReferenceEquals(_endpointScan, scan)) _endpointScan = null; }
    }

    private void ApplyEndpointSnapshot(List<AudioEndpoint> endpoints)
    {
        if (_endpoints.SequenceEqual(endpoints)) return;
        string? inputId = _routeStart.Requested?.InputId ?? (_inputs.SelectedItem as AudioEndpoint)?.Id ?? SavedEndpointId(true);
        string? outputId = _routeStart.Requested?.OutputId ?? (_outputs.SelectedItem as AudioEndpoint)?.Id ?? SavedEndpointId(false);
        _endpoints = endpoints;
        _binding = true;
        try
        {
            _outputs.BeginUpdate(); _inputs.BeginUpdate();
            _outputs.Items.Clear();
            _outputs.Items.AddRange(endpoints.Where(e => e.Flow == 0 && e.IsActive).Cast<object>().ToArray());
            _outputs.SelectedItem = _outputs.Items.Cast<AudioEndpoint>().FirstOrDefault(e => e.Id == outputId);
            BindInputs(inputId);
        }
        finally { _outputs.EndUpdate(); _inputs.EndUpdate(); _binding = false; }
    }

    private string? SavedEndpointId(bool input)
    {
        if (_sourceId is not null)
            return _settings.Routes.TryGetValue(_sourceId, out var route) ? input ? route.InputId : route.OutputId : null;
        return input ? _settings.InputId : _settings.OutputId;
    }

    private async Task MonitorEndpointsAsync()
    {
        try
        {
            var endpoints = await ScanEndpointsAsync();
            if (_closing || _busy || _resizingWindow) return;
            ApplyEndpointSnapshot(endpoints);
            _endpointMonitorError = null;
            if (_routeStart.Requested is not null)
            {
                var readiness = GetRouteReadiness();
                SetWaitingMessage(readiness);
                if (readiness == RouteReadiness.Ready) BeginOperation(TryStartPendingRouteAsync);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closing && _endpointMonitorError != ex.Message)
            {
                _endpointMonitorError = ex.Message;
                Log(F("设备更新失败，将自动重试：{0}", "Could not update devices; retrying automatically: {0}", Engine(ex.Message)));
            }
        }
    }

    private RouteReadiness GetRouteReadiness() => _routeStart.Evaluate(id => _endpoints.Any(e => e.Id == id && e.IsActive));
    private void SetWaitingMessage(RouteReadiness readiness)
    {
        var input = _endpoints.FirstOrDefault(e => e.Id == _routeStart.Requested?.InputId);
        string? message = readiness switch
        {
            RouteReadiness.WaitingForInput when input?.State == 2 => T("所选输入已禁用；启用后会自动开始。", "The input is disabled. Routing will start when it is enabled."),
            RouteReadiness.WaitingForInput when input is not null && !input.IsA2dp => T("等待输入设备就绪，就绪后会自动开始。", "Waiting for the input device. Routing will start when it is ready."),
            RouteReadiness.WaitingForInput => T("等待手机播放音频，输入就绪后会自动开始。", "Play audio on your phone. Routing will start when the input is ready."),
            RouteReadiness.WaitingForOutput => T("等待所选输出设备，就绪后会自动开始。", "Waiting for the selected output. Routing will start when it is ready."),
            RouteReadiness.Ready => T("输入和输出已就绪，正在启动…", "Devices ready. Starting the route…"),
            _ => null
        };
        if (message != _waitingMessage && message is not null) Log(message);
        _waitingMessage = message;
    }

    private void BindInputs(string? id)
    {
        _inputs.Items.Clear();
        var selectedPhone = _phones.SelectedItem as BluetoothSource;
        var inputs = _endpoints.Where(e => e.Flow == 1 && (e.IsA2dp || (_allInputs.Checked && e.IsActive))).ToArray();
        _inputs.Items.AddRange(inputs.Cast<object>().ToArray());
        _inputs.SelectedItem = inputs.FirstOrDefault(e => e.Id == id);
        if (_inputs.SelectedIndex < 0 && id is null && selectedPhone?.ContainerId is Guid container && container != Guid.Empty &&
            container != new Guid("00000000-0000-0000-ffff-ffffffffffff"))
        {
            var matches = inputs.Where(e => e.IsA2dp && e.ContainerId == container).ToArray();
            if (matches.Length == 1) _inputs.SelectedItem = matches[0];
        }
    }

    private void SourceChanged()
    {
        if (_binding || _closing) return;
        SaveSelections();
        _sourceId = (_phones.SelectedItem as BluetoothSource)?.Id;
        SavedRoute? route = null;
        if (_sourceId is not null) _settings.Routes.TryGetValue(_sourceId, out route);
        BindInputs(route?.InputId);
        _outputs.SelectedItem = _outputs.Items.Cast<AudioEndpoint>().FirstOrDefault(e => e.Id == route?.OutputId);
        UpdateConnectionState();
    }

    private async Task ConnectAsync()
    {
        var phone = _phones.SelectedItem as BluetoothSource ?? throw new InvalidOperationException(T("请先选择已配对的手机。", "Select a paired phone first."));
        SaveSelections();
        await StopRouteAsync();
        Log(F("正在连接 {0}…", "Connecting to {0}…", phone.Name));
        await _bluetooth.ConnectAsync(phone, _lifetime.Token);
        await RefreshDevicesAsync();
        _nextEndpointCheck = DateTime.MinValue;
        Log(T("手机已连接。点击开始路由，再在手机播放音频即可。", "Phone connected. Start the route, then play audio on your phone."));
    }

    private async Task StartRouteAsync()
    {
        string inputId = (_inputs.SelectedItem as AudioEndpoint)?.Id ?? SavedEndpointId(true)
            ?? throw new InvalidOperationException(T("请选择手机的 A2DP 输入设备。", "Select your phone's A2DP input."));
        string outputId = (_outputs.SelectedItem as AudioEndpoint)?.Id ?? SavedEndpointId(false)
            ?? throw new InvalidOperationException(T("请选择目标输出设备。", "Select an output device."));
        SaveSelections();
        _lastRouteError = null;
        // Register intent before refreshing. Do not reject a cached UNPLUGGED state:
        // Some A2DP inputs remain inactive until the remote source starts streaming.
        _routeStart.Request(new(inputId, outputId, (int)_buffer.Value, _exclusive.Checked));
        try
        {
            ApplyEndpointSnapshot(await ScanEndpointsAsync());
            if (_closing) return;
            await TryStartPendingRouteAsync();
        }
        catch { _routeStart.Cancel(); _waitingMessage = null; throw; }
        _nextEndpointCheck = DateTime.MinValue;
    }

    private async Task TryStartPendingRouteAsync()
    {
        var request = _routeStart.Requested;
        if (request is null || _closing) return;
        var readiness = GetRouteReadiness();
        SetWaitingMessage(readiness);
        if (readiness != RouteReadiness.Ready) return;
        try { await _router.StartAsync(request); }
        catch (Exception ex) when (ex is AudioEndpointNotReadyException || ex.HResult == unchecked((int)0x88890004) ||
                                  ex.HResult == unchecked((int)0x88890026) || ex.HResult == unchecked((int)0x80070490))
        {
            // The endpoint may disappear between enumeration and activation. Preserve
            // the explicit request, keep Stop available, and retry after the next scan.
            SetWaitingMessage(RouteReadiness.WaitingForInput);
            return;
        }
        catch { _routeStart.Cancel(); _waitingMessage = null; throw; }
        if (!_routeStart.Complete(request)) { await _router.StopAsync(); return; }
        _waitingMessage = null;
        var input = _endpoints.First(e => e.Id == request.InputId);
        var output = _endpoints.First(e => e.Id == request.OutputId);
        Log(F("路由已启动：{0} → {1}", "Route started: {0} → {1}", input.Name, output.Name));
        Log(F("输入 ID：{0}；输出 ID：{1}；预缓冲 {2} ms；音量 {3}%；静音 {4}", "Input ID: {0}; output ID: {1}; prebuffer {2} ms; volume {3}%; muted {4}", input.Id, output.Id, _buffer.Value, _volume.Value, _mute.Checked));
        Log(Engine(_router.Status.Description.Replace('\n', ' ')));
        _lastTelemetry = DateTime.MinValue;
    }
    private async Task StopRouteAsync()
    {
        bool wasWaiting = _routeStart.Requested is not null;
        _routeStart.Cancel(); _waitingMessage = null;
        if (wasWaiting) Log(T("等待已取消，不会自动开始路由。", "Waiting cancelled. The route will not start automatically."));
        bool wasRunning = _router.Status.Running;
        if (wasRunning) Log(T("停止前统计：", "Final statistics: ") + DescribeTelemetry(_router.Status));
        await _router.StopAsync();
        if (wasRunning) Log(T("路由已停止，手机仍保持连接。", "Route stopped. Your phone remains connected."));
    }

    private void SaveSelections()
    {
        SavedRoute? previous = null;
        if (_sourceId is not null) _settings.Routes.TryGetValue(_sourceId, out previous);
        _settings.InputId = (_inputs.SelectedItem as AudioEndpoint)?.Id ?? (_sourceId is null ? _settings.InputId : previous?.InputId);
        _settings.OutputId = (_outputs.SelectedItem as AudioEndpoint)?.Id ?? (_sourceId is null ? _settings.OutputId : previous?.OutputId);
        _settings.LastBluetoothId = _sourceId;
        _settings.BufferMs = (int)_buffer.Value; _settings.Volume = _volume.Value;
        _settings.Muted = _mute.Checked; _settings.ReconnectOnStart = _reconnect.Checked;
        _settings.ShowAllInputs = _allInputs.Checked;
        _settings.ExclusiveOutput = _exclusive.Checked;
        _settings.Language = UiText.Language; _settings.CloseToTray = _closeToTray.Checked;
        if (_sourceId is not null) _settings.Routes[_sourceId] = new() { InputId = _settings.InputId, OutputId = _settings.OutputId };
        if (_preview) return;
        try { _settings.Save(); } catch (Exception ex) { Log(T("设置保存失败：", "Could not save settings: ") + Engine(ex.Message)); }
    }
    private void UpdateVolume()
    {
        _router.Gain = _volume.Value / 100f; _router.Muted = _mute.Checked;
        _volumeText.Text = _volume.Value + "%";
    }
    private void UpdateConnectionState()
    {
        string? state = _phones.SelectedItem is BluetoothSource p ? _preview ? "已连接" : _bluetooth.State(p.Id) : null;
        _connectionState.Text = state is null ? T("尚未选择手机", "No phone selected") : Connection(state);
        _connectionState.ForeColor = state == "已连接" ? Theme.Green : Theme.Muted;
    }
    private void UpdateStatus()
    {
        var state = _router.Status;
        _routeState.Text = _routeStart.Requested is not null ? _waitingMessage ?? T("正在检查设备…", "Checking devices…") : Engine(state.Description);
        _meter.Value = Math.Clamp((int)(state.Peak * 1000), 0, 1000);
        _stats.Text = DescribeFlow(state) + "\n\n" +
            F("队列 {0:0.0} ms    欠载 {1:N0}\n输入 {2:N0} 包    有声输出 {3:N0} 帧", "Queue {0:0.0} ms    Underruns {1:N0}\nInput {2:N0} packets    Signal out {3:N0} frames",
                state.QueueMs, state.Underruns, state.CapturePackets, state.OutputSignalFrames);
        _badge.Text = _busy ? T("处理中", "Working") : _routeStart.Requested is not null ? T("等待音频", "Waiting")
            : state.Running ? T("正在路由", "Live") : state.Error is not null ? T("需处理", "Attention") : T("就绪", "Ready");
        _badge.Accent = state.Error is not null && _routeStart.Requested is null ? Theme.Red : state.Running ? Theme.Green : _busy || _routeStart.Requested is not null ? Theme.Blue : Theme.Muted;
        _footerHint.Text = _routeStart.Requested is not null ? _waitingMessage : state.Running ? T("音频正在发送到所选设备。收起到托盘后仍会继续。", "Audio is going to your chosen device. It keeps playing in the tray.")
            : T("选择输入和输出，开始你的专属音频路径。", "Choose an input and output to start your audio route.");
        if (state.Running && (DateTime.UtcNow - _lastTelemetry).TotalSeconds >= 5)
        {
            _lastTelemetry = DateTime.UtcNow;
            Log(DescribeTelemetry(state));
        }
        if (_routeStart.Requested is null && state.Error is not null && state.Error != _lastRouteError)
        {
            _lastRouteError = state.Error; Log(T("音频引擎：", "Audio engine: ") + Engine(state.Error));
        }
        UpdateButtons();
    }
    private string DescribeFlow(RouteStatus state)
    {
        if (_routeStart.Requested is not null) return T("就绪后自动开始，可随时取消", "Starts when ready. You can cancel anytime.");
        if (!state.Running) return T("等待开始路由", "Ready to start routing");
        if (state.CapturePackets == 0) return T("等待手机音频", "Waiting for phone audio");
        if (state.InputSignalFrames == 0) return T("已收到输入，当前为静音", "Input received; currently silent");
        if (_mute.Checked || _volume.Value == 0) return T("此路由已静音", "This route is muted");
        if (state.OutputSignalFrames > 0) return T("音频已发送到目标输出", "Audio submitted to your output");
        return state.Primed ? T("等待输出音频", "Waiting to submit audio") : T("正在预缓冲", "Prebuffering audio");
    }
    private string DescribeTelemetry(RouteStatus state) => DescribeFlow(state) + F(
        "；采集包={0}，采集帧={1}，输入有声帧={2}，源读取帧={3}，输出帧={4}，输出有声帧={5}，输入峰值={6:0.000000}，输出峰值={7:0.000000}，预缓冲就绪={8}，队列={9:0.0}ms，欠载={10}，不连续={11}，丢帧={12}",
        "; packets={0}, captured={1}, input signal={2}, source reads={3}, rendered={4}, output signal={5}, input peak={6:0.000000}, output peak={7:0.000000}, primed={8}, queue={9:0.0}ms, underruns={10}, gaps={11}, dropped={12}",
        state.CapturePackets, state.CapturedFrames, state.InputSignalFrames, state.SourceFramesRead, state.RenderedFrames, state.OutputSignalFrames,
        state.Peak, state.OutputPeak, state.Primed, state.QueueMs, state.Underruns, state.Discontinuities, state.DroppedFrames);
    private void UpdateButtons()
    {
        bool running = _router.Status.Running;
        bool waiting = _routeStart.Requested is not null;
        _connect.Enabled = _disconnect.Enabled = _refresh.Enabled = !_busy;
        _start.Enabled = !_busy && !running && !waiting; _stop.Enabled = !_busy && (running || waiting);
        _stop.Text = waiting ? T("取消等待", "Cancel waiting") : T("停止路由", "Stop");
        _phones.Enabled = _inputs.Enabled = _outputs.Enabled = _allInputs.Enabled = _buffer.Enabled = !_busy && !running && !waiting;
        _exclusive.Enabled = !_busy && !running && !waiting;
        _toTrayItem.Enabled = !_closing; _exitItem.Enabled = !_closing;
    }
    private void Log(string message)
    {
        if (_log.TextLength > 24000) _log.Text = _log.Text[^12000..];
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        _log.AppendText(line);
        if (_preview) return;
        try
        {
            Directory.CreateDirectory(Settings.DirectoryPath);
            string path = Path.Combine(Settings.DirectoryPath, "router.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".old", true);
            File.AppendAllText(path, line);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private void ShowPanel() { ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; Activate(); }
    private static void OpenBluetoothSettings() => Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;
        if (!_exitRequested && !_closing && _closeToTray.Checked && e.CloseReason == CloseReason.UserClosing && !_preview)
        {
            e.Cancel = true; HideToTray(); return;
        }
        e.Cancel = true;
        if (_closing) return;
        _closing = true; Enabled = false; _timer.Stop(); _lifetime.Cancel();
        SaveSelections();
        try
        {
            await _operation;
            await _endpointMonitor;
            await StopRouteAsync();
            await _bluetooth.DisposeAsync();
        }
        catch (Exception ex) { Log(T("退出清理：", "Shutdown cleanup: ") + Engine(ex.Message)); }
        finally
        {
            _layoutTimer.Dispose(); _router.Dispose(); _tray.Dispose(); _timer.Dispose(); _lifetime.Dispose(); _tips.Dispose(); _mark.Image?.Dispose();
            _allowClose = true; Close();
        }
    }
}

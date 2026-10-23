using A2dpSinkRouter.Audio;
using A2dpSinkRouter.Bluetooth;
using A2dpSinkRouter.Infrastructure;
using A2dpSinkRouter.UI;
using static A2dpSinkRouter.Infrastructure.UiText;

namespace A2dpSinkRouter;

internal sealed partial class MainForm
{
    private readonly MenuStrip _topBar = new() { Dock = DockStyle.Fill, AutoSize = false, BackColor = Theme.Surface, ForeColor = Theme.Muted,
        Font = new("Segoe UI", 9), Padding = new(16, 2, 16, 2), Margin = Padding.Empty, Renderer = new FlatMenuRenderer() };
    private readonly ToolStripMenuItem _languageMenu = new();
    private readonly ToolStripMenuItem _windowMenu = new();
    private readonly ToolStripMenuItem _preferencesMenu = new();
    private readonly ToolStripMenuItem _appearanceMenu = new();
    private readonly ToolStripMenuItem _lightItem = new();
    private readonly ToolStripMenuItem _darkItem = new();
    private readonly ToolStripMenuItem _zhItem = new("简体中文");
    private readonly ToolStripMenuItem _enItem = new("English");
    private readonly ToolStripMenuItem _toTrayItem = new();
    private readonly ToolStripMenuItem _exitItem = new();
    private readonly ToolStripMenuItem _dataDirectoryItem = new();
    private readonly ToolStripMenuItem _closeToTray = new() { CheckOnClick = true };
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolTip _tips = new() { AutoPopDelay = 12000, InitialDelay = 400 };
    private readonly Panel _header = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = Padding.Empty };
    private readonly Panel _footer = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = Padding.Empty };
    private readonly Panel _body = new BufferedPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Panel _viewport = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background };
    private readonly Panel _logsPage = new() { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new(24) };
    private readonly AppButton _routeTab = new();
    private readonly AppButton _logsTab = new();
    private readonly StatusBadge _badge = new();
    private readonly Label _title = new() { Text = "A2DP Sink Router", AutoSize = true };
    private readonly Label _subtitle = new() { AutoSize = true, ForeColor = Theme.Muted };
    private readonly Label _footerHint = new() { AutoSize = false, ForeColor = Theme.Muted };
    private readonly PictureBox _mark = new SharpPictureBox();
    private readonly List<SurfaceCard> _cards = [];
    private TableLayoutPanel _shell = null!;
    private bool _layoutReady, _layingOut, _applyingLanguage;
    private readonly System.Windows.Forms.Timer _layoutTimer = new() { Interval = 40 };
    private bool _layoutDirty, _resizingWindow;
    private bool? _twoColumns;
    private int _lastContentWidth = -1;
    private int _layoutPasses;
    private bool _layingChrome;
    private int S(int value) => (int)Math.Round(value * DeviceDpi / 96f);

    private void BuildLayout()
    {
        _shell = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Padding = Padding.Empty };
        _shell.ColumnStyles.Add(new(SizeType.Percent, 100));
        _shell.RowStyles.Add(new(SizeType.Absolute, S(30)));
        _shell.RowStyles.Add(new(SizeType.Absolute, S(100)));
        _shell.RowStyles.Add(new(SizeType.Percent, 100));
        _shell.RowStyles.Add(new(SizeType.Absolute, S(78)));
        _shell.Controls.Add(_topBar, 0, 0); _shell.Controls.Add(_header, 0, 1); _shell.Controls.Add(_body, 0, 2); _shell.Controls.Add(_footer, 0, 3);
        Controls.Add(_shell);
        BuildTopBar();
        using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("App.Mark")!)
        using (var image = Image.FromStream(stream)) _mark.Image = new Bitmap(image);
        _title.Font = new("Segoe UI", 18, FontStyle.Bold);
        _subtitle.Font = new("Segoe UI", 9);
        Localize(_subtitle, "把手机音频，送到你选择的设备。", "Your phone audio. Your choice of output.");
        Localize(_routeTab, "音频路由", "Routing"); Localize(_logsTab, "诊断日志", "Activity log");
        _header.Controls.AddRange([_mark, _title, _subtitle, _routeTab, _logsTab]);
        _routeTab.Click += (_, _) => SetPage(false); _logsTab.Click += (_, _) => SetPage(true);
        _body.Controls.Add(_viewport); _body.Controls.Add(_logsPage);
        BuildCards(); BuildLogPage();
        _start.Tag = new LocalizedText("开始路由", "Start routing");
        _stop.Tag = new LocalizedText("停止路由", "Stop");
        ((AppButton)_start).Tone = ButtonTone.Primary;
        _start.AutoSize = _stop.AutoSize = false;
        _footerHint.Font = new("Segoe UI", 9);
        _footer.Controls.AddRange([_footerHint, _start, _stop]);
        _header.SizeChanged += (_, _) => LayoutChrome();
        _footer.SizeChanged += (_, _) => LayoutChrome();
        _header.Paint += (_, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1); };
        _footer.Paint += (_, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0); };
        _viewport.SizeChanged += (_, _) => ScheduleCardLayout();
        _layoutTimer.Tick += (_, _) => { _layoutTimer.Stop(); if (_layoutDirty) LayoutCards(); };
        ResizeBegin += (_, _) => _resizingWindow = true;
        ResizeEnd += (_, _) => { _resizingWindow = false; LayoutCards(); };
        _layoutReady = true;
        SetPage(false); LayoutChrome();
    }

    private void BuildTopBar()
    {
        MenuText(_languageMenu, "语言", "Language");
        MenuText(_windowMenu, "窗口", "Window");
        MenuText(_preferencesMenu, "偏好", "Preferences");
        MenuText(_appearanceMenu, "外观", "Appearance");
        MenuText(_lightItem, "亮色", "Light"); MenuText(_darkItem, "暗色", "Dark");
        MenuText(_toTrayItem, "收起到托盘", "Minimize to tray");
        MenuText(_exitItem, "退出程序", "Exit application");
        MenuText(_dataDirectoryItem, "打开数据目录", "Open data folder");
        MenuText(_closeToTray, "关闭窗口时收起到托盘", "Close window to the system tray");
        MenuText(_reconnect, "启动时连接上次的手机", "Connect last phone on launch");
        _languageMenu.DropDownItems.AddRange([_zhItem, _enItem]);
        _windowMenu.DropDownItems.AddRange([_toTrayItem, new ToolStripSeparator(), _exitItem]);
        _appearanceMenu.DropDownItems.AddRange([_lightItem, _darkItem]);
        _preferencesMenu.DropDownItems.AddRange([_appearanceMenu, new ToolStripSeparator(), _closeToTray, _reconnect, new ToolStripSeparator(), _dataDirectoryItem]);
        _dataDirectoryItem.Click += (_, _) =>
        {
            if (_preview) return;
            Directory.CreateDirectory(Settings.DirectoryPath);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Settings.DirectoryPath) { UseShellExecute = true });
        };
        _lightItem.Click += (_, _) => ChangeAppearance(false);
        _darkItem.Click += (_, _) => ChangeAppearance(true);
        _zhItem.Click += (_, _) => ChangeLanguage("zh-CN");
        _enItem.Click += (_, _) => ChangeLanguage("en-US");
        _toTrayItem.Click += (_, _) => HideToTray(); _exitItem.Click += (_, _) => RequestExit();
        _badge.Size = new(S(100), S(24));
        var host = new ToolStripControlHost(_badge) { Alignment = ToolStripItemAlignment.Right, AutoSize = false, Size = _badge.Size, Margin = Padding.Empty };
        _topBar.Items.AddRange([_languageMenu, _windowMenu, _preferencesMenu, host]);
        MainMenuStrip = _topBar;
    }
    private static void MenuText(ToolStripItem item, string zh, string en) { item.Tag = new LocalizedText(zh, en); item.Text = T(zh, en); }
    private void ChangeLanguage(string language)
    {
        if (_applyingLanguage || UiText.Language == language) return;
        UiText.Language = language; _settings.Language = language;
        _waitingMessage = null;
        if (_routeStart.Requested is not null) SetWaitingMessage(GetRouteReadiness());
        ApplyLanguage(); SaveSelections();
    }

    private void BuildCards()
    {
        var phone = CreateCard("01  蓝牙音源", "01  Bluetooth source", "在 Windows 配对手机后，在这里连接。", "Pair your phone in Windows, then connect it here.");
        AddField(phone, "手机", "Phone", _phones);
        Localize(_connect, "连接 / 重连", "Connect / reconnect"); Localize(_disconnect, "断开", "Disconnect");
        Add(phone, MakeFlow(_connect, _disconnect));
        _connectionState.ForeColor = Theme.Muted; _connectionState.Margin = new(0, 4, 0, 0);
        Add(phone, _connectionState);
        var route = CreateCard("02  音频路径", "02  Audio path", "指定输入和输出，声音只走这条路径。", "Choose an input and output for this route.");
        AddField(route, "手机输入", "Phone input", _inputs);
        Localize(_allInputs, "显示其他输入设备", "Show other audio inputs"); Add(route, _allInputs);
        AddField(route, "播放到", "Play through", _outputs);
        Add(route, Caption("设备状态会自动更新，无需手动刷新。", "Device availability updates automatically."));
        var options = CreateCard("音量与延迟", "Volume & latency", null, null);
        var volumeRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Height = 34, Margin = new(0, 0, 0, 4) };
        volumeRow.ColumnStyles.Add(new(SizeType.Percent, 100)); volumeRow.ColumnStyles.Add(new(SizeType.Absolute, 52));
        _volumeText.Dock = DockStyle.Fill; _volumeText.TextAlign = ContentAlignment.MiddleRight; _volumeText.Padding = Padding.Empty;
        volumeRow.Controls.Add(_volume, 0, 0); volumeRow.Controls.Add(_volumeText, 1, 0);
        Add(options, volumeRow); Localize(_mute, "静音此路由", "Mute this route"); Add(options, _mute);
        _buffer.BorderStyle = BorderStyle.FixedSingle; _buffer.Margin = new(0, 3, 8, 4);
        Add(options, MakeFlow(Caption("预缓冲", "Prebuffer"), _buffer, MakeLabel("ms")));
        Localize(_exclusive, "使用独占输出", "Use exclusive output"); Add(options, _exclusive);
        var status = CreateCard("实时状态", "Live status", null, null);
        Add(status, Caption("输入电平", "Input level"));
        _meter.Margin = new(0, 2, 0, 14); Add(status, _meter);
        _stats.AutoSize = false; _stats.Dock = DockStyle.Top; _stats.Height = 78;
        _stats.Font = new("Segoe UI", 9); _stats.Padding = Padding.Empty; _stats.ForeColor = Theme.Ink;
        Add(status, _stats);
        var details = MakeButton(T("查看引擎详情", "Engine details"), (_, _) => SetPage(true));
        Localize(details, "查看引擎详情", "Engine details"); ((AppButton)details).Tone = ButtonTone.Quiet;
        Add(status, MakeFlow(details));
        _routeState.AutoSize = false; _routeState.Dock = DockStyle.Top; _routeState.Height = 64;
        _routeState.Font = new("Segoe UI", 8.5f); _routeState.ForeColor = Theme.Muted; _routeState.Padding = Padding.Empty;
        _cards.AddRange([phone, route, options, status]);
        _viewport.Controls.AddRange(_cards.Cast<Control>().ToArray());
    }

    private void BuildLogPage()
    {
        var frame = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = Theme.Surface, Padding = new(18) };
        frame.RowStyles.Add(new(SizeType.AutoSize)); frame.RowStyles.Add(new(SizeType.AutoSize)); frame.RowStyles.Add(new(SizeType.Percent, 100));
        var controls = MakeFlow(Caption("运行记录与诊断信息", "Activity & diagnostic details"), _refresh,
            MakeButton(T("蓝牙设置", "Bluetooth settings"), (_, _) => OpenBluetoothSettings()),
            MakeButton(T("清空显示", "Clear view"), (_, _) => _log.Clear()));
        ((Button)controls.Controls[2]).Tag = new LocalizedText("蓝牙设置", "Bluetooth settings");
        ((Button)controls.Controls[3]).Tag = new LocalizedText("清空显示", "Clear view");
        Localize(_refresh, "刷新设备", "Refresh devices");
        _log.Font = new("Consolas", 9.5f); _log.BackColor = Theme.Surface; _log.ForeColor = Theme.Muted;
        _log.WordWrap = false; _log.ScrollBars = ScrollBars.Both; _log.Margin = new(0, 12, 0, 0);
        frame.Controls.Add(controls, 0, 0); frame.Controls.Add(_routeState, 0, 1); frame.Controls.Add(_log, 0, 2); _logsPage.Controls.Add(frame);
    }

    private void SetPage(bool logs)
    {
        _logsPage.Visible = logs; _viewport.Visible = !logs;
        _routeTab.Tone = logs ? ButtonTone.Quiet : ButtonTone.Primary;
        _logsTab.Tone = logs ? ButtonTone.Primary : ButtonTone.Quiet;
        if (!logs) LayoutCards();
    }
    private void LayoutChrome()
    {
        if (!_layoutReady || _layingChrome) return;
        _layingChrome = true;
        try
        {
        int titleTop = S(12);
        _mark.SetBounds(S(24), titleTop, S(40), S(40));
        _title.Location = new(S(76), titleTop);
        // Use the font's actual preferred height, including CJK font fallback.
        int subtitleTop = titleTop + _title.PreferredSize.Height + S(4);
        _subtitle.Location = new(S(76), subtitleTop);
        int navTop = subtitleTop + _subtitle.PreferredSize.Height + S(12);
        _routeTab.SetBounds(S(24), navTop, S(102), S(32)); _routeTab.AutoSize = false;
        _logsTab.SetBounds(S(134), navTop, S(114), S(32)); _logsTab.AutoSize = false;
        _shell.RowStyles[1].Height = navTop + S(44);
        int fw = _footer.ClientSize.Width;
        _start.SetBounds(fw - S(312), S(18), S(158), S(40));
        _stop.SetBounds(fw - S(144), S(18), S(120), S(40));
        _footerHint.SetBounds(S(24), S(17), Math.Max(S(180), fw - S(366)), S(46));
        }
        finally { _layingChrome = false; }
    }
    private void ScheduleCardLayout()
    {
        if (!_layoutReady || _layingOut || !_viewport.Visible) return;
        _layoutDirty = true;
        if (!_layoutTimer.Enabled) _layoutTimer.Start();
    }
    private void InvalidateCardMeasurements()
    {
        _lastContentWidth = -1;
        foreach (var card in _cards) card.InvalidateMeasure();
    }
    private void LayoutCards()
    {
        if (!_layoutReady || _layingOut || _cards.Count == 0) return;
        _layoutTimer.Stop(); _layoutDirty = false;
        int pad = S(24), gap = S(16);
        int scrollbar = SystemInformation.VerticalScrollBarWidth;
        // Reserve a stable scrollbar gutter. ClientSize itself shrinks when a vertical
        // scrollbar appears; recover that width so visibility cannot flip the layout.
        int fullWidth = _viewport.ClientSize.Width + (_viewport.VerticalScroll.Visible ? scrollbar : 0);
        int width = Math.Max(S(280), fullWidth - pad * 2 - scrollbar);
        if (_lastContentWidth == width) return;
        _layingOut = true;
        _layoutPasses++;
        _viewport.SuspendLayout();
        try
        {
            bool columns = _twoColumns is null ? width >= S(850) : _twoColumns.Value ? width >= S(830) : width >= S(870);
            _twoColumns = columns;
            int cardWidth = columns ? (width - gap) / 2 : width;
            int[] heights = _cards.Select(c => c.MeasureHeight(cardWidth)).ToArray();
            Point scroll = _viewport.AutoScrollPosition;
            void Place(int index, int x, int y) => _cards[index].SetBounds(x + scroll.X, y + scroll.Y, cardWidth, heights[index]);
            int total;
            if (columns)
            {
                int firstRow = Math.Max(heights[0], heights[1]), secondRow = Math.Max(heights[2], heights[3]);
                heights[0] = heights[1] = firstRow; heights[2] = heights[3] = secondRow;
                Place(0, pad, pad); Place(1, pad + cardWidth + gap, pad);
                Place(2, pad, pad + firstRow + gap); Place(3, pad + cardWidth + gap, pad + firstRow + gap);
                total = pad * 2 + gap + firstRow + secondRow;
            }
            else
            {
                int y = pad;
                for (int i = 0; i < _cards.Count; i++) { Place(i, pad, y); y += heights[i] + gap; }
                total = y - gap + pad;
            }
            _viewport.AutoScrollMinSize = new(0, total);
            _lastContentWidth = width;
        }
        finally { _viewport.ResumeLayout(true); _layingOut = false; }
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (!_layoutReady) return;
        _shell.RowStyles[0].Height = S(30); _shell.RowStyles[1].Height = S(100); _shell.RowStyles[3].Height = S(78);
        InvalidateCardMeasurements();
        LayoutChrome(); LayoutCards();
    }

    private static ComboBox MakeCombo()
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat,
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 27, IntegralHeight = false, DropDownHeight = 280,
            BackColor = Theme.Surface, ForeColor = Theme.Ink, Margin = new(0, 2, 0, 10) };
        combo.DrawItem += (_, e) =>
        {
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? Theme.Hover : Theme.Surface);
            e.Graphics.FillRectangle(background, e.Bounds);
            string text = e.Index >= 0 ? DisplayItem(combo.Items[e.Index]) : "";
            var bounds = e.Bounds; bounds.Inflate(-8, 0);
            TextRenderer.DrawText(e.Graphics, text, combo.Font, bounds, combo.Enabled ? Theme.Ink : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        combo.DropDown += (_, _) => combo.DropDownWidth = Math.Min(1000, Math.Max(combo.Width,
            combo.Items.Cast<object>().Select(o => TextRenderer.MeasureText(DisplayItem(o), combo.Font).Width + 40).DefaultIfEmpty(combo.Width).Max()));
        return combo;
    }
    private static string DisplayItem(object? item) => item is AudioEndpoint ep
        ? ep.Name + (ep.State == 2 ? T("  [已禁用]", "  [Disabled]") : ep.Flow == 1
            ? ep.IsActive ? T("  [就绪]", "  [Ready]") : T("  [等待音频]", "  [Waiting]") : "") : item?.ToString() ?? "";
    private static Button MakeButton(string text, EventHandler? click = null)
    {
        var button = new AppButton { Text = text };
        if (click is not null) button.Click += click;
        return button;
    }
    private static Label MakeLabel(string text) => new() { Text = text, AutoSize = true, UseMnemonic = false, Dock = DockStyle.Top, Padding = new(0, 3, 0, 3), Margin = new(0, 0, 0, 5) };
    private static Label Caption(string zh, string en)
    {
        var label = MakeLabel(T(zh, en)); label.ForeColor = Theme.Muted; label.Font = new("Segoe UI", 9);
        Localize(label, zh, en); return label;
    }
    private static void Localize(Control control, string zh, string en) { control.Tag = new LocalizedText(zh, en); control.Text = T(zh, en); }
    private static FlowLayoutPanel MakeFlow(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new(0, 3, 0, 7) };
        flow.Controls.AddRange(controls); return flow;
    }
    private static SurfaceCard CreateCard(string zh, string en, string? descriptionZh, string? descriptionEn)
    {
        var card = new SurfaceCard();
        var title = MakeLabel(T(zh, en)); title.Font = new("Segoe UI", 12, FontStyle.Bold); Localize(title, zh, en);
        title.Margin = new(0, 0, 0, 10); Add(card, title);
        if (descriptionZh is not null) Add(card, Caption(descriptionZh, descriptionEn!));
        return card;
    }
    private static void Add(SurfaceCard card, Control control)
    {
        var content = card.Content; int row = content.RowCount++;
        content.RowStyles.Add(new(SizeType.AutoSize)); content.Controls.Add(control, 0, row);
        control.Anchor |= AnchorStyles.Left | AnchorStyles.Right;
        if (control is CheckBox) control.Margin = new(0, 4, 0, 7);
    }
    private static void AddField(SurfaceCard card, string zh, string en, Control control) { Add(card, Caption(zh, en)); Add(card, control); }

    private void ApplyLanguage()
    {
        _applyingLanguage = true;
        try
        {
            void Walk(Control parent)
            {
                foreach (Control control in parent.Controls)
                {
                    if (control.Tag is LocalizedText text) { control.Text = text.ToString(); control.AccessibleName = control.Text; }
                    Walk(control);
                }
            }
            Walk(this);
            void WalkMenu(ToolStripItemCollection items)
            {
                foreach (ToolStripItem item in items)
                {
                    if (item.Tag is LocalizedText text) item.Text = text.ToString();
                    if (item is ToolStripDropDownItem menu) WalkMenu(menu.DropDownItems);
                }
            }
            WalkMenu(_topBar.Items); WalkMenu(_trayMenu.Items);
            _zhItem.Checked = !English; _enItem.Checked = English;
            _tips.SetToolTip(_buffer, T("更小的预缓冲可减少等待，更大的预缓冲可容忍更多抖动。", "A smaller prebuffer reduces waiting; a larger one tolerates more jitter."));
            _tips.SetToolTip(_exclusive, T("独占期间，其他应用可能无法使用所选输出设备。", "Other applications may not be able to use this output while it is exclusive."));
            _toTrayItem.ToolTipText = T("音频继续运行，双击托盘图标可恢复窗口。", "Keep audio running. Double-click the tray icon to restore the window.");
            _closeToTray.ToolTipText = T("启用时，关闭窗口不影响音频。使用“退出”可结束程序。", "Closing the window keeps audio running. Use Exit to shut down the app.");
            _phones.AccessibleName = T("手机", "Phone"); _inputs.AccessibleName = T("手机输入", "Phone input");
            _outputs.AccessibleName = T("播放到", "Play through"); _volume.AccessibleName = T("路由音量", "Route volume");
            _buffer.AccessibleName = T("预缓冲毫秒数", "Prebuffer in milliseconds"); _meter.AccessibleName = T("输入电平", "Input level");
            _phones.Invalidate(); _inputs.Invalidate(); _outputs.Invalidate();
            InvalidateCardMeasurements();
            UpdateConnectionState(); UpdateStatus(); LayoutChrome(); LayoutCards();
        }
        finally { _applyingLanguage = false; }
    }
    private void AddMenu(string zh, string en, EventHandler click) => _trayMenu.Items.Add(new ToolStripMenuItem(T(zh, en), null, click) { Tag = new LocalizedText(zh, en) });
    private void HideToTray() { if (_preview) return; Hide(); ShowInTaskbar = false; }
    private void RequestExit() { _exitRequested = true; Close(); }

    private void PopulatePreview()
    {
        _binding = true;
        _phones.Items.Clear(); _inputs.Items.Clear(); _outputs.Items.Clear();
        var container = Guid.Empty;
        _phones.Items.Add(new BluetoothSource("preview-phone", T("示例手机", "Example Phone"), container)); _phones.SelectedIndex = 0;
        _endpoints = [new("preview-input", T("示例 A2DP 输入", "Example A2DP Input"), 1, 1, container), new("preview-output", T("示例输出设备", "Example Output"), 0, 1, null)];
        _inputs.Items.Add(_endpoints[0]); _inputs.SelectedIndex = 0;
        _outputs.Items.Add(_endpoints[1]); _outputs.SelectedIndex = 0;
        _binding = false;
        _connectionState.Text = T("已连接 · 示例手机", "Connected · Example Phone"); _connectionState.ForeColor = Theme.Green;
        _badge.Text = T("界面预览", "UI preview"); _badge.Accent = Theme.Blue;
        _stats.Text = T("等待开始路由\n选择目标输出后，点击下方“开始路由”。\n设备状态自动更新。", "Ready to route\nChoose your output, then select Start routing.\nDevice availability updates automatically.");
        _routeState.Text = T("共享输出 · 独立音频路径\n界面预览不会连接、采集或播放音频。", "Shared output · Dedicated audio path\nThis preview does not connect, capture, or play audio.");
        _log.Text = T("[预览] 界面已加载\r\n[预览] 输入与输出设备状态已更新\r\n[预览] 选择输出，然后开始路由。", "[Preview] Interface loaded\r\n[Preview] Input and output availability updated\r\n[Preview] Choose your output, then start routing.");
        LayoutCards();
    }

    internal void RenderPreview(string path, bool logs, bool bottom)
    {
        SetPage(logs); PerformLayout(); LayoutChrome(); LayoutCards();
        if (bottom && !logs) _viewport.ScrollControlIntoView(_cards[^1]);
        Application.DoEvents();
        using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        if (_footer.Bottom > ClientSize.Height || _start.Right > _footer.Width) throw new InvalidOperationException("Footer controls are outside the window.");
        if (_preferencesMenu.Bounds.Right > _topBar.Items[^1].Bounds.Left)
            throw new InvalidOperationException("Top bar controls overlap.");
        if (_subtitle.Top < _title.Bottom || _subtitle.Bottom > _routeTab.Top)
            throw new InvalidOperationException("Header text overlaps adjacent controls.");
        if (logs && _log.ClientSize.Height < 40) throw new InvalidOperationException("The log viewport is not accessible.");
        if (bottom && !logs && _cards[^1].Bottom > _viewport.ClientSize.Height + 2)
            throw new InvalidOperationException("The final card cannot be scrolled into view.");
        File.WriteAllText(path + ".layout.json", System.Text.Json.JsonSerializer.Serialize(new
        {
            Language = UiText.Language, Appearance = Theme.Dark ? "dark" : "light", Window = new { Width, Height }, Dpi = DeviceDpi,
            FooterVisible = _footer.Bottom <= ClientSize.Height,
            VerticalScroll = _viewport.VerticalScroll.Visible,
            Columns = _cards[0].Top == _cards[1].Top ? 2 : 1,
            StartText = _start.Text, TrayText = _toTrayItem.Text, ExitText = _exitItem.Text,
            Cards = _cards.Select(c => new { c.Left, c.Top, c.Width, c.Height })
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    internal void CheckPreviewResize(string path)
    {
        Size original = Size;
        var samples = new List<object>();
        foreach (int width in new[] { 720, 760, 900, 940, 980, 1120, 1400, 980, 940, 900, 760, 1120 })
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Size = new(width, 700); Application.DoEvents(); LayoutCards();
            watch.Stop();
            if (_twoColumns == true && (_cards[0].Bottom != _cards[1].Bottom || _cards[2].Top != _cards[3].Top || _cards[2].Bottom != _cards[3].Bottom))
                throw new InvalidOperationException("Card rows are not aligned.");
            if (_viewport.HorizontalScroll.Visible) throw new InvalidOperationException($"Unexpected horizontal scrolling at {width}: viewport {_viewport.ClientSize}, cards {string.Join(';', _cards.Select(c => c.Bounds))}.");
            if (_cards.Any(c => c.Content.Bottom > c.Height - c.Padding.Bottom)) throw new InvalidOperationException("Card content is clipped.");
            samples.Add(new { Width = width, Columns = _twoColumns == true ? 2 : 1, Milliseconds = watch.Elapsed.TotalMilliseconds });
        }
        int before = _cards.Sum(c => c.MeasureCount);
        for (int i = 0; i < 20; i++) { Height = 680 + i * 5; Application.DoEvents(); LayoutCards(); }
        int heightOnlyMeasurements = _cards.Sum(c => c.MeasureCount) - before;
        if (heightOnlyMeasurements != 0) throw new InvalidOperationException("Height-only resizing remeasured cards.");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { Samples = samples, HeightOnlyMeasurements = heightOnlyMeasurements, LayoutPasses = _layoutPasses }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Size = original; Application.DoEvents(); LayoutCards();
    }
    internal void SwitchPreviewLanguage(string language)
    {
        ChangeLanguage(language);
        PopulatePreview();
    }
    internal void DisposePreview()
    {
        _layoutTimer.Dispose(); _tray.Dispose(); _timer.Dispose(); _tips.Dispose(); _router.Dispose(); _lifetime.Dispose(); _mark.Image?.Dispose();
        _allowClose = true; Close();
    }
}

namespace A2dpSinkRouter.Audio;

internal sealed record RouteStatus(bool Running, string Description, string? Error, double QueueMs,
    long CapturedFrames, long RenderedFrames, long Underruns, long DroppedFrames, long Discontinuities, float Peak, double DriftPpm,
    long CapturePackets, long InputSignalFrames, long SourceFramesRead, long OutputSignalFrames, float OutputPeak, bool Primed);

internal sealed class AudioRouter : IDisposable
{
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _thread;
    private volatile bool _running;
    private string _description = "路由已停止";
    private string? _error;
    private float _gain = 1;
    private bool _muted;
    private double _queueMs, _driftPpm;
    private float _peak;
    private long _captured, _rendered, _underruns, _dropped, _discontinuities;
    private long _capturePackets, _inputSignalFrames, _sourceFramesRead, _outputSignalFrames;
    private float _outputPeak;
    private bool _primed;
    public float Gain { set => Volatile.Write(ref _gain, Math.Clamp(value, 0, 1)); }
    public bool Muted { set => Volatile.Write(ref _muted, value); }
    public RouteStatus Status => new(_running, _description, _error, Volatile.Read(ref _queueMs),
        Interlocked.Read(ref _captured), Interlocked.Read(ref _rendered), Interlocked.Read(ref _underruns),
        Interlocked.Read(ref _dropped), Interlocked.Read(ref _discontinuities), Volatile.Read(ref _peak), Volatile.Read(ref _driftPpm),
        Interlocked.Read(ref _capturePackets), Interlocked.Read(ref _inputSignalFrames), Interlocked.Read(ref _sourceFramesRead),
        Interlocked.Read(ref _outputSignalFrames), Volatile.Read(ref _outputPeak), Volatile.Read(ref _primed));

    public async Task StartAsync(RouteOptions options)
    {
        await StopAsync();
        _stop.Reset(); _error = null;
        _captured = _rendered = _underruns = _dropped = _discontinuities = 0;
        _capturePackets = _inputSignalFrames = _sourceFramesRead = _outputSignalFrames = 0;
        _outputPeak = 0; _primed = false;
        _queueMs = _driftPpm = 0; _peak = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => Run(options, started)) { IsBackground = true, Name = "A2DP WASAPI router" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        await started.Task;
    }

    public async Task StopAsync()
    {
        var thread = _thread;
        if (thread is null) return;
        _stop.Set();
        await Task.Run(() => thread.Join());
        _thread = null;
        _running = false;
        _peak = 0; _queueMs = 0;
        if (_error is null) _description = "路由已停止";
    }

    private unsafe void Run(RouteOptions options, TaskCompletionSource started)
    {
        nint mmcss = 0;
        bool com = false;
        try
        {
            Native.Check(Native.CoInitializeEx(0, 0)); com = true;
            using var input = AudioStream.Open(options.InputId, true);
            using var output = AudioStream.Open(options.OutputId, false, options.ExclusiveOutput);
            var buffer = new RouteBuffer(input.Format.Rate, output.Format.Rate, options.BufferMs);
            // Give render deadlines precedence when both events are signaled.
            WaitHandle[] events = [_stop, output.Ready, input.Ready];
            uint task = 0;
            mmcss = Native.AvSetMmThreadCharacteristics("Pro Audio", ref task);
            string scheduling = mmcss == 0 ? " / MMCSS 不可用" : "";
            _description = $"输入 {input.Format} · {input.Mode} {input.PeriodMs:0.##} ms\n" +
                           $"输出 {output.Format} · {output.Mode} {output.PeriodMs:0.##} ms{scheduling}\n" +
                           $"端点缓冲：输入 {input.BufferFrames} 帧，输出 {output.BufferFrames} 帧";
            // Service the allocated endpoint capacity rather than assuming an engine
            // period equals the buffer size. Drivers may expose several periods here.
            uint quantum = output.BufferFrames;
            Native.Check(output.Render.GetBuffer(quantum, out _));
            Native.Check(output.Render.ReleaseBuffer(quantum, Native.Silent));
            Native.Check(input.Client.Start());
            Native.Check(output.Client.Start());
            _running = true;
            started.TrySetResult();
            while (true)
            {
                int signaled = WaitHandle.WaitAny(events, 1000);
                if (signaled == 0) break;
                if (signaled == WaitHandle.WaitTimeout)
                    throw new IOException("音频设备未响应事件，路由已停止。请检查设备连接后重新启动。");
                Native.Check(input.Capture.GetNextPacketSize(out uint packet));
                int packetBudget = 8;
                while (packet > 0 && packetBudget-- > 0)
                {
                    if (_stop.WaitOne(0)) return;
                    Native.Check(input.Capture.GetBuffer(out nint data, out uint frames, out uint flags, out _, out _));
                    try
                    {
                        buffer.BeginPacket((flags & Native.Discontinuity) != 0);
                        bool silent = (flags & Native.Silent) != 0;
                        if (frames > 0 && !silent && data == 0) throw new IOException("采集端点返回了无效的非静音数据指针。");
                        byte* bytes = (byte*)data;
                        float peak = 0;
                        long signalFrames = 0;
                        for (uint i = 0; i < frames; i++)
                        {
                            byte* frame = bytes + i * input.Format.BlockAlign;
                            float l = silent ? 0 : input.Format.Decode(frame);
                            float r = silent ? 0 : input.Format.Channels == 1 ? l : input.Format.Decode(frame + input.Format.BytesPerSample);
                            buffer.Push(l, r);
                            float level = Math.Max(Math.Abs(l), Math.Abs(r));
                            peak = Math.Max(peak, level);
                            if (level > 0.000001f) signalFrames++;
                        }
                        Volatile.Write(ref _peak, peak);
                        Interlocked.Add(ref _captured, frames);
                        Interlocked.Increment(ref _capturePackets);
                        Interlocked.Add(ref _inputSignalFrames, signalFrames);
                    }
                    finally { Native.Check(input.Capture.ReleaseBuffer(frames)); }
                    Native.Check(input.Capture.GetNextPacketSize(out packet));
                }

                if (packet > 0) input.Ready.Set();
                bool renderReady = signaled == 1 || output.Ready.WaitOne(0);
                // Shared mode may be serviced on either event using current padding.
                // Exclusive mode must write a full block only after a render event.
                if (!output.Exclusive || renderReady)
                {
                    uint frames = output.BufferFrames;
                    if (!output.Exclusive)
                    {
                        Native.Check(output.Client.GetCurrentPadding(out uint padding));
                        uint available = output.BufferFrames - Math.Min(padding, output.BufferFrames);
                        frames = available;
                    }
                    if (frames > 0)
                    {
                        buffer.BeginRender(frames);
                        float gain = Volatile.Read(ref _muted) ? 0 : Volatile.Read(ref _gain);
                        Native.Check(output.Render.GetBuffer(frames, out nint data));
                        try
                        {
                            long signalFrames = 0;
                            float peak = 0;
                            byte* bytes = (byte*)data;
                            for (uint i = 0; i < frames; i++)
                            {
                                buffer.TryRead(out float l, out float r);
                                float level = Math.Max(Math.Abs(l * gain), Math.Abs(r * gain));
                                peak = Math.Max(peak, level);
                                if (level > 0.000001f) signalFrames++;
                                byte* frame = bytes + i * output.Format.BlockAlign;
                                if (output.Format.Channels == 1) output.Format.Encode(frame, (l + r) * 0.5f * gain);
                                else
                                {
                                    output.Format.Encode(frame, l * gain);
                                    output.Format.Encode(frame + output.Format.BytesPerSample, r * gain);
                                    for (int ch = 2; ch < output.Format.Channels; ch++)
                                        output.Format.Encode(frame + ch * output.Format.BytesPerSample, 0);
                                }
                            }
                            Interlocked.Add(ref _outputSignalFrames, signalFrames);
                            Volatile.Write(ref _outputPeak, peak);
                        }
                        finally { Native.Check(output.Render.ReleaseBuffer(frames, 0)); }
                        Interlocked.Add(ref _rendered, frames);
                    }
                }
                Interlocked.Exchange(ref _dropped, buffer.DroppedFrames);
                Interlocked.Exchange(ref _discontinuities, buffer.Discontinuities);
                Interlocked.Exchange(ref _underruns, buffer.Underruns);
                Interlocked.Exchange(ref _sourceFramesRead, buffer.SourceFramesRead);
                Volatile.Write(ref _primed, buffer.Primed);
                Volatile.Write(ref _driftPpm, buffer.DriftPpm);
                Volatile.Write(ref _queueMs, buffer.QueueMs);
                Volatile.Write(ref _peak, Volatile.Read(ref _peak) * 0.98f);
            }
        }
        catch (Exception ex)
        {
            _error = $"{ex.Message} (0x{ex.HResult:X8})";
            _description = "路由已停止：" + _error;
            started.TrySetException(ex);
        }
        finally
        {
            _running = false; Volatile.Write(ref _peak, 0);
            if (mmcss != 0) Native.AvRevertMmThreadCharacteristics(mmcss);
            if (com) Native.CoUninitialize();
        }
    }

    public void Dispose()
    {
        _stop.Set(); _thread?.Join(); _thread = null;
        _stop.Dispose();
    }
}

using System.Runtime.InteropServices;

namespace A2dpSinkRouter.Audio;

internal sealed class AudioStream : IDisposable
{
    private IMMDevice? _device;
    private IAudioClient? _client;
    private object? _service;
    public AutoResetEvent Ready { get; } = new(false);
    public IAudioClient Client => _client!;
    public PcmFormat Format { get; private set; } = null!;
    public uint BufferFrames { get; private set; }
    public double PeriodMs { get; private set; }
    public string Mode { get; private set; } = "";
    public bool Exclusive { get; private set; }
    public IAudioCaptureClient Capture => (IAudioCaptureClient)_service!;
    public IAudioRenderClient Render => (IAudioRenderClient)_service!;

    public static AudioStream Open(string id, bool capture, bool exclusive = false)
    {
        var stream = new AudioStream();
        try { stream.Initialize(id, capture, exclusive); return stream; }
        catch { stream.Dispose(); throw; }
    }

    private void Activate()
    {
        var iid = typeof(IAudioClient).GUID;
        Native.Check(_device!.Activate(ref iid, 23, 0, out object obj));
        _client = (IAudioClient)obj;
    }

    private void Initialize(string id, bool capture, bool exclusive)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try { Native.Check(enumerator.GetDevice(id, out _device)); }
        finally { Native.Release(enumerator); }
        Native.Check(((IMMEndpoint)_device).GetDataFlow(out int flow));
        if (flow != (capture ? 1 : 0)) throw new InvalidOperationException("输入或输出端点方向错误。");
        Native.Check(_device.GetState(out uint state));
        if (state != 1) throw new AudioEndpointNotReadyException("音频端点尚未就绪，正在等待设备可用。");
        Activate();
        Native.Check(Client.GetMixFormat(out nint format));
        try
        {
            Format = PcmFormat.Read(format);
            if (capture && Format.Channels > 2) throw new NotSupportedException("首版输入支持单声道或立体声，请选择 A2DP 输入端点。");
            bool initialized = false;
            if (exclusive && !capture)
            {
                InitializeExclusive(format);
                initialized = true;
            }
            if (!initialized && _client is IAudioClient3 lowLatency && lowLatency.GetSharedModeEnginePeriod(format, out _, out _, out uint minimum, out _) >= 0)
            {
                int hr = lowLatency.InitializeSharedAudioStream(Native.EventCallback, minimum, format, 0);
                if (hr >= 0)
                {
                    initialized = true; PeriodMs = minimum * 1000.0 / Format.Rate;
                    Mode = "共享低周期";
                }
                else
                {
                    // A failed initialization may leave client state unsuitable for reuse.
                    Native.Release(_client); _client = null; Activate();
                    Mode = $"共享兼容 (低周期 0x{hr:X8})";
                }
            }
            if (!initialized)
            {
                Native.Check(Client.Initialize(0, Native.EventCallback, 0, 0, format, 0));
                Native.Check(Client.GetDevicePeriod(out long period, out _));
                PeriodMs = period / 10000.0;
                if (Mode.Length == 0) Mode = "共享兼容";
            }
            Native.Check(Client.GetBufferSize(out uint frames)); BufferFrames = frames;
            Native.Check(Client.SetEventHandle(Ready.SafeWaitHandle.DangerousGetHandle()));
            var iid = capture ? typeof(IAudioCaptureClient).GUID : typeof(IAudioRenderClient).GUID;
            Native.Check(Client.GetService(ref iid, out _service));
        }
        finally { Marshal.FreeCoTaskMem(format); }
    }

    private void InitializeExclusive(nint mixFormat)
    {
        // Prefer the native format, then PCM at the same sample rate. Never silently fall
        // back to shared mode when the user explicitly requests an exclusive output.
        var candidates = new List<nint> { mixFormat };
        try
        {
            foreach (int bits in new[] { 32, 24, 16 })
            {
                int channels = Math.Min(2, Format.Channels);
                nint candidate = Marshal.AllocHGlobal(18);
                for (int i = 0; i < 18; i++) Marshal.WriteByte(candidate, i, 0);
                Marshal.WriteInt16(candidate, 0, 1);
                Marshal.WriteInt16(candidate, 2, (short)channels);
                Marshal.WriteInt32(candidate, 4, Format.Rate);
                Marshal.WriteInt32(candidate, 8, Format.Rate * channels * bits / 8);
                Marshal.WriteInt16(candidate, 12, (short)(channels * bits / 8));
                Marshal.WriteInt16(candidate, 14, (short)bits);
                candidates.Add(candidate);
            }
            foreach (nint candidate in candidates)
            {
                int support = Client.IsFormatSupported(1, candidate, out nint closest);
                if (closest != 0) Marshal.FreeCoTaskMem(closest);
                if (support != 0) continue;
                var chosen = PcmFormat.Read(candidate);
                Native.Check(Client.GetDevicePeriod(out long standard, out long minimum));
                long period = minimum > 0 ? minimum : standard;
                int hr = Client.Initialize(1, Native.EventCallback, period, period, candidate, 0);
                if (hr == unchecked((int)0x88890019)) // AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED
                {
                    Native.Check(Client.GetBufferSize(out uint alignedFrames));
                    period = (long)Math.Round(10_000_000.0 * alignedFrames / chosen.Rate);
                    Native.Release(_client); _client = null; Activate();
                    hr = Client.Initialize(1, Native.EventCallback, period, period, candidate, 0);
                }
                Native.Check(hr);
                Format = chosen; Exclusive = true; Mode = "独占事件驱动";
                PeriodMs = period / 10000.0;
                return;
            }
            throw new NotSupportedException("设备不支持当前采样率的独占输出格式，请关闭独占选项后重试。");
        }
        finally { foreach (nint candidate in candidates.Skip(1)) Marshal.FreeCoTaskMem(candidate); }
    }

    public void Dispose()
    {
        _client?.Stop();
        Native.Release(_service); _service = null;
        Native.Release(_client); _client = null;
        Native.Release(_device); _device = null;
        Ready.Dispose();
    }
}

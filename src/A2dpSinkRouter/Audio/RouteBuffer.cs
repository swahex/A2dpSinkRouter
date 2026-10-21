namespace A2dpSinkRouter.Audio;

// Device-independent streaming policy, shared by the WASAPI worker and regression checks.
internal sealed class RouteBuffer
{
    private const double MaximumClockCorrection = 0.002;
    private readonly FrameQueue _queue;
    private readonly int _inputRate, _target;
    private readonly double _nominalStep;
    private double _smoothedError, _step;
    private bool _starvedThisBlock;
    public bool Primed { get; private set; }
    public long Discontinuities { get; private set; }
    public long Underruns { get; private set; }
    public long SourceFramesRead { get; private set; }
    public long DroppedFrames => _queue.DroppedFrames;
    public double QueueMs => _queue.Count * 1000.0 / _inputRate;
    public double DriftPpm { get; private set; }

    public RouteBuffer(int inputRate, int outputRate, int bufferMs)
    {
        _inputRate = inputRate;
        _target = Math.Max(64, (int)Math.Ceiling(inputRate * bufferMs / 1000.0));
        _nominalStep = (double)inputRate / outputRate;
        _step = _nominalStep;
        _queue = new FrameQueue(Math.Max(inputRate / 4, _target * 2 + 64), inputRate, outputRate);
    }

    public void BeginPacket(bool discontinuity)
    {
        if (!discontinuity) return;
        Discontinuities++;
        // This flag describes a gap BEFORE the current valid packet. Preserve both
        // queued audio and clock feedback: some drivers flag nearly every packet,
        // which would otherwise prevent the smoothed error from tracking drift.
        // Unprimed rendering and backlog trimming reset feedback below.
    }
    public void Push(float left, float right) => _queue.Push(left, right);

    public void BeginRender(uint outputFrames)
    {
        int needed = (int)Math.Ceiling(outputFrames * _nominalStep * (1 + MaximumClockCorrection)) + _queue.LookaheadFrames + 1;
        int threshold = Math.Max(_target, needed);
        if (_queue.Count > Math.Max(threshold + _inputRate / 25, threshold * 2))
        {
            _queue.TrimTo(threshold); _smoothedError = 0;
        }
        if (!Primed && _queue.Count >= threshold) Primed = true;
        _starvedThisBlock = false;
        _smoothedError += (_queue.Count - threshold - _smoothedError) * 0.005;
        double correction = Primed ? Math.Clamp(_smoothedError / (_inputRate * 5.0), -MaximumClockCorrection, MaximumClockCorrection) : 0;
        if (!Primed) _smoothedError = 0;
        DriftPpm = correction * 1_000_000;
        _step = _nominalStep * (1 + correction);
    }

    public bool TryRead(out float left, out float right)
    {
        left = right = 0;
        if (!Primed) return false;
        if (_queue.TryRead(_step, out left, out right)) { SourceFramesRead++; return true; }
        Primed = false;
        if (!_starvedThisBlock) { Underruns++; _starvedThisBlock = true; }
        return false;
    }
}

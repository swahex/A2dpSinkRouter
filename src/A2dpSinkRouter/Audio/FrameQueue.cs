using System.Numerics;

namespace A2dpSinkRouter.Audio;

// Single-worker, bounded storage. Coefficients and mirrored rings are allocated once.
internal sealed class FrameQueue
{
    private const int BaseTaps = 192;
    private const int Phases = 1024;
    private const double KaiserBeta = 10.5;
    private readonly int _taps, _half, _capacity;
    private readonly bool _sameRate;
    private readonly float[] _left, _right, _kernel;
    private long _written;
    private double _position;
    public long DroppedFrames { get; private set; }
    public int Count => Math.Max(0, (int)(_written - _position));
    public int Capacity => _capacity;
    public int LookaheadFrames => _half;

    public FrameQueue(int capacity, int inputRate, int outputRate)
    {
        if (inputRate <= 0 || outputRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputRate));
        int width = Vector<float>.Count;
        int required = (int)Math.Ceiling(BaseTaps * Math.Max(1.0, (double)inputRate / outputRate));
        _taps = (required + width - 1) / width * width;
        _half = _taps / 2;
        if (capacity < _taps + 32) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity; _sameRate = inputRate == outputRate;
        _left = new float[capacity * 2]; _right = new float[capacity * 2];
        // The extra phase permits interpolation up to a fractional position of one.
        _kernel = new float[(Phases + 1) * _taps];
        double lowerRate = Math.Min(inputRate, outputRate);
        double passband = Math.Min(20000, lowerRate * 0.45);
        double cutoff = (passband + lowerRate * 0.5) / inputRate;
        double windowScale = BesselI0(KaiserBeta);
        for (int phase = 0; phase <= Phases; phase++)
        {
            double sum = 0;
            int start = phase * _taps;
            for (int tap = 0; tap < _taps; tap++)
            {
                double x = tap - _half + 1 - (double)phase / Phases;
                double normalized = x / _half;
                double window = Math.Abs(normalized) >= 1 ? 0 : BesselI0(KaiserBeta * Math.Sqrt(1 - normalized * normalized)) / windowScale;
                double sinc = Math.Abs(x) < 1e-12 ? cutoff : Math.Sin(Math.PI * cutoff * x) / (Math.PI * x);
                double weight = sinc * window;
                _kernel[start + tap] = (float)weight; sum += weight;
            }
            for (int tap = 0; tap < _taps; tap++) _kernel[start + tap] = (float)(_kernel[start + tap] / sum);
        }
    }

    private static double BesselI0(double x)
    {
        double sum = 1, term = 1, quarter = x * x / 4;
        for (int k = 1; k < 64; k++)
        {
            term *= quarter / (k * k); sum += term;
            if (term < sum * 1e-16) break;
        }
        return sum;
    }

    public void Reset() { _written = 0; _position = 0; }
    public void TrimTo(int frames)
    {
        if (Count <= frames) return;
        double newPosition = _written - Math.Max(frames, _half + 1);
        if (newPosition <= _position) return;
        DroppedFrames += (long)(newPosition - _position); _position = newPosition;
    }
    public void Push(float left, float right)
    {
        if (_written - _position >= _capacity - _half - 1) { _position += 1; DroppedFrames++; }
        int slot = (int)(_written % _capacity);
        _left[slot] = _left[slot + _capacity] = left;
        _right[slot] = _right[slot + _capacity] = right;
        _written++;
    }

    public bool TryRead(double step, out float left, out float right)
    {
        long center = (long)_position;
        left = right = 0;
        if (center + _half >= _written) return false;
        double fraction = _position - center;
        if (_sameRate && step == 1 && fraction == 0)
        {
            int slot = (int)(center % _capacity);
            left = _left[slot]; right = _right[slot]; _position++;
            return true;
        }
        double phasePosition = fraction * Phases;
        int phase = Math.Min(Phases - 1, (int)phasePosition), kernelStart = phase * _taps;
        float blend = (float)(phasePosition - phase);
        long first = center - _half + 1, oldest = Math.Max(0, _written - _capacity);
        if (Vector.IsHardwareAccelerated && first >= oldest)
        {
            // Mirroring makes the complete FIR window contiguous across ring wrap.
            int slot = (int)(first % _capacity);
            var amount = new Vector<float>(blend);
            var sumLeft = Vector<float>.Zero; var sumRight = Vector<float>.Zero;
            for (int tap = 0; tap < _taps; tap += Vector<float>.Count)
            {
                var a = new Vector<float>(_kernel, kernelStart + tap);
                var b = new Vector<float>(_kernel, kernelStart + _taps + tap);
                var weights = a + (b - a) * amount;
                sumLeft += new Vector<float>(_left, slot + tap) * weights;
                sumRight += new Vector<float>(_right, slot + tap) * weights;
            }
            left = Vector.Sum(sumLeft); right = Vector.Sum(sumRight);
        }
        else
        {
            // A fresh/reset stream has no negative-time history. Never read stale slots.
            double sumLeft = 0, sumRight = 0;
            for (int tap = 0; tap < _taps; tap++)
            {
                long frame = first + tap;
                if (frame < oldest) continue;
                int slot = (int)(frame % _capacity);
                double a = _kernel[kernelStart + tap];
                double weight = a + (_kernel[kernelStart + _taps + tap] - a) * blend;
                sumLeft += _left[slot] * weight; sumRight += _right[slot] * weight;
            }
            left = (float)sumLeft; right = (float)sumRight;
        }
        _position += step;
        return true;
    }
}

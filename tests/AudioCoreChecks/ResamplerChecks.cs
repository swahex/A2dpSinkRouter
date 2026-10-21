using A2dpSinkRouter.Audio;

internal static class ResamplerChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        foreach (var (input, output) in new[] { (44100, 96000), (48000, 44100), (96000, 48000), (192000, 44100) })
        foreach (int frequency in new[] { 1000, 18000, 20000 })
        {
            var queue = new FrameQueue(input * 2 + 1024, input, output);
            for (int i = 0; i < input * 2; i++) queue.Push((float)(0.5 * Math.Sin(2 * Math.PI * frequency * i / input)), 0);
            int warmup = output / 10, count = 0;
            double sin = 0, cos = 0, power = 0;
            while (count < warmup + output && queue.TryRead((double)input / output, out float left, out float right))
            {
                if (count >= warmup)
                {
                    double angle = 2 * Math.PI * frequency * count / output;
                    sin += left * Math.Sin(angle); cos += left * Math.Cos(angle); power += (double)left * left;
                    Require(right == 0, "Resampler introduced channel crosstalk");
                }
                count++;
            }
            Require(count == warmup + output, "Insufficient resampled frames");
            double amplitude = 2 * Math.Sqrt(sin * sin + cos * cos) / output;
            double gainDb = 20 * Math.Log10(amplitude / 0.5);
            double residual = Math.Max(0, power / output - amplitude * amplitude / 2);
            double residualDb = 10 * Math.Log10(Math.Max(1e-30, residual) / (amplitude * amplitude / 2));
            Require(Math.Abs(gainDb) < 0.01, $"SRC passband error: {input}->{output} {frequency}Hz {gainDb:F5}dB");
            Require(residualDb < -100, $"SRC phase interpolation residual: {input}->{output} {frequency}Hz {residualDb:F2}dB");
        }
        Console.WriteLine("PASS SRC 1/18/20kHz passband and residuals, including 192->44.1kHz");

        var identity = new FrameQueue(512, 48000, 48000);
        var random = new Random(381);
        var expected = Enumerable.Range(0, 256).Select(_ => (float)(random.NextDouble() * 1.8 - 0.9)).ToArray();
        foreach (float sample in expected) identity.Push(sample, -sample);
        for (int i = 0; i <= identity.LookaheadFrames; i++) identity.Push(0, 0);
        foreach (float sample in expected)
            Require(identity.TryRead(1, out float left, out float right) && left == sample && right == -sample, "Equal-rate integer-phase samples must be bit-exact");
        identity.Reset();
        for (int i = 0; i < 300; i++) identity.Push(0, 0);
        while (identity.TryRead(1.0001, out float left, out float right)) Require(left == 0 && right == 0, "Reset leaked stale mirrored-ring history");
        Console.WriteLine("PASS equal-rate bit-exact bypass and clean reset");

        var streaming = new FrameQueue(512, 44100, 96000);
        int written = 0;
        double position = 0, errorPower = 0, signalPower = 0;
        const int total = 96000 * 2;
        for (int n = 0; n < total; n++)
        {
            double step = 44100.0 / 96000 * (1 + 0.002 * Math.Sin(2 * Math.PI * n / 96000));
            while (written <= (long)position + streaming.LookaheadFrames)
            {
                float sample = (float)(0.5 * Math.Sin(2 * Math.PI * 18000 * written / 44100));
                streaming.Push(sample, -sample); written++;
            }
            Require(streaming.TryRead(step, out float left, out float right), "Streaming FIR ran out of prefetched samples");
            double reference = 0.5 * Math.Sin(2 * Math.PI * 18000 * position / 44100);
            if (n > 1000) { errorPower += (left - reference) * (left - reference); signalPower += reference * reference; }
            Require(left == -right, "Streaming stereo polarity changed");
            position += step;
        }
        double driftResidualDb = 10 * Math.Log10(errorPower / signalPower);
        Require(driftResidualDb < -95, $"Drift-adjusted SRC quality regressed: {driftResidualDb:F2}dB");
        Require(streaming.DroppedFrames == 0, "Normal ring wrap dropped source samples");
        Console.WriteLine($"PASS repeated ring wrap and smoothly changing +/-2000ppm: {driftResidualDb:F2}dB residual");
    }
}

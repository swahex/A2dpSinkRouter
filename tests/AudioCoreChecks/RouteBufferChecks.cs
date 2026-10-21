using A2dpSinkRouter.Audio;

internal static class RouteBufferChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Push(RouteBuffer buffer, int frames, bool discontinuity = false)
    {
        buffer.BeginPacket(discontinuity);
        for (int i = 0; i < frames; i++) buffer.Push(0.25f, -0.25f);
    }

    private static int Render(RouteBuffer buffer, int frames)
    {
        buffer.BeginRender((uint)frames);
        int read = 0;
        for (int i = 0; i < frames; i++)
        {
            bool success = buffer.TryRead(out float left, out float right);
            Require(float.IsFinite(left) && left == -right, "Route buffer corrupted stereo samples");
            if (success) read++;
            else Require(left == 0 && right == 0, "Unprimed/starved output must be silent");
        }
        return read;
    }

    public static void Run()
    {
        ClockDrift();
        PacketJitter();
        PrebufferAndResume();
    }

    private static void ClockDrift()
    {
        // Independent virtual clocks: 10 ms source packets and 10 ms output blocks.
        // Packet deadlines, not the controller's read position, determine arrivals.
        foreach (int ppm in new[] { -1000, 1000 })
        foreach (bool flagged in new[] { false, true })
        {
            const int inputRate = 44100, outputRate = 48000, seconds = 90;
            // Leave room for a full packet of arrival phase ripple plus the
            // proportional controller's steady-state queue offset (5 ms at 1000 ppm).
            var buffer = new RouteBuffer(inputRate, outputRate, 40);
            double packetPeriod = 0.01 / (1 + ppm / 1_000_000.0);
            double nextPacket = 0, minQueue = double.MaxValue, maxQueue = 0, correctionSum = 0;
            int measuredBlocks = 0;
            for (int block = 0; block < seconds * 100; block++)
            {
                double now = block * 0.01;
                while (nextPacket <= now + 1e-9)
                {
                    Push(buffer, 441, flagged);
                    nextPacket += packetPeriod;
                }
                Render(buffer, 480);
                if (block < 6000) continue;
                minQueue = Math.Min(minQueue, buffer.QueueMs);
                maxQueue = Math.Max(maxQueue, buffer.QueueMs);
                correctionSum += buffer.DriftPpm;
                measuredBlocks++;
            }
            double correction = correctionSum / measuredBlocks;
            string label = $"40 ms, {ppm:+#;-#} ppm, every-packet discontinuity={flagged}";
            Console.WriteLine($"CLOCK {label}: queue={minQueue:F2}..{maxQueue:F2} ms, correction={correction:F1} ppm, underruns={buffer.Underruns}, dropped={buffer.DroppedFrames}");
            Require(buffer.Underruns == 0 && buffer.DroppedFrames == 0,
                $"Clock feedback must avoid underruns and trimming: {label}");
            // Packet quantization contributes up to 10 ms of queue ripple.
            Require(maxQueue - minQueue < 11 && minQueue > 10 && maxQueue < 50,
                $"Clock feedback queue did not settle: {label}");
            Require(Math.Abs(correction - ppm) < 100,
                $"Clock correction did not track the independent source clock: {label}, actual={correction:F1}");
            Require(buffer.Discontinuities == (flagged ? (long)Math.Floor((seconds - 0.01 + 1e-9) / packetPeriod) + 1 : 0),
                "Clock simulation discontinuity accounting failed");
        }
        Console.WriteLine("PASS 90-second virtual clock feedback in both directions, with and without repeated flags");
    }

    private static void PacketJitter()
    {
        const int rate = 48000;
        var buffer = new RouteBuffer(rate, rate, 40);
        int[] lengthsMs = [3, 17, 6, 14, 9, 11];
        int[] delaysMs = [0, 4, 1, 8, 0, 2];
        var packets = new List<(int DueMs, int Frames)>();
        int sourceMs = 0, previousDue = 0;
        for (int packet = 0; sourceMs < 12000; packet++)
        {
            int length = lengthsMs[packet % lengthsMs.Length];
            sourceMs += length;
            int due = Math.Max(previousDue, sourceMs + delaysMs[packet % delaysMs.Length]);
            // Hold a short group for 20 ms, then deliver all overdue packets together.
            if (sourceMs >= 5000 && sourceMs < 5020) due = Math.Max(due, 5020);
            packets.Add((due, length * rate / 1000));
            previousDue = due;
        }
        int next = 0, rendered = 0, maxBatch = 0;
        for (int now = 0; now < 12000; now += 5)
        {
            int batch = 0;
            while (next < packets.Count && packets[next].DueMs <= now)
            {
                Push(buffer, packets[next++].Frames, true);
                batch++;
            }
            maxBatch = Math.Max(maxBatch, batch);
            rendered += Render(buffer, 240);
        }
        Require(maxBatch >= 2, "Jitter simulation must exercise burst delivery");
        Require(rendered > rate * 11 && buffer.Underruns == 0 && buffer.DroppedFrames == 0,
            $"Jitter within the prebuffer budget must remain continuous: read={rendered}, underruns={buffer.Underruns}, dropped={buffer.DroppedFrames}");
        Require(buffer.QueueMs < 70, "Packet jitter left excessive queued latency");
        Console.WriteLine("PASS irregular packet lengths, delayed arrivals and burst catch-up with repeated flags");
    }

    private static void PrebufferAndResume()
    {
        // Exercise both the selected target and the larger actual-render-block threshold.
        foreach (int block in new[] { 240, 1440 })
        {
            var buffer = new RouteBuffer(48000, 48000, 20);
            int lookahead = new FrameQueue(12000, 48000, 48000).LookaheadFrames;
            int threshold = Math.Max(960, (int)Math.Ceiling(block * 1.002) + lookahead + 1);
            Push(buffer, threshold - 1, true);
            Require(Render(buffer, block) == 0 && !buffer.Primed && buffer.Underruns == 0,
                "One frame below the startup threshold must wait silently");
            Push(buffer, 1, true);
            Require(Render(buffer, block) == block && buffer.Primed,
                "The exact startup threshold must admit a complete render block");

            // No input for 200 ms: one starvation transition, then silent prebuffering.
            for (int elapsed = 0; elapsed < 9600; elapsed += block) Render(buffer, block);
            Require(!buffer.Primed && buffer.Underruns == 1, "A pause must count one underrun and return to prebuffering");
            int queued = (int)Math.Round(buffer.QueueMs * 48);
            Push(buffer, threshold - 1 - queued, true);
            Require(Render(buffer, block) == 0 && buffer.Underruns == 1,
                "Resume must wait for a full prebuffer without repeated underrun counts");
            Push(buffer, 1, true);
            Require(Render(buffer, block) == block && buffer.Primed && buffer.Underruns == 1,
                "Resume at the exact threshold must produce a complete block");

            Push(buffer, 24000, true);
            Require(Render(buffer, block) == block && buffer.DroppedFrames > 0 && buffer.QueueMs < 40,
                "An oversized catch-up burst must discard old latency and keep rendering");
        }
        Console.WriteLine("PASS prebuffer one-frame boundaries, small/large render blocks, pause/resume and oversized burst recovery");
    }
}

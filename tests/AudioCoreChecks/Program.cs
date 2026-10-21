using System.Runtime.InteropServices;
using A2dpSinkRouter.Audio;

// Synthetic buffers only: no Core Audio, Bluetooth, microphone, or output device access.
static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

// A failed check should report diagnostics and a nonzero exit code rather than
// escaping to Windows as an unhandled exception (which may open an error dialog).
try
{
unsafe
{
    byte* data = stackalloc byte[8];
    foreach (var spec in new[] { (8, false), (16, false), (24, false), (32, false), (32, true), (64, true) })
    {
        var format = new PcmFormat(48000, 1, spec.Item1, spec.Item1 / 8, spec.Item2);
        foreach (float sample in new[] { -1f, -0.73f, 0f, 0.73f, 1f })
        {
            format.Encode(data, sample);
            float decoded = format.Decode(data);
            float tolerance = spec.Item1 == 8 ? 0.016f : spec.Item1 == 16 ? 0.00007f : 0.000001f;
            Require(Math.Abs(decoded - sample) < tolerance, $"PCM roundtrip failed: {spec}, {sample}, {decoded}");
        }
        format.Encode(data, float.NaN);
        Require(format.Decode(data) == 0, "NaN must become silence");
        format.Encode(data, 10f);
        Require(format.Decode(data) > 0.98f, "Positive clipping must not wrap negative");
    }
    Console.WriteLine("PASS PCM integer/float conversion, full-scale clipping, and NaN silence");
}

foreach (var rates in new[] { (44100, 48000), (48000, 44100), (96000, 48000), (48000, 96000) })
{
    int input = rates.Item1, output = rates.Item2;
    var queue = new FrameQueue(input * 2 + 256, input, output);
    for (int i = 0; i < input; i++)
    {
        float sample = (float)(Math.Sin(2 * Math.PI * 1000 * i / input) * 0.5);
        queue.Push(sample, -sample);
    }
    for (int i = 0; i <= queue.LookaheadFrames; i++) queue.Push(0, 0);
    int count = 0, crossings = 0; double sum = 0; float last = 0;
    while (queue.TryRead((double)input / output, out float l, out float r))
    {
        Require(float.IsFinite(l) && Math.Abs(l + r) < 1e-6, "Channel separation/finite output failed");
        if (last < 0 && l >= 0) crossings++;
        last = l; sum += l * l; count++;
    }
    Require(Math.Abs(count - output) <= 3, "Resampling duration changed after flushing filter lookahead");
    Require(Math.Abs(crossings - 1000) <= 2, "Resampling pitch changed");
    Require(Math.Abs(Math.Sqrt(sum / count) - 0.35355) < 0.01, "Resampling level changed");
    Console.WriteLine($"PASS {input} -> {output}: {count} frames; pitch, duration, level and stereo polarity");
}

{
    var queue = new FrameQueue(100000, 96000, 48000);
    for (int i = 0; i < 96000; i++) queue.Push((float)Math.Sin(2 * Math.PI * 36000 * i / 96000), 0);
    int count = 0; double power = 0;
    while (queue.TryRead(2, out float sample, out _)) { if (count > 100) power += sample * sample; count++; }
    Require(Math.Sqrt(power / (count - 101)) < 0.01, "Downsampling alias rejection failed");
    Console.WriteLine("PASS downsampling removes out-of-band 36 kHz tone");
}

{
    var queue = new FrameQueue(256, 48000, 48000);
    for (int i = 0; i < 10000; i++) queue.Push(i / 10000f, -i / 10000f);
    Require(queue.Count <= 256 && queue.DroppedFrames > 9700, "Queue must remain bounded");
    queue.TrimTo(64);
    Require(queue.Count == Math.Max(64, queue.LookaheadFrames + 1), "Latency trimming must preserve FIR lookahead and discard stale frames");
    queue.Reset();
    Require(!queue.TryRead(1, out _, out _) && queue.Count == 0, "Discontinuity reset must remove stale audio");
    for (int i = 0; i < 200; i++) queue.Push(0, 0);
    while (queue.TryRead(1.002, out float l, out float r)) Require(l == 0 && r == 0, "Silence must remain silence");
    Console.WriteLine("PASS bounded overflow, stale-frame trimming, reset, and drift-adjusted silence");
}

{
    nint format = Marshal.AllocHGlobal(40);
    try
    {
        for (int i = 0; i < 40; i++) Marshal.WriteByte(format, i, 0);
        Marshal.WriteInt16(format, 0, unchecked((short)0xfffe));
        Marshal.WriteInt16(format, 2, 2); Marshal.WriteInt32(format, 4, 48000);
        Marshal.WriteInt16(format, 12, 8); Marshal.WriteInt16(format, 14, 32); Marshal.WriteInt16(format, 16, 22);
        Marshal.StructureToPtr(new Guid("00000003-0000-0010-8000-00aa00389b71"), format + 24, false);
        var pcm = PcmFormat.Read(format);
        Require(pcm.FloatingPoint && pcm.Rate == 48000 && pcm.Channels == 2, "Extensible format parsing failed");
        Marshal.WriteInt16(format, 12, 7);
        bool rejected = false;
        try { PcmFormat.Read(format); } catch (NotSupportedException) { rejected = true; }
        Require(rejected, "Invalid block alignment must be rejected");
    }
    finally { Marshal.FreeHGlobal(format); }
    Console.WriteLine("PASS extensible format parsing and invalid format rejection");
}
// Reproduce the former mute condition: every 10 ms capture packet flags a gap,
// while the user selects a 20 ms prebuffer. Clearing the queue on every flag can
// never reach that threshold, even though Windows and the capture stream have audio.
foreach (bool flagEveryPacket in new[] { false, true })
{
    var buffer = new RouteBuffer(48000, 96000, 20);
    int audibleFrames = 0;
    for (int tick = 0; tick < 100; tick++)
    {
        buffer.BeginPacket(flagEveryPacket);
        for (int i = 0; i < 480; i++) buffer.Push(0.25f, -0.25f);
        buffer.BeginRender(960);
        for (int i = 0; i < 960; i++)
        {
            buffer.TryRead(out float l, out float r);
            if (Math.Abs(l) > 0.1f && Math.Abs(r) > 0.1f) audibleFrames++;
        }
    }
    Require(audibleFrames > 80000, "Streaming prebuffer must not remain permanently muted on repeated discontinuities");
    Require(buffer.Discontinuities == (flagEveryPacket ? 100 : 0), "Discontinuity accounting failed");
    Console.WriteLine($"PASS streaming capture/render with repeated discontinuities={flagEveryPacket}: {audibleFrames} audible frames");
}

// A render block may span multiple engine periods. Prime for the actual requested
// block size, then accept continuous valid data despite flagged source gaps.
{
    var buffer = new RouteBuffer(48000, 48000, 5);
    int audibleFrames = 0;
    for (int tick = 0; tick < 100; tick++)
    {
        buffer.BeginPacket(true);
        for (int i = 0; i < 480; i++) buffer.Push(0.5f, 0.5f);
        if (tick % 3 != 2) continue;
        buffer.BeginRender(1440);
        for (int i = 0; i < 1440; i++)
        {
            buffer.TryRead(out float l, out _);
            if (l > 0.1f) audibleFrames++;
        }
    }
    Require(audibleFrames > 40000, "Multi-period render blocks must produce audio after priming");
    Console.WriteLine($"PASS multi-period render block admission: {audibleFrames} audible frames");
}

// Endpoint activation can follow Bluetooth connection by an arbitrary interval:
// simulate playback making the input available without a second Start click.
{
    var gate = new RouteStartGate();
    var states = new Dictionary<string, bool> { ["phone-input"] = false, ["chosen-output"] = true };
    bool Available(string id) => states.TryGetValue(id, out bool active) && active;
    var request = new RouteOptions("phone-input", "chosen-output", 20);
    gate.Request(request);
    Require(gate.Evaluate(Available) == RouteReadiness.WaitingForInput, "Inactive A2DP input must preserve pending start");
    for (int i = 0; i < 100; i++)
        Require(gate.Evaluate(Available) == RouteReadiness.WaitingForInput, "Idle phone must not time out or consume start intent");
    states["phone-input"] = true;
    Require(gate.Evaluate(Available) == RouteReadiness.Ready, "Playback activation must make the original request ready");
    Require(ReferenceEquals(gate.Requested, request) && gate.Complete(request), "Original route options must survive deferred start");
    Require(gate.Evaluate(Available) == RouteReadiness.NoRequest, "Successful start must be consumed exactly once");

    gate.Request(request); states["phone-input"] = false;
    gate.Cancel(); states["phone-input"] = true;
    Require(gate.Evaluate(Available) == RouteReadiness.NoRequest, "Cancel must prevent a later playback event from starting audio");
    Require(!gate.Complete(request), "Cancelled request must not complete later");

    gate.Request(request);
    states["chosen-output"] = false; states["other-speaker"] = true;
    Require(gate.Evaluate(Available) == RouteReadiness.WaitingForOutput, "Must wait for the chosen output rather than another active speaker");
    var replacement = new RouteOptions("phone-input", "other-speaker", 12);
    gate.Request(replacement);
    Require(!gate.Complete(request) && ReferenceEquals(gate.Requested, replacement), "Late completion must not erase a newer route request");
    Require(gate.Evaluate(Available) == RouteReadiness.Ready, "Replacement request should use its explicitly chosen output");
    Console.WriteLine("PASS delayed input activation, pending intent, cancel, exact output selection, and stale completion");
}
Console.WriteLine("All deferred route-start checks passed. No audio devices were accessed.");
ResamplerChecks.Run();
RouteBufferChecks.Run();
Console.WriteLine("All audio core and SRC checks passed. No audio devices were accessed.");
return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL audio core checks: {ex}");
    return 1;
}

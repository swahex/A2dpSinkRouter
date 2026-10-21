# Audio core checks

Run from the repository root with the .NET 8 SDK:

```powershell
dotnet run --project tests/AudioCoreChecks -c Release
```

These are small console regression checks against linked production sources. They
use synthetic samples and virtual time; no phone, Bluetooth, or audio device is accessed.

`RouteBufferChecks.cs` covers three main risks:

- Independent capture/render clocks over 90 virtual seconds per case: 44.1→48 kHz,
  ±1000 ppm with a 40 ms buffer, each with and
  without every-packet discontinuity flags. The last 30 seconds must show bounded
  queue ripple and correction near the source clock offset, with no underruns or drops.
- Irregular 3–17 ms packets, delayed arrival, and burst catch-up against 5 ms output
  blocks with a 40 ms buffer. Valid arrivals also carry discontinuity flags.
- Startup and resume one frame below/at the admission threshold, 5/30 ms output
  blocks, starvation accounting, silent waiting, and oversized-backlog trimming.

The repeated-flag clock case caught a real regression: resetting the smoothed
error on every packet reduced correction to about −11 ppm for a −1000 ppm source,
causing two underruns in 90 seconds with a 40 ms buffer. Preserving feedback makes
that case track −1000 ppm without underruns; the positive offset is checked too.

Buffer size still matters. Exploratory runs with a 20 ms buffer and 10 ms packets
produced underruns even without flags: eight at −1000 ppm and two at −250 ppm over
90 virtual seconds. The
proportional controller needs a steady queue offset (5 ms at 1000 ppm), in addition
to packet-phase ripple, render-block space, and FIR lookahead. Packetized arrivals
can exhaust that margin before feedback has time to react. These passing cases
do not establish that every buffer size can tolerate the full ±2000 ppm correction
range. The exploratory 20 ms cases remain an unresolved buffer-margin limitation.

These checks do not exercise WASAPI event scheduling, endpoint failures/resource
release, full channel mapping, or real-time timing/allocation guarantees.

# A2DP Sink Router

Receive Bluetooth audio from your phone on Windows, then send it to a chosen headset, speaker, or USB DAC through a dedicated WASAPI route.

**No Windows “Listen to this device” required. No change to the system default output.** Designed to keep local routing buffers small; Bluetooth transport, drivers, and output hardware still determine total latency.

## Features

- Discover paired phones; connect, disconnect, and reconnect; remember routing per phone.
- Automatically refresh endpoint availability. Request routing before playback starts, or cancel the pending request.
- Event-driven WASAPI capture and rendering to an explicit output, with shared and optional exclusive modes.
- Bounded buffering, resampling, device-clock drift compensation, route volume, mute, and diagnostic counters.
- First-launch language selection, live Chinese/English switching, saved light/dark themes, and tray controls.
- Minimal divider sections, responsive columns, fixed routing actions, and a separate activity log.

## Requirements

- Windows 10 2004 (build 19041) or later, x64.
- A Bluetooth adapter, drivers, and phone compatible with the Windows A2DP sink path.
- **.NET 8 Desktop Runtime x64** for framework-dependent packages. Self-contained builds include the runtime.
- Pair the phone in Windows Bluetooth settings before use.

## Usage

1. Extract the complete application package and keep its files together. Exit an older running version before upgrading.
2. On first launch, choose Simplified Chinese or English. Existing configurations skip this dialog.
3. Select your phone and choose **Connect / reconnect**.
4. Select its A2DP input and your desired output, choose **Start routing**, then play audio on the phone.
5. If the input is not ready, routing waits automatically. **Cancel waiting** removes that request.

Use **Language** to switch languages, **Preferences → Appearance** for light/dark mode, and **Window** to minimize to the tray or exit. Double-click the tray icon to restore the window. Closing the window goes to the tray by default; this preference can be disabled. Stopping the route keeps the Bluetooth connection; exiting releases it.

The default prebuffer target is 12 ms, adjustable from 5 to 100 ms. Stop routing before changing buffer or exclusive-mode settings. Exclusive mode occupies the selected output device.

Smaller buffers are more sensitive to packet arrival jitter and clock differences; the default does not guarantee stable playback on every device. If playback breaks up or the underrun count keeps rising, stop routing, increase the prebuffer (try 40 ms first), and restart routing. A larger buffer adds local waiting time.

Settings and logs are stored in `%LOCALAPPDATA%\A2dpSinkRouter\`. Levels, frame counts, queue times, and device periods are diagnostic values, not end-to-end Bluetooth latency measurements.

## Build

Install the .NET 8 SDK on Windows, then run from the repository root:

```powershell
.\tools\Build.ps1
# Output: artifacts/win-x64/A2dpSinkRouter.exe

# Optional: bundle the runtime (initial restore downloads runtime packages)
.\tools\Build.ps1 -SelfContained

# Optional: synthetic algorithm checks; no sound card or Bluetooth access
.\tools\Build.ps1 -CheckAudioCore
```

Node.js is not required to build the application. Icon assets are committed. To regenerate them:

```powershell
npm install --prefix tools/icon @napi-rs/canvas
node tools/Draw-Icon.cjs
```

The included GitHub Actions workflow builds on Windows, runs synthetic checks, and uploads a framework-dependent package as a workflow artifact.

## Architecture and limits

```text
Phone → Windows A2DP sink → A2DP capture endpoint
      → WASAPI capture → bounded queue / resampling / clock compensation
      → WASAPI render → selected output device
```

Windows `AudioPlaybackConnection` manages Bluetooth reception. The data path calls Core Audio directly. One input routes to one output; ASIO, multiple outputs, EQ, and recording are not implemented. Device loss stops an active route and reports an error; starting again is required. Sleep recovery, cross-monitor DPI behavior, and hardware compatibility need practical verification.

Automated checks cover synthetic audio buffers and device-free UI previews. They do not verify Bluetooth hardware compatibility or end-to-end latency.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md), [CHANGELOG.md](CHANGELOG.md), and [SECURITY.md](SECURITY.md). Include your application version, Windows version, reproduction steps, and sanitized diagnostics in bug reports. Logs may contain device names and identifiers; review them before posting publicly.

## License

Project code, documentation, and icon assets are provided under [MIT](LICENSE). Dependencies retain their own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

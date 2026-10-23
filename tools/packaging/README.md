# A2DP Sink Router {{VERSION}}

Receive phone audio over Bluetooth on Windows and route it to a chosen output through WASAPI. Windows **Listen to this device** is not required, and the system default output is unchanged.

## Requirements

Windows 10 2004 (build 19041) or later, x64. {{RUNTIME_REQUIREMENT}} Extract all files together and run `A2dpSinkRouter.exe`. Exit an older running version before upgrading.

## Usage

1. Pair the phone in Windows Bluetooth settings.
2. Choose a language on first launch. Existing configurations keep their saved language.
3. Select the phone and choose **Connect / reconnect**.
4. Select its A2DP input and the desired output, choose **Start routing**, then play audio on the phone.
5. If the input is not ready, the app waits and starts when it becomes available. **Cancel waiting** removes the request.

Audio endpoint availability refreshes automatically. Device loss stops an active route and reports an error; start it again after the device becomes available. Disable system monitoring for that input while using this route to avoid duplicate playback.

## Menus and settings

- **Language** switches between Simplified Chinese and English and saves the choice.
- **Preferences → Appearance** selects light or dark mode.
- **Window → Minimize to tray** keeps audio running. Double-click the tray icon to restore.
- **Window → Exit application** stops and releases audio streams and Bluetooth connections.
- Closing the window goes to the tray by default; this preference can be disabled.
- Stopping routing keeps the phone connected. Prebuffer defaults to 12 ms, adjustable from 5 to 100 ms. Stop routing before changing buffer or exclusive-mode settings. Exclusive mode occupies the selected output device.

Smaller buffers are more sensitive to packet arrival jitter and clock differences; the default does not guarantee stable playback on every device. If playback breaks up or the underrun count keeps rising, stop routing, increase the prebuffer (try 40 ms first), and restart routing. A larger buffer adds local waiting time.

This is a portable application: exit from the tray and delete the extracted folder to remove it. To also clear saved settings and logs, remove `%LOCALAPPDATA%\A2dpSinkRouter\`. **Preferences → Open data folder** opens that location. Bluetooth pairings and separately installed .NET runtimes are managed by Windows.

## Diagnostics and documentation

Open **Activity log** for diagnostics. Settings and logs are stored in `%LOCALAPPDATA%\A2dpSinkRouter\`. Logs may include device names and identifiers; sanitize them before sharing.

Hardware testing by the maintainer confirmed normal operation across multiple phones, PCs and headsets. Queue times and device periods are local diagnostics, not end-to-end Bluetooth latency measurements.

See [changelog](../../CHANGELOG.md), [license](../../LICENSE), and [third-party notices](../../THIRD_PARTY_NOTICES.md).

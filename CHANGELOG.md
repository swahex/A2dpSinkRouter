# Changelog

## 0.4.3

- Fix an intermittent reconnect failure after manually disconnecting a phone, where the application reported that the Bluetooth connection closed before opening completed.
- Give Windows time to release the previous connection before reconnecting, allow the connection state to settle after opening, and retry a successful open that remains closed up to twice. Waiting and retries can be cancelled, and the UI continues to show the connecting state during recovery.
- Preserve native errors and HRESULTs when validating connection state. Cancellation, timeouts, access denial and other native failures do not trigger automatic retries.
- Add regression checks for delayed state updates, reconnect cooldown, retry limits, cancellation, retired callbacks and native error preservation.

## 0.4.2

- Check the selected input's Windows "Listen to this device" setting when starting a route. If explicitly enabled, show a non-blocking notification and activity-log reminder about possible doubled audio or echo.
- Keep routing available when the setting is absent or unreadable. The check is read-only and does not change Windows sound settings.

Validation: The maintainer verified normal operation across multiple phones, PCs and headsets. Release builds and offline audio, connection lifecycle and endpoint-property regression checks passed.

## 0.4.1

- Move the application and offline regression checks to .NET 10 LTS.
- Provide self-contained and framework-dependent release packages with SHA-256 checksums.
- Read the window title and package version from the project assembly/version metadata.
- Release the previous phone on reconnect/switch, allow cancelling a connection attempt, ignore retired connection callbacks, and preserve the startup phone when it is temporarily unavailable.
- Add device-free connection lifecycle regression checks.

The audio routing algorithm is unchanged. Validation covers Release builds, offline audio and connection lifecycle checks, and device-free UI previews.

## 0.3.5

No changes to audio routing behavior from 0.3.4.

## 0.3.4

Includes the clock-feedback fix and offline regression checks listed under 0.3.3. No additional audio behavior changes.

## 0.3.3

- Preserve drift feedback when capture packets carry discontinuity flags, preventing repeated flags from suppressing clock correction.
- Add offline checks for independent clock drift, packet jitter and bursts, prebuffer boundaries, pause/resume, and backlog recovery.
- Report failed audio core checks with diagnostics and exit code 1 instead of an unhandled exception dialog.

Validation: Synthetic audio checks and device-free UI previews.

## 0.3.2

- Improve SRC passband and alias rejection with a Kaiser-windowed sinc filter whose length scales for downsampling; interpolate adjacent coefficient phases.
- Add an exact equal-rate/no-drift path and SIMD filtering over mirrored ring buffers.
- Account for filter lookahead and maximum clock correction in prebuffer admission.
- Add offline SRC regression checks for high-frequency response, ring wrap, reset, and changing clock ratios.

## 0.3.1

- Replace the basic headphones icon with the **Signal Bridge** mark: a gray receiving enclosure, white waveform, and outward routing arrow. Retain monochrome styling and size-specific ICO layers.

## 0.3.0

- Replace rounded cards and buttons with monochrome divider sections and rectangular controls.
- Lay out header text using measured font heights to avoid subtitle clipping.
- Add persistent light/dark themes and first-launch language selection.
- Redraw the icon as simple headphones with no microphone arm; export size-specific ICO layers, PNG, and SVG.
- Add repository templates and Windows build automation.

Validation: Release build and device-free UI previews.

## 0.2.1

Top menus, aligned card rows, coalesced resize requests, cached measurements, and stable scrollbar sizing.

## 0.2.0

Bilingual UI, tray and close behavior, responsive layout, and initial Canvas icon.

## 0.1.2

Wait for the A2DP input to become ready after an explicit route request; refresh endpoint state automatically.

## 0.1.1

Fix repeated discontinuities keeping routing in prebuffer; improve render scheduling and diagnostics.

## 0.1.0

Initial Bluetooth reception and independent WASAPI routing implementation.

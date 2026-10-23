# Contributing

Use Chinese or English for issues and pull requests.

1. Install the .NET 10 SDK on Windows and run `./tools/Build.ps1 -CheckAudioCore`. The checks use synthetic memory data and do not access devices.
2. Keep UI, Bluetooth connection management, and the WASAPI data path separate. Avoid allocation, file I/O, and UI calls in the real-time audio loop.
3. Add Chinese and English strings for visible UI changes. Check both themes, narrow/wide windows, and scrolling; generated assets must retain their source.
4. Describe the problem, final behavior, and checks performed in your PR. Distinguish synthetic/UI checks from hardware audio results.

Do not commit hardware inventories, local logs, device IDs, SDK caches, binaries, or personal paths.

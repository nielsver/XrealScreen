# This PC (ARREALONE)

Checked 2026-10-15. This is not the machine described in `local-environment.md`.

| Item | Result |
|------|--------|
| .NET SDK | 10.0.402, which satisfies `global.json` (`10.0.401`, `latestFeature`) [verified-local] |
| `dotnet test --solution XrealScreen.slnx -p:Platform=x64` | 169 passed, 0 failed [verified-local] |
| `dotnet build src/XrealScreen.App -p:Platform=x64` | Succeeded, 0 warnings [verified-local] |
| Active monitors | `\\.\DISPLAY1` AUO31A6 1920×1200 @ 60 Hz, internal. `\\.\DISPLAY2` BenQ EL2870U 3840×2160 @ 60 Hz, primary, HDMI. Same adapter LUID `0x15522`. [verified-local] |
| Desktop capture | `xrs capture test \\.\DISPLAY2 --border` delivered 121 frames in 4.0 s at 3840×2160, then 67 frames in 2.0 s. The first frame, written with `--png`, is the live desktop and the UI text is sharp. The printed latency is not photon latency: it subtracts WGC `SystemRelativeTime` from a `Stopwatch` timestamp, and the average came out negative. [verified-local] |
| Glasses | TCP `169.254.2.1:52998` did not connect. No active glasses monitor. Inactive PnP entries named "Generic Monitor (One)" were present earlier. IMU and glasses-anchor checks are still open. [verified-local] |
| Virtual display driver | Not installed. `stampinf.exe` and `Inf2Cat.exe` are not on this PC, so the driver package cannot be signed yet. `winget install Microsoft.WindowsWDK.10.0.26100` downloaded and then exited `0x80070642` (installer cancelled). The shell is not elevated. No `bcdedit` change was made. [verified-local] |

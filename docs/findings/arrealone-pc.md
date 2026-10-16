# This PC (ARREALONE)

Checked 2026-10-15. This is not the machine described in `local-environment.md`.

| Item | Result |
|------|--------|
| .NET SDK | 10.0.402, which satisfies `global.json` (`10.0.401`, `latestFeature`) [verified-local] |
| `dotnet test --solution XrealScreen.slnx -p:Platform=x64` | 169 passed, 0 failed [verified-local] |
| `dotnet build src/XrealScreen.App -p:Platform=x64` | Succeeded, 0 warnings [verified-local] |
| Active monitors | `\\.\DISPLAY1` AUO31A6 1920×1200 @ 60 Hz, internal. `\\.\DISPLAY2` BenQ EL2870U 3840×2160 @ 60 Hz, primary, HDMI. Same adapter LUID `0x15522`. [verified-local] |
| Desktop capture | `xrs capture test \\.\DISPLAY2 --border` delivered 121 frames in 4.0 s at 3840×2160, then 67 frames in 2.0 s. The first frame, written with `--png`, is the live desktop and the UI text is sharp. The printed latency is not photon latency: it subtracts WGC `SystemRelativeTime` from a `Stopwatch` timestamp, and the average came out negative. [verified-local] |
| Glasses | 2026-10-16: `xrs probe` found no USB network adapter. Ports 52996–52999 on `169.254.2.1` are closed. No active glasses monitor. Inactive PnP entries named "Generic Monitor (One)" remain. IMU and glasses-anchor checks are still open. [verified-local] |
| Virtual display driver | 2026-10-16: WDK 10.0.26100.6584 installed. Driver built from virtual-display-rs `22fcd2e0`, version 0.4.0.1, signed `CN=XrealScreen Driver (test)`, thumbprint `EAA58EC8BC18D3F426E90AFBB995827886D3A32E`. Installed as `oem186.inf`. Device `ROOT\DISPLAY\0000` status OK. `xrs vdd add 1920x1080` created `\\.\DISPLAY9` (EDID CHY0000) and `xrs vdd clear` removed it and restored the laptop plus BenQ. The virtual monitor's adapter LUID was `0x239070BB`; the real monitors are `0x15522`. No `bcdedit` change. [verified-local] |

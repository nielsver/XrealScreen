# This PC (ARREALONE)

Checked 2026-10-15. This is not the machine described in `local-environment.md`.

| Item | Result |
|------|--------|
| .NET SDK | 10.0.402, which satisfies `global.json` (`10.0.401`, `latestFeature`) [verified-local] |
| `dotnet test --solution XrealScreen.slnx -p:Platform=x64` | 169 passed, 0 failed [verified-local] |
| `dotnet build src/XrealScreen.App -p:Platform=x64` | Succeeded, 0 warnings [verified-local] |
| Active monitors | `\\.\DISPLAY1` AUO31A6 1920×1200 @ 60 Hz, internal, at (-1920, 0). `\\.\DISPLAY2` BenQ EL2870U 3840×2160 @ 60 Hz, primary, HDMI, at (0, 0). `\\.\DISPLAY3` XREAL One, EDID MRG4101, 3840×1080 @ 60 Hz, DisplayPort, at (3840, 0). All three on adapter LUID `0x15522`. [verified-local] |
| Desktop capture | `xrs capture test \\.\DISPLAY2 --border` delivered 121 frames in 4.0 s at 3840×2160, then 67 frames in 2.0 s. The first frame, written with `--png`, is the live desktop and the UI text is sharp. The printed latency is not photon latency: it subtracts WGC `SystemRelativeTime` from a `Stopwatch` timestamp, and the average came out negative. [verified-local] |
| Glasses display | 2026-10-16: XREAL One, USB VID_3318 PID_0438, EDID MRG4101, friendly name One, `\\.\DISPLAY3` 3840×1080 @ 60 Hz. `xrs render --workspace GlassesAnchor --screens 3 --seconds 20` created `\\.\DISPLAY9`–`DISPLAY11` at 1272×716, captured them, and presented 1202 frames at 59.5 fps on `\\.\DISPLAY3` (frame p50 16.67 ms, latch→present 0.68 ms, 277 capture updates). Cleanup removed the virtual monitors and restored the laptop, BenQ, and glasses. [verified-local] |
| Glasses IMU | 2026-10-16, earlier the same day: UsbNcm was up (PC 169.254.2.10, glasses 169.254.2.1). A 3 s probe decoded the IMU stream at about 995 samples/s with no dropped reports. After the glasses-anchor run, `xrs probe` found no 169.254.2.x adapter and ports 52996–52999 were closed. Present PnP devices no longer included the UsbNcm interface. The picture stayed up. CDC ECM stays in error; that is the unused interface. [verified-local] |
| Virtual display driver | 2026-10-16: WDK 10.0.26100.6584 installed. Driver built from virtual-display-rs `22fcd2e0`, version 0.4.0.1, signed `CN=XrealScreen Driver (test)`, thumbprint `EAA58EC8BC18D3F426E90AFBB995827886D3A32E`. Installed as `oem186.inf`. Device `ROOT\DISPLAY\0000` status OK. `xrs vdd add 1920x1080` created `\\.\DISPLAY9` (EDID CHY0000) and `xrs vdd clear` removed it and restored the laptop plus BenQ. The virtual monitor's adapter LUID was `0x239070BB`; the real monitors are `0x15522`. No `bcdedit` change. [verified-local] |

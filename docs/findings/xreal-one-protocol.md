# XREAL One-series protocol (USB network)

Applies to XREAL One / One Pro per community sources and **verified on XREAL 1S** (fw 15.01.03.522, 2026-09-26) for the stream port. Control port 52999 is still unverified.

## Transport
| Fact | Value | Source | Verified? |
|------|-------|--------|-----------|
| Data link | USB NCM network adapter "UsbNcm Host Device" (USB interface MI_01) | one-xr, SamiMitwalli | [verified-hw] 1S fw 15.01.03.522 |
| Glasses IP | `169.254.2.1` | one-xr, SamiMitwalli | [verified-hw] 1S fw 15.01.03.522 |
| PC IP | `169.254.2.10/24` | one-xr | [verified-hw] 1S fw 15.01.03.522 |
| TCP 52996 | metadata: magic `27 31`, 32-byte messages at 90 Hz (timestamp + counter, no pose; see `xreal-eye-camera.md`) | one-xr, Grayscale-Feed port list | port open: [verified-hw] 1S fw 15.01.03.522; rate [verified-hw] 2026-10-29 |
| TCP 52997 | Eye camera: 8-bit grayscale 512×378, 60 fps (Gesture Control on) or 15 fps (off), same framing shape (see `xreal-eye-camera.md`) | Grayscale-Feed port list; own decoding | [verified-hw] 2026-10-29 |
| TCP 50051 | gRPC server (camera control, `frames.service.Frames`; see `xreal-eye-camera.md`) | own scan; SDK method names | [verified-hw] 2026-10-29 |
| TCP 1234, 50052, 50180, 52988–52995 | open, silent on connect; purpose unknown | own scan | [verified-hw] 2026-10-29 |
| TCP 52998 | IMU stream: 1000.0 Hz IMU (dt 0.98–1.02 ms, no gaps) + ~400 Hz magnetometer reports | one-xr, SamiMitwalli | [verified-hw] 1S fw 15.01.03.522 |
| TCP 52999 | control (request/response) | one-xr | port open [verified-hw]; GET software/DSP version [verified-hw] 2026-10-29 (below); other commands [from-source:MIT] only |
| Video | USB-C DP alt mode, monitor EDID `MRG4102` "XREAL 1S" | vendor | [verified-hw] 1S fw 15.01.03.522 |

## Stream framing (port 52998)
Implemented in `src/XrealScreen.Device.XrealOne/OneReportFramer.cs`; ported from one-xr `OneXrReportMessageParser.kt` [from-source:MIT]. Framing and field layout [verified-hw] 1S fw 15.01.03.522: 0 dropped bytes / 0 bad frames over 125 s of recordings.

Header (6 bytes): `magic0` = `0x28` (or `0x27`), `magic1` = `0x36`, then **u32 big-endian** body length = 128.
Body (128 bytes, little-endian):

| Offset | Type | Field |
|--------|------|-------|
| 0x00 | u64 | device id |
| 0x08 | u64 | device timestamp, ns ([verified-hw] 1S fw 15.01.03.522: matches wall clock) |
| 0x18 | u32 | report type: `0x0B` IMU, `0x04` magnetometer |
| 0x1C | 3x f32 | gyro x,y,z - rad/s ([verified-hw] 1S fw 15.01.03.522; bias ≈ -0.003/-0.007/-0.006 at 43 °C) |
| 0x28 | 3x f32 | accel x,y,z - m/s² ([verified-hw] 1S fw 15.01.03.522: \|a\| = 9.80 at rest) |
| 0x34 | 3x f32 | magnetometer x,y,z |
| 0x40 | f32 | temperature °C ([verified-hw] 1S fw 15.01.03.522: ~43 °C) |
| 0x44 | u8 | IMU id |
| 0x45 | 3x u8 | frame id |

**Sensor frame [verified-hw] 1S fw 15.01.03.522: X = right, Y = down, Z = forward, shared by gyro and accel** (derived from a cued rotation recording, `tests/fixtures/1s-rotate-fw15.01.03.522.*`). Turning left gives negative gyro Y; looking up gives positive gyro X; tilting to the left shoulder gives negative gyro Z. We map both sensors with `+z,-x,-y` (`XrealOneImuOptions.SensorToBody`) into the tracker body frame X forward, Y left, Z up. (one-xr's accel remap belongs to its own Euler convention; it is not a frame mismatch.)
One-xr also subtracts a factory gyro bias (temperature-interpolated, read from the device config on the control port); we currently rely on our own still-detection bias estimator.

## Control port framing (port 52999)
Request: u16 BE magic, u32 BE length (= body + 4), u32 BE transaction id with the high bit set, body. GET requests use body `18 00`; a string reply body is the transaction id, then `22 <len> 12 <len> <UTF-8>`. [from-source:MIT] one-xr `XrControlProtocol.kt`; GET version [verified-hw] 2026-10-29.

| Magic | Command | 1S reply 2026-10-29 |
|-------|---------|---------------------|
| `0x271D` | GET software version | `1.9.1.20260914175625` [verified-hw] |
| `0x272D` | GET DSP version | `15.A.01.097_20250430` [verified-hw] |
| `0x2729` | GET ID | serial number (string; keep out of the repo) [verified-hw] |
| `0x271F` | GET config | ~220 KB factory calibration JSON (see `xreal-eye-camera.md`) [verified-hw] |
| `0x2700`–`0x2710` | GET (unknown ids) | no reply at all, service keeps working [verified-hw] 2026-10-31 |
| **`0x2711`** | **GET (unknown id) — do not send** | no reply, and the whole control service stops answering (TCP still accepts; replies never come, connections hang in FIN_WAIT2) until the USB cable is replugged; IMU and camera keep working [verified-hw] 2026-10-31 |

**Unknown ids are not safe to probe:** a GET (`18 00`) on an id that is not a property can still trigger something (0x2711 stops the service), so the control port is not scanned blindly; only ids with a known meaning are sent [verified-hw] 2026-10-31.

XREAL's updater (https://www.xreal.com/ota/) shows the full firmware as `15.1.03.603_20260914_15.A.01.097_20250430_1.9.1.20260914175625` on 2026-10-29 (user): `15.1.03.603_20260914` + DSP + software. The first part is not in any reply above; where it comes from (MCU over HID?) is [hypothesis] — found in M9 E0. The OSD's `15.01.03.522` on 2026-09-26 is probably that first part before an update [hypothesis].

## Control features (one-xr reports) [from-source:MIT], all [hypothesis] for 1S
- `zeroView()` / recalibrate (recenter)
- brightness, dimmer (electrochromic)
- scene / input mode
- key events (see `XREALKeyType` in `xreal-sdk.md`)
- device id / firmware version

## USB IDs
| Model | PIDs (VID `0x3318`) | Source |
|-------|------|--------|
| One Pro | `0x0435`, `0x0436` | XRLinuxDriver [from-GPL:facts-only] |
| One | `0x0437`, `0x0438` | XRLinuxDriver [from-GPL:facts-only] |
| **1S** | `0x043e`, `0x043d` | XRLinuxDriver [from-GPL:facts-only] |
| Air series | HID only (no network) | ar-drivers-rs [from-source:MIT] |

## Related work (not used for code)
- wheaney/XRLinuxDriver — GPL-3.0, facts only.
- MSmithDev/AirAPI_Windows — GPL-3.0, Air HID; facts only.
- ar-drivers-rs — MIT, Air HID; reference for later Air support.

## M1 verification checklist
- [x] NCM adapter present with 1S; PC gets `169.254.2.10`
- [x] TCP 52998 connectable; stream rate measured (1000 Hz)
- [x] Magic, length endianness, header sizes confirmed on bytes
- [x] Gyro rad/s, accel m/s², timestamps ns; sensor axes determined
- [ ] Control port 52999 request format captured (client not written yet)
- [x] Firmware version recorded (15.01.03.522)

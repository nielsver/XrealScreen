# XREAL SDK (com.xreal.xr 3.1.0)

Location inspected: `E:\XREAL SDK\com.xreal.xr.tar\package` (Unity 2021.3 package). Date: 2026-09-26.

## Verdict
**Not usable as a runtime on Windows. Facts only.** (ADR-0006)

## Binaries
- Only Android arm64 native binaries: `libXREALXRPlugin.so`, `libnr_api.so` (inside `nr_api.aar`), etc. [verified-local]
- `Tools~/Windows/trackableImageTools.exe` — offline image-database tool; irrelevant. [verified-local]
- No Windows x64 plugin in this package, but C# has `UNITY_STANDALONE` P/Invoke paths to `"XREALXRPlugin"`. The Windows DLLs ship in the separate experimental package (`xreal-sdk-experimental.md`). [from-sdk:facts-only] 2026-10-29
- No LICENSE / notice files; runtime loads `nrsdk_license.bin`. → Do not redistribute, link, or copy. [verified-local]
- docs.xreal.com offers no Windows/OpenXR SDK.

## P/Invoke surface (names only) [from-sdk:facts-only]
`CreateSession`, `Pause`, `Resume`, `Destroy`, `GetDeviceType`, `GetDeviceCategory`, `GetTrackingType`, `SwitchTrackingType`, `GetDevicePoseFromHead`, `GetDeviceResolution`, `GetDeviceRefreshRate`, `SetGlassesEventCallback`, …
No setters for brightness, dimming, or display mode in this package; the experimental package has them (`xreal-sdk-experimental.md`).

## Enums [from-sdk:facts-only]
`XREALDeviceType` (`Runtime/Scripts/XREALPlugin.cs`):

| Value | Name | Value | Name |
|------:|------|------:|------|
| 0 | INVALID | 12 | ONE_PROL |
| 1 | LIGHT | 13 | XREAL_AURA_M |
| 2 | AIR | 14 | XREAL_AURA_L |
| 3 | AIR2_PRO | **15** | **XREAL_1S** |
| 4 | AIR2 | 1001 | HONOR_AIR |
| 5 | AIR2_ULTRA | 1002 | VIDDA_ONE_PRO |
| 10 | ONE | 1003 | ROG_XREAL_R1_M |
| 11 | ONE_PROM | 1004 | ROG_XREAL_R1_L |

`TrackingType`: MODE_6DOF=0, MODE_3DOF=1, MODE_0DOF=2, MODE_0DOF_STAB=3.
`XREALKeyType`: MULTI=1, INCREASE=2, DECREASE=3, MENU=4.

`XREALActionType` (`XREALCallbackHandler.cs`) — **SDK event codes, not wire command IDs**:

| Code | Meaning |
|------|---------|
| 1–3 | click / double click / long press |
| 4 / 5 | open / close screen |
| 6 / 7 | brightness + / − |
| 8 / 9 | volume + / − |
| 10 / 11 | mono / stereo |
| 12 | NEXT_EC_LEVEL (electrochromic dimming) |
| 13 / 14 | DP / UVC voice |
| 30–37 | sleep level, color calibration, startup state, switch space mode, **TRIGGER_RECENTER=34**, OSD menu, photo, video |
| 1010 | screen status |
| 2000 / 2001 | disconnect / force quit |
| 2003 | system display change |
| 2021–2028 | display state, DP working, key state, proximity/wearing, RGB camera plug, temperature data, power save, temperature state |

## USB IDs [from-sdk:facts-only]
- VID `0x3318`.
- PIDs in `glasses_device_filter.xml`: `0x0423`–`0x0428`, `0x0431`, `0x0432`, `0x0435`–`0x0438`. No model mapping in the file.
- Note: 1S PIDs `0x043d`/`0x043e` (see `xreal-one-protocol.md`) are **not** in this list.

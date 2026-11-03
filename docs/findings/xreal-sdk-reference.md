# XREAL SDK as a reference: how XREAL solved things

Read 2026-10-31: the C#/Unity part of `com.xreal.xr.experimental` 3.1.0 (all ~6,000 lines: runtime, samples, shaders) and the capture code of `com.xreal.xr` 3.1.0 (`Runtime/Scripts/Android/Camera Features`). **No license → facts and ideas only (ADR-0006):** described in our own words, no code copied, no disassembly, nothing of it ships or is loaded. Function and type names are given so the facts can be found again. Everything here is [from-sdk:facts-only] unless tagged otherwise; whether it works the same on the 1S is [hypothesis] until tested.

Native-library facts (DLL strings, AXR messages): `xreal-sdk-experimental.md`.

## 1. Connecting to One-series glasses (Windows)
- Detection is over USB (HID, `libnr_glasses_api`), polled once a second; it returns a glasses type including boot/upgrade modes.
- Before creating a session the SDK **waits until TCP 169.254.2.1:52999 accepts** (up to 5 tries, 1 s each), then **polls a "startup state" every 200 ms until it reports "end"**. Only then does it use the glasses.
- → Explains our 2026-10-31 observation that the control port connects but does not answer for 10–40 s after plugging in [verified-hw]. **Use for us:** the app/CLI should wait for a first good reply (e.g. GET 0x271D) with retries instead of failing after 3 s; this is also the "glasses ready" signal for the E0 "Eye attached" indicator.
- On disconnect or a glasses power-save event the SDK tears the session down and goes back to polling.

## 2. Glasses-side reprojection ("direct present") — idea for App head tracking
- For One-series glasses the SDK renders **side-by-side stereo 3840×1080 at 60 Hz** full-screen on the glasses monitor, with **prediction 75 ms** and an **overfill factor 1.05** (renders 5 % beyond the visible field).
- Every frame it draws **a row of metadata pixels into the top line of the image** (one pixel per 4 bytes of native "frame metadata", colour = the lower 3 bytes). The metadata comes from the native library per frame (`CreateFrame` / `GetFrameMetaData`); its content is not visible in C#.
- Reading: the glasses read the pose the frame was rendered for from that pixel row and **warp the image to the current head pose themselves** (the overfill gives them margin). 75 ms prediction suggests a long render→photon path that the glasses then correct. [hypothesis]
- **Use for us:** this is how App head tracking could get Anchor-quality stability (glasses-side late warp) instead of our PC-side 20 ms prediction (ADR-0009). Blocked on the metadata format (native, unknown); the pixel row only affects the image, so experiments cannot harm the glasses, but blind guessing is unlikely to hit the format. Research item (ROADMAP "Later").
- Glasses monitor is found by its name containing "Air" or "One", or by a 3840×1080 mode; the SDK makes it extended + primary and moves its window there.

## 3. Display modes
- Modes (EDID): 1920×1080 at 60/72/90/120 Hz and 3840×1080 (SBS 3D) at 60/72/90 Hz; 3840×1080 at 120 Hz exists as a value but no sample offers it. Older glasses use a separate "2D/3D mode" list (with obsolete 50/55 Hz entries; 3840 at 90 Hz marked "mac M chip, direct link").
- Switching pattern: set the mode, then **wait for the monitor count to drop, then to come back, then read the mode back** (5 s timeout each); on Android also stop and restart the XR display. → Same shape as our UltraWide auto-restart (ADR-0010).
- Other display controls: UltraWide on/off, brightness level (max from the glasses), colour temperature levels, electrochromic level count/level, screen on/off, HDCP, "physical button may switch display mode" flag, default start resolution.

## 4. Blend Mode (camera + virtual image) — answers E9
- Modes: **Blend**, **CameraOnly**, **VirtualOnly** (no separate side-by-side mode); a separate **capture side**: Single, **Both** (left and right at half size next to each other = our "Side by side"), Left, Right.
- **Camera type RGB or Gray:** blending also works on the **grayscale tracking cameras**, not only the colour camera.
- Method: a capture camera is attached to the head; it is placed at the **physical camera's pose relative to the head** and given the **camera's own projection** (intrinsics). The virtual scene is rendered from that viewpoint over the camera image as background (YUV 4:2:0 converted in a shader for RGB; grayscale used directly), then encoded. Options: hologram opacity, background colour, lock roll, frame rate, default 1280×720.
- If the colour camera cannot start ("busy"), it falls back to VirtualOnly.
- **Use for us:** we already have everything for a **grayscale Blend Mode** — the Eye feed (port 52997) plus the camera intrinsics and camera↔IMU extrinsics from the calibration JSON (GET 0x271F) and our head pose. So Blend Mode does not have to wait for the colour camera route (E9); it can start on the grayscale camera and switch to colour later.

## 5. Distortion map encoding (macOS overlay)
- The display distortion is a grid of (screen x, y → u, v) points; the SDK bakes it into a 1920×1080 RGBA texture with **u in R+G and v in B+A as 16-bit values over the range −0.05…1.05**, and a per-frame warp matrix (pose correction) is applied in the shader. Only relevant if we ever render our own distortion (not needed while the glasses do it).

## 6. Glasses events and keys
- Event types include: single/double/long press, screen on/off, brightness ±, volume ±, mono/stereo, next dimming level, sleep-time level, **startup state**, switch space mode, **recenter**, **OSD main menu**, **take photo**, **take video**, screen status, disconnect, display change, key state, **wear state (proximity)**, colour-camera plugged in/out, **temperature** (forehead/temple), power-save state, temperature state.
- **Use for us:** glasses buttons as app hotkeys (e.g. photo key → Blend Mode photo, recenter key → workspace recenter), pause on take-off. The control port pushes key events as magic 0x272E (one-xr, MIT); whether the others arrive there is [hypothesis].

## 7. Never send these (they exist, so a blind probe can hit them)
Firmware update (OTA: MCU, DP, audio, **camera**, bin, app), **USB function configuration** (can switch NCM/ECM/audio/HID/MTP/mass storage — could cut our network link), factory **activation state / activation time**, customer/box serial numbers, **reboot**, **force sleep**, DisplayPort ESD/level/HDCP, LED modes, IMU frequency divider, log trigger, net log. The 0x2711 hang (`xreal-one-protocol.md`) shows a GET can trigger an action → only ids with a known meaning are ever sent.

## 8. Other facts
- Device codenames: One = "GF", One Pro = "Gina", Air 2 Ultra = "Flora", Air 2 = "P55", Air 2 Pro = "P55E"; the 1S is not in this list (newer). Upgrade mode identifies as "Artosyn … Uboot HID" (Artosyn chip).
- Tracking types: 0DoF, 0DoF stabilised, 3DoF, 6DoF (6DoF on the PC uses the grayscale cameras).
- IPD can be set by the app (metres); proximity sensor near/far thresholds; power save with sleep levels 30 s / 5 / 10 / 20 / 60 min / never.
- `GetGlassesSystemVersion` is what XREAL's own sample shows as "glass version" → likely the `15.1.03.603` firmware part (E0 open item) [hypothesis].

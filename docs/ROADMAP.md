# Roadmap

Risk-first. Each milestone ends with its exit criteria checked and `docs/STATUS.md` updated.

## M0 Bootstrap
Deliverables: solution skeleton, `global.json`, central package management, docs + ADRs + findings, subagents, skills, CLAUDE.md/AGENTS.md, GitHub Actions build+test.
- [x] `dotnet build` and `dotnet test` green locally (CI workflow added; first run on push)
- [x] WinUI 3 shell with Mica + NavigationView launches (2026-09-26)

## M1 Protocol spike (needs glasses)
Deliverables: `xrs` CLI (`probe`, `imu record/decode/live/replay`) - **done**; stream framer - **done**; raw capture `.xrcap` (replaces a separate Sniffer) - **done**; control-port client (recenter etc.) - moved to M4 (native mode needs it); real fixtures - **done**.
- [x] 1S NCM adapter + TCP 52998 confirmed (2026-09-26, fw 15.01.03.522)
- [x] IMU rate and units verified; sensor axes determined
- [x] Real fixtures committed (`tests/fixtures/*fw15.01.03.522*`) with regression tests

## M2 Display / VDD spike
Deliverables: CCD enumeration - **done**; EDID glasses identification - **done**; virtual monitor add/remove via virtual-display-rs (ADR-0008) - **done**; layout snapshot/restore (positions + resolutions, `%LOCALAPPDATA%\XrealScreen\topology-snapshot.json`) - **done**; borderless WGC capture - **done** (`XrealScreen.Render`, `xrs capture test`).
- [x] Virtual monitors at 1920×1080 (60/120 Hz), 2560×1080, 3840×1080, 1920×2160 (2026-09-26)
- [x] Monitors created, captured and removed cleanly; topology restored (2026-09-26)
- [x] Driver needs no admin at runtime; pipe protocol documented from source and verified

## M3 Render spike — done 2026-09-26
Deliverables: fullscreen swapchain on glasses output, captured monitor on pose-driven quad, latency stats.
- [x] 120 fps sustained (119.9 fps over 5 min, vblank-paced, ADR-0009)
- [x] < 20 ms pose→present (latch→present ≈ 0.2 ms; true motion-to-photon not measured yet)
- [x] No visible drift over 5 min (user, glasses test 2026-09-26)
- [x] Mouse usable on the virtual monitors (desktop order desk → virtual → glasses)

## M4 Native mode MVP — superseded by the glasses-anchor workspace (ADR-0010), done 2026-09-27
- [x] Glasses keep the image still (OSD Anchor); app splits the UltraWide image into 1–3 pixel-exact screens
- [x] Gap + aspect-preserving fill; preview; Apply button; auto-restart on UltraWide change
- [ ] Control port 52999 (glasses-side recenter / UltraWide switching from the app) — optional

## M5 Virtual workspace MVP — mostly done 2026-09-27
- [x] App: Start/Stop/Recenter, screen count, ultrawide, layout, distance (live), stabilizer presets, tracking axes, neck model
- [x] Settings persisted (`%LOCALAPPDATA%\XrealScreen\settings.json`)
- [x] Mouse usable (desktop order desk → virtual → glasses)
- [x] Comfort: Full mode with less lag — late latch, 20 ms prediction, gyro-speed stabilizer, nod rate gate (ADR-0009/0007 updates)
- [ ] Remove the DWM frame from the glasses path (ADR-0009 follow-up)

## M6 Background & service (in progress: crash-safe restore done)
Deliverables: tray, StartupTask, helper service + ACL'd pipe, crash-safe topology restore, sleep/resume + hot-plug state machines.
- [ ] Kill app mid-session → topology restored
- [ ] Sleep/resume and unplug/replug recover without restart

## M7 Packaging (phases 1 + 2 done 2026-09-27)
- [x] App icon + MSIX logos (`tools/branding/Make-Icons.ps1`)
- [x] Signed self-contained MSIX + install/uninstall scripts (`installer/`, ADR-0005 update)
- [x] Phase 2: build + sign virtual-display-rs 0.4 ourselves; installer installs it (`installer/driver/Build-Driver.ps1`, `Install-Driver.ps1`, ADR-0005)
- [x] MSI with FalkForge: unpackaged self-contained app, `xrs driver` installs the driver (`installer\Build-Msi.ps1`, ADR-0013) — install test on a PC pending
- [ ] Trusted signing certificate (or Store) for a public release; driver via EV + attestation signing; license list of the driver's Rust crates
- [ ] Clean-VM install test

## M8 Hardening
Deliverables: HDR tone-map, DPI, hybrid GPU, One/One Pro regression, accessibility, v1.0.
- [ ] Hybrid-GPU laptop works (VDD + glasses on same adapter)
- [ ] Accessibility pass
- [ ] v1.0 tagged

## M9 XREAL Eye camera & hand control (started 2026-10-29, ADR-0016)
Facts: `docs/findings/xreal-eye-camera.md`. Order: E0 → E1 → E2 → E3 → E4 (user priority: keyboard), then E10 (60 fps, user: must be investigated), then E5–E9. Every camera feature is off by default.

**Token log** (user request 2026-10-29): after each step, measure with `tools/Get-ClaudeTokenUsage.ps1 -Since <step start> -Until <step end>` and add a row. Cache read = earlier conversation re-read from the prompt cache (cheap); Input + Cache write = new input.

| Step | Date | Requests | Input + cache write | Cache read | Output |
|------|------|---------:|--------------------:|-----------:|-------:|
| Investigation + hand tests (before the plan) | 2026-10-29 | 45 | 94,977 | 3,957,178 | 42,598 |
| Plan: M9, ADR-0016, finding | 2026-10-29 | 19 | 34,195 | 2,669,863 | 19,255 |
| Colour camera research, E9 | 2026-10-29 | 22 | 42,284 | 3,842,015 | 20,127 |
| Firmware read over port 52999 | 2026-10-29 | 15 | 18,579 | 3,107,941 | 8,760 |
| Keyboard languages, firmware panel, calibration facts, token log | 2026-10-29 | 14 | 21,425 | 3,179,700 | 16,124 |
| E0 camera feed + glasses info (code, tests, CLI, app card, 5-min feed check) | 2026-10-29 | 40 | 80,070 | 11,709,060 | 48,082 |
| E0 frame-rate cause (Gesture Control), 15 fps decision, E10 plan (incl. context summary) | 2026-10-29 | 18 | 65,524 | 2,269,480 | 15,840 |
| XREAL SDK experimental package facts, RGB Blend Mode plan, handoff | 2026-10-29 | 20 | 44,577 | 2,208,479 | 14,855 |
| E10 read-only probe (control scan stopped at 0x2711, listen-only 52996/camera with Gesture Control on/off), SDK AXR facts | 2026-10-31 | 72 | 187,686 | 12,869,378 | 36,545 |
| XREAL SDK C#/Unity read as reference + E2 hand tracking (prototype, engine, tests, CLI, 3 live pinch tests) | 2026-10-31 | 103 | 227,536 | 35,296,450 | 119,671 |
| E3 hand control in xrs (engine, calibration, dwell, precision zone, 8 spoken target tests) | 2026-11-01 | 147 | 827,618 | 43,938,484 | 153,084 |

**E0 Camera feed foundation**
Built (2026-10-29, `feature/eye-camera-e0`): `EyeFrameFramer`, pooled `EyeFrame`, `XrealEyeCameraSource` (reconnect is left to the caller, like `XrealOneImuSource`) and `.xrcam` (`XrcamWriter`/`XrcamReader`) in `Device.XrealOne`; `xrs camera snapshot | record | live`; tests on synthetic frames (no real room images in the repo); **glasses info**: `ControlWire` + `XrealControlClient` (port 52999, read-only GETs) + `GlassesInfo`/`GlassesCalibration` → `xrs device info` and a **Glasses** card on the app's Device page (software + DSP version, serial number, cameras with field of view, calibration date) — **no update check**.
Open: the `15.1.03.603` part of the firmware (not on the control port; XREAL's sample shows `GetGlassesSystemVersion` — HID?); wait for "glasses ready" (control port answers, 10–40 s after plug-in) with retries instead of failing after 3 s, as XREAL's SDK does (`xreal-sdk-reference.md` §1) and an "Eye attached" indicator that does not read the camera.
- [x] Firmware shown in `xrs device info` matches https://www.xreal.com/ota/ for the parts we can read (software + DSP, 2026-10-29); app card built, not yet seen by the user
- [x] `xrs camera live` receives every frame the glasses send — 2026-10-29: 0 dropped frames over 5 min at 15 fps (Gesture Control off) and over 127 s at 60 fps (Gesture Control on); the rate follows the glasses' Gesture Control switch, not light. Decided (user): E2–E4 at 15 fps with Gesture Control off; 60 fps without it is E10
- [x] Framer tests: resync after garbage, byte-by-byte and split TCP reads, wrong length, stride < width (`EyeFrameFramerTests`); control wire + fake-server client tests (`ControlPortTests`); `.xrcam` round trip (`XrcamFileTests`)

**E1 Metadata port 52996 (glasses' own 6DoF?)**
2026-10-31 (Eye attached, Anchor off): 32-byte messages at 90 Hz, only a timestamp and a counter, no pose; same with Gesture Control on/off. Still to check: OSD Anchor on, head moved.
Deliverables: `xrs probe metadata --record`; recordings with Eye on/off, OSD Spatial Anchor on/off, head moved forward/back 20 cm; finding updated.
- [ ] Content of 52996 described, position present or ruled out

**E2 Hand tracking engine**
Built (2026-10-31, `feature/hand-tracking-e2`): `tools/Convert-HandModels.ps1` → `artifacts/models/hands`; `src/XrealScreen.Vision` (`HandTracker`, `PalmDecoder`, `HandRoi`, `ImageOps`, `OneEuroFilter`, `PinchDetector`; 26 tests); `xrs hands eval <xrcam> [--step 2] [--phases]` and `xrs hands live [--window]`. Measured on the 2026-10-29 recording (converted to .xrcam, kept private) at 15 fps — same as MediaPipe on the same frames: still 100 %, pinch 97.8 % (MediaPipe 98.3), near/far, low, look down 100 %, pointing 47 % (MediaPipe 42); 9.2 ms median / 11.9 ms p95 per frame (blur 1.9, landmarks 7.3, palm detector 8.4 when run).
Deliverables: `tools/Convert-HandModels.ps1` (MediaPipe hand landmarker → ONNX); `HandTracker` (blur → palm detection → landmarks → One Euro filter) on ONNX Runtime; `PinchDetector` (hysteresis 0.35/0.6 + minimum hold); landmark fixtures; `xrs hands live` (preview window with skeleton).
- [x] Same hand-found rates as the Python test (≥ 95 % raised/pinch) on a recorded `.xrcam` — still 100 %, pinch 97.8 % at 15 fps (2026-10-31)
- [ ] ≤ 15 ms per frame at 30 fps on the dev PC; latency camera → landmarks measured — time per frame met (9.2 ms median, 11.9 ms p95, Debug build); end-to-end latency still to measure live
- [x] Pinch and hand-found rates measured on a 15 fps recording (Gesture Control off), not only on 60 fps — `--step 2` of the 30 fps recording (2026-10-31)
- [x] Pinch detector: ≥ 95 % of pinches, ≤ 1 false click per minute with the hand idle in view — live test with the user 2026-10-31 (spoken count, `xrs hands live --log`): 11 of 11 pinches, 0 false clicks in an idle minute with close 0.25 / open 0.45 / 150 ms release delay (replayed from the frame log; the first live runs showed press–release chatter and a relaxed open hand at only 0.4–0.55, which the old 0.35 / 0.6 handled badly). One user, small sample: recheck in E3
- [ ] Hand lost for ~1 s during fast free movement (found 62–82 % in the idle minute): improve in E3 (e.g. run the palm detector while tracking is weak, keep the region a few frames)

**E3 Hand control (pointer + click)**
Built (2026-11-01, `feature/hand-tracking-e2`): `HandCalibration` (affine, 5-point least squares; rejects error > 0.1 or a screen corner within 25 px of the camera edge), `HandPointer` (index-base anchor, One Euro 0.5 Hz, precision zone 30 % gain below 0.15 screen/s with catch-up and ≤ 0.05 lag, click = pinch or dwell 1.2 s, 1 s repeat guard, pinch ignored while the hand moves > 0.2 screen/s, thumb+middle scroll), `Host.Hands.HandControlEngine` (camera reconnect, pause on real input, `SendInputPointer`), `HandCalibrationStore`; `xrs hands control | targets [--calibrate] [--click] [--wait-go] [--log]` with a target window (camera inset, skeleton, pinch meter, dwell ring, wait hints, global Esc). Calibration ignores pinches (dwell only).
Target tests with the user 2026-11-01 (all 10/10 hit, 40 px, 1920×1200 glasses monitor): misses 27 → 5 → 23 → 19 → 7 → 21 per run; median 4.7–20 s per target. Causes found: thumb hidden when aiming (pinch ratio of a relaxed hand 0.19–0.37, i.e. near "closed"), stray pinches while moving, dwell firing while adjusting beside the target, pointer jitter ±26 px at ~8 screen px per camera px, hand found only 40–85 % in the evening run (camera mean 65 vs 76 in the morning). The moving-pinch filter (last fix) is not yet tested on the glasses.
Deliverables: `HandControlEngine` (SendInput like head look); 5-point calibration; pinch = click, pinch-hold = drag, pinch-drag vertical = scroll; pauses on real mouse/keyboard input and after 3 s without a hand; Ctrl+Alt+J; app **Hands** page (on/off, calibrate, sensitivity, left/right hand, preview) + tray item; camera-in-use indicator; warning to keep the glasses' Gesture Control off.
- [ ] User can click 10 targets of 40 px on the glasses display, ≤ 1 miss — at 15 fps (Gesture Control off); if this fails, E10 (60 fps) moves before E4 — not met yet (best run 7 misses, 2026-11-01)
- [ ] Off = camera socket closed, 0 % CPU

**E4 Virtual keyboard**
Deliverables: phase a — open `osk.exe` on the glasses display from the Hands page; phase b — own no-activate overlay keyboard (large keys, hover highlight, pinch press, key repeat, Shift/AltGr/symbols, click sound), Ctrl+Alt+K to show/hide; **layouts as data files** (`Keyboards/<culture>.json`, schema + loader + tests) with **Swedish (`sv-SE`) and English (`en-US`) first**, globe key + setting to switch, default from the Windows input language; Unicode input so å ä ö work under any Windows layout; docs on adding a language.
- [ ] User types a 20-character sentence with ≤ 2 errors (target ≥ 5 words/min) — in Swedish (with å ä ö) and in English
- [ ] Focus stays in the target app while typing
- [ ] A third layout can be added by dropping in a JSON file (tested with a test layout)

**E5 Passthrough peek**
Deliverables: hotkey/tray shows the camera feed as a panel (top-most window on the glasses in Glasses anchor; texture in the scene in App head tracking), optional hand skeleton.
- [ ] Peek opens in < 200 ms and shows the keyboard/desk when looking down

**E6 Camera drift correction (App head tracking)**
Deliverables: visual yaw estimate from feature tracking fused with the gyro (ADR-0007 update); A/B in the glasses.
- [ ] Yaw drift over 10 min reduced vs gyro-only (measured)

**E7 Lean tracking / 6DoF for workspace and games**
Deliverables: position from 52996 if E1 found it, otherwise visual-inertial odometry research; lean-in parallax in App head tracking; opentrack UDP output on the Games page (ADR-0011 update).
- [ ] Lean 10 cm toward a screen → visible parallax; works in one sim via opentrack

**E8 SDK, local API, docs, privacy**
Deliverables: camera frames + hand events in `XrealScreen.Sdk` and the local API (opt-in, ADR-0012 update); docs/sdk pages + sample; website privacy policy and FAQ (EN + SV); Store notes; legal-security-reviewer pass before merge.
- [ ] Privacy policy states what the camera is used for and that nothing leaves the PC

**E9 Colour (RGB) camera — research, low priority, not for hand tracking**
The Eye's colour camera has no webcam interface; XREAL's SDK starts it with RPCs to the glasses' gRPC server (`docs/findings/xreal-eye-camera.md`). Hand tracking stays on the grayscale feed (ADR-0016).
Deliverables: decide the legal route first (black-box probing of our glasses vs. reading message definitions in the unlicensed SDK — legal-security-reviewer, ADR-0006); then `xrs camera rgb` (start, receive, decode, save a PNG); then uses in this order: colour peek (E5), **RGB camera Blend Mode** (user, 2026-10-29), first-person virtual webcam (Windows 11 virtual camera for Teams/Zoom/OBS), text/QR reading (Windows OCR), photo key.
**Blend Mode** — XREAL's SDK also blends on the **grayscale** cameras (`xreal-sdk-reference.md` §4), so a grayscale Blend Mode can be built before the colour route is solved (Eye feed + calibration intrinsics/extrinsics + head pose); colour replaces the background later. Blend Mode = mixed-reality output that combines the camera image with what the glasses show (our virtual screens), like XREAL's capture blend modes: **Blend** (screens drawn over the camera image, aligned with the camera calibration and head pose), **Camera only**, **Virtual only**, **Side by side**. Used for photos, video recording and the virtual webcam; picked in the app; off by default like every camera feature.
- [ ] Legal route decided and recorded
- [ ] One colour frame received on the PC and saved, resolution/format/rate recorded
- [ ] Colour peek or virtual webcam works with the user
- [ ] Blend Mode: a photo and a 30 s video in Blend, Camera only, Virtual only and Side by side; in Blend the virtual screens line up with where they appear in the glasses (checked by the user)

**E10 60 fps camera without the glasses' Gesture Control — research (user: must be investigated)**
The glasses send 15 fps while Gesture Control is off and 60 fps while it is on (`docs/findings/xreal-eye-camera.md`). Goal: 60 fps with Gesture Control off, so hand control is smooth and the glasses never react to the same pinch.
Deliverables: read-only first — **passive** comparison with Gesture Control on and off: the metadata stream (52996, with E1), the IMU/camera headers and the known control-port properties; check one-xr (MIT) for a matching command; record findings. A blind GET scan of the control port is **not** safe: GET on the unknown id 0x2711 stops the control service until a replug (2026-10-31, `xreal-one-protocol.md`), so only ids with a known meaning are sent. Lead (2026-10-29/31, `docs/findings/xreal-sdk-experimental.md`): XREAL's Windows SDK starts the grayscale camera by an RPC (`NRGrayscaleCameraStart`, no frame-rate setter) and has an "AXR" request family that most likely travels over the glasses' gRPC `Frames/StartStreaming` (port 50051), incl. a sensor stream request **with parameters** and a glasses CPU frequency mode. Field layouts are not readable without disassembly, and the same channel carries firmware writes → no guessing; learn the bytes by watching an XREAL program (route b/c, user approval + legal-security-reviewer first). Only then, with the user's approval and a recorded way back, try **one** SET that restores the original value after the test (first write to the glasses: legal-security-reviewer and ADR-0006 route decided first, no XREAL SDK code). If it works: `XrealEyeCameraSource` asks for 60 fps while hand control is on and restores the old value when it stops (ADR-0016 update).
- [ ] The setting behind the frame rate is found, or ruled out for the ports we can reach
- [ ] If found: 60 fps for 5 min with Gesture Control off, 0 dropped frames, and the glasses' own pinch does nothing
- [ ] The glasses are back to their original setting after the test and after an app crash (verified by reading it back)

## Later
- Glasses-side reprojection for App head tracking: XREAL renders SBS 3840×1080@60 with 75 ms prediction and 5 % overfill and writes per-frame pose metadata into the top pixel row so the glasses warp to the latest pose (`xreal-sdk-reference.md` §2); needs the metadata format
- Glasses buttons as app hotkeys (photo, recenter, menu events) and pause on take-off (wear sensor) (`xreal-sdk-reference.md` §6)
- Own IddCx driver (WDK + EV cert + attestation signing)
- Air-series HID support
- SBS stereo
- Pin virtual screens next to the real monitor (detect it in the Eye feed)
- Hand gestures beyond pinch (palm menu, swipe between screens)

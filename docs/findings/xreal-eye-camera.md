# XREAL Eye camera

The Eye is XREAL's clip-on camera for the One series. We read its tracking-camera feed over the glasses' USB network and ran hand tracking on it. Plan: ROADMAP M9, ADR-0016.

Hardware runs on 2026-10-29 used our XREAL 1S (PID 0x043E, USB REV 0409) with the Eye attached, software **1.9.1.20260914175625**, DSP 15.A.01.097_20250430 (read over control port 52999, see `xreal-one-protocol.md`).

## Hardware (vendor docs)
| Fact | Value | Tag |
|------|-------|-----|
| Photo camera | 12 MP, f/2.25; photos up to 2016×1512 JPG, video 1600×1200 MP4 30/60 fps, recorded by the glasses (buttons) | vendor spec, [hypothesis] for PC access |
| Supported glasses | "One and One Pro series"; works on our 1S | [verified-hw] 2026-10-29 |
| 6DoF | "Native 6DoF" computed in the glasses' X1 chip | vendor spec, [hypothesis] whether the pose reaches the PC |
| XREAL SDK camera API | Unity/Android only (YUV_420_888 frames); not usable on Windows (ADR-0006) | [from-sdk:facts-only] |

## Feed on TCP 52997
| Fact | Value | Tag |
|------|-------|-----|
| Availability | Streams as soon as a client connects; no Nebula beta, no extra OSD setting needed on the 1S | [verified-hw] 2026-10-29 |
| Rate | ~11.6 MB/s, 60 fps (device timestamps 16.68 ms apart), 0 gaps over 127 s | [verified-hw] 2026-10-29 |
| Rate and Gesture Control | **OFF → 15.0 fps** (66.7 ms, ~2.9 MB/s), 0 gaps; light is not the cause (15 fps at mean brightness ~45 and ~70). **ON is not reliable:** 60 fps on 2026-10-29, but on 2026-10-31 the feed stopped (0 frames, port still open) within ~5 s of switching it on, twice; once it came back at 15 fps after switching off, once it stayed at 0 after switching off (IMU and control port fine). Gesture Control ON is no fallback for 60 fps | [verified-hw] 2026-10-29, 2026-10-31 |
| Framing | magic `27 48`, then **u32 big-endian** body length (193,856) — same shape as the IMU port | [verified-hw] 2026-10-29 |
| Body | 320-byte header + one **8-bit grayscale 512×378** image (row stride 512) | [verified-hw] 2026-10-29 |
| Header u64 LE @17 | device timestamp, ns (no separate frame counter found) | [verified-hw] 2026-10-29 |
| Header @5 / @9 / @14 | 504 (usable width, matches calibration), 378 (height), 2 (unknown) | [verified-hw] 2026-10-29 for 504/378; @14 [hypothesis] |
| Cameras | one view per frame; no left/right alternation seen | [verified-hw] 2026-10-29 |
| Image noise | pixel-level checkerboard noise; must be smoothed (3×3 Gaussian) before ML models | [verified-hw] 2026-10-29 |
| Other glasses | One Pro + Nebula beta reportedly send 4-bit stereo 512×378 and need OSD Spatial Anchor (Aloim, research-only) | [hypothesis] for us |

## Metadata on TCP 52996
| Fact | Value | Tag |
|------|-------|-----|
| Rate with Eye attached | ~3.4 KB/s, magic `27 31` | [verified-hw] 2026-10-29 |
| Content | 32-byte messages at **90 Hz** (11.11 ms): u64 LE device timestamp ns at offset 8, u32 LE counter at 16 (keeps counting across connections), all other bytes 0. Probably display timing; **no 6DoF pose** | [verified-hw] 2026-10-31 |
| Gesture Control on vs off | same layout and rate; no byte differs between the two states | [verified-hw] 2026-10-31 |

## Colour (RGB) camera
| Fact | Value | Tag |
|------|-------|-----|
| Webcam | no USB video (UVC) interface; glasses expose HID ×2, NCM network, CDC ECM (no driver), audio | [verified-hw] 2026-10-29 |
| More TCP ports | full scan: 1234, 50051, 50052, 50180, 52988–52999 open; all silent on connect except 50051, 52996–52998 | [verified-hw] 2026-10-29 |
| Port 50051 | gRPC server (HTTP/2 SETTINGS on connect); server reflection not enabled (UNIMPLEMENTED) | [verified-hw] 2026-10-29 |
| SDK camera control | the SDK's native library starts both cameras with RPCs: `NRRgbCameraCreate`, `…InitSetImageResolution`, `…InitSetPixelFormat`, `…InitSetCompression`, `…InitSetExposureTime`, `…InitSetGain`, `…Stop`; same set for `NRGrayscaleCamera…`; frames are YUV 4:2:0 | [from-sdk:facts-only] |
| `/frames.service.Frames/StartStreaming` | method name in the SDK; on 50051 it exists (an unknown method returns UNIMPLEMENTED); an empty request gets one reply `10 04` (field 2 = 4, probably an error code) and the stream ends | [verified-hw] 2026-10-29; meaning [hypothesis] |
| Request format | unknown — needed before a colour frame can be received (ROADMAP M9 E9) | [hypothesis] |
| Live resolution | SDK: Low = ½, High = 1.5× the device's standard resolution; its recording example uses 1280×720 @ 30 fps | [from-sdk:facts-only] |

## Factory calibration (control port GET config `0x271F`)
A ~220 KB JSON with keys `FSN` (serial — keep out of the repo), `IMU`, `RGB_camera`, `SLAM_camera`, `display`, `display_distortion`, `glasses_version` (7), `last_modified_time`. [verified-hw] 2026-10-29

| Fact | Value | Tag |
|------|-------|-----|
| `RGB_camera` | 1 camera, 2016×1512, radial model, fx≈fy≈960, cx/cy≈1007/759 | [verified-hw] 2026-10-29 |
| `SLAM_camera` | 1 camera, **504×378**, fx≈fy≈240, cx/cy≈252/190, same distortion coefficients | [verified-hw] 2026-10-29 |
| Same sensor? | SLAM = RGB scaled by ¼ (resolution, focal length, centre, distortion all match) → the grayscale feed is the colour camera binned | [hypothesis] |
| Field of view | from the intrinsics ≈ 93° horizontal × 76° vertical (both cameras) | [verified-hw] calibration, computed |
| Usable width | 504 of the 512-byte rows in the 52997 feed (header @5 = 504 confirmed by calibration) | [verified-hw] 2026-10-29 |
| Camera ↔ IMU | `imu_p_cam` ≈ (−25, 14, 4) mm and `imu_q_cam` (JPL quaternion, ≈ identity) — usable for drift correction / lean tracking | [verified-hw] 2026-10-29 |

## Hand tracking test (2026-10-29)
MediaPipe Hand Landmarker (float16 model, Apache-2.0) in Python on the recorded feed (30 fps kept), 3×3 blur first. User wore the glasses and followed spoken steps. [verified-hw] 2026-10-29

| Pose | Hand found | Note |
|------|-----------|------|
| Raised, still (~40 cm) | 100 % | fingertip jitter 1.2 px/frame (512 px wide image) |
| Pointing, drawing a square | 43 % | side-on hand is the weak case |
| Pinch once per second | 96 % | pinch ratio \|thumb tip − index tip\| / \|wrist − middle MCP\|: 0.1–0.2 closed, ~1.0 open |
| Closer / further | 100 % | hand size 57–89 px |
| Typing height, looking ahead | 100 % | hands at desk height are in view |
| Looking down at the hand | 100 % | |

- Without the blur: 0 % found (score 0.97 with it, same frame).
- Inference 10–19 ms per frame on the dev PC CPU [verified-local].

## Glasses' own Gesture Control
| Fact | Value | Tag |
|------|-------|-----|
| Feature | firmware gestures with the Eye: hand moves a cursor, pinch (≥ ~300 ms, < ~2.2 cm) clicks, wrist flip turns pages, palm-back opens a menu | vendor docs, [hypothesis] on Windows |
| Switch | OSD: double-tap X → Additional (Other) → Lab → Gesture Controls → Gesture Control; **off by default**; can be bound to a shortcut key | vendor docs |
| Conflicts | mutually exclusive with 3D mode; would double-click together with our hand control → keep it off | vendor docs |
| Feed with it off | the 52997 feed works with Gesture Control OFF (OSD checked by the user), but at 15 fps instead of 60 | [verified-hw] 2026-10-29 |

# ADR-0015: Clear AR HUD

- **Status:** Accepted
- **Date:** 2026-11-03

## Context
The XREAL One series is optical see-through. The micro-OLED adds light, so a black pixel leaves the room visible. The app already clears the swapchain to black and then draws captured monitors, which is the virtual desktop. The glasses measure rotation only (3DoF). They have no built-in world camera; the XREAL Eye is a separate clip-on and its picture format is not decoded. Person recognition, object recognition, and scanning need a camera and are out of scope.

The user wants a mode that is not the desktop: a fully clear view with a small head-tracked overlay.

## Decision
A clear HUD is its own session (`ArHudEngine`), separate from the workspace. It connects the existing One-series IMU, runs the existing head tracker, and presents a black frame with an instrument overlay (reticle, horizon, pitch ladder, heading tape, clock). It does not create virtual monitors, capture the desktop, or change display topology. The workspace and the HUD both need the glasses output, so starting one while the other is running is refused. Heading zero is the last recenter, not magnetic north. The glasses OSD must be Follow, Stabilizer off, UltraWide off; the app does not switch the OSD (control port 52999 is still unimplemented).

## Consequences
- `GlassesPresenter` draws an `IGlassesScene`. The workspace scene and the new HUD scene both implement it.
- Layout math (`HudLayout`) is pure and tested without a GPU. A WARP test checks that the frame is black except for the strokes.
- Brightness and which instruments are shown are app preferences in `app.json`.
- World-locked panels, the Eye camera, and recognition stay later work. They need head position or a picture of the room.

## Alternatives
- Draw the HUD inside the workspace scene, behind the monitors — the desktop would still be there, which is the thing this mode exists to leave out.
- A PC webcam for labels — the webcam is not the glasses' viewpoint, and this slice has no recognition.
- Decode TCP 52997 now — there is no picture format yet, and the One in use has no Eye module.

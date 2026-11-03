# ADR-0017: Clear AR HUD

- **Status:** Accepted. The "does not capture the desktop" sentence is superseded by ADR-0019.
- **Date:** 2026-11-03

## Context
The XREAL One series is optical see-through. The micro-OLED adds light, so a black pixel leaves the room visible. The app already clears the swapchain to black and then draws captured monitors, which is the virtual desktop. The glasses measure rotation only (3DoF). They have no built-in world camera. The XREAL Eye is a separate clip-on; its grayscale feed is decoded in ADR-0016 and is not used by the HUD. Person recognition, object recognition, and scanning stay out of this mode.

The user wants a mode that is not the desktop: a fully clear view with a small head-tracked overlay.

## Decision
A clear HUD is its own session (`ArHudEngine`), separate from the workspace. It connects the existing One-series IMU, runs the existing head tracker, and presents a black frame with an instrument overlay (reticle, horizon, pitch ladder, heading tape, clock). It does not create virtual monitors, capture the desktop, or change display topology. The workspace and the HUD both need the glasses output, so starting one while the other is running is refused. Heading zero is the last recenter, not magnetic north. The glasses OSD must be Follow, Stabilizer off, UltraWide off. The HUD does not change that OSD.

## Consequences
- `GlassesPresenter` draws an `IGlassesScene`. The workspace scene and the new HUD scene both implement it.
- Layout math (`HudLayout`) is pure and tested without a GPU. A WARP test checks that the frame is black except for the strokes.
- Brightness and which instruments are shown are app preferences in `app.json`.
- World-locked panels and recognition stay later work. They need head position or a picture of the room. The Eye feed is a separate mode (ADR-0016).

## Alternatives
- Draw the HUD inside the workspace scene, behind the monitors — the desktop would still be there, which is the thing this mode exists to leave out.
- A PC webcam for labels — the webcam is not the glasses' viewpoint, and this slice has no recognition.
- Draw the Eye feed behind the instruments — that covers the room with a camera picture. The HUD stays optically clear, and the Eye is optional.

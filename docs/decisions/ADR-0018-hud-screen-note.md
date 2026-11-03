# ADR-0018: The clear HUD can read the main monitor

- **Status:** Superseded by ADR-0019
- **Date:** 2026-11-03

## Context
ADR-0017 keeps the glasses optically clear and does not capture the desktop. The wearer still wants to glance at the PC screen and get a short note in the glasses. The glasses have no world camera. The main monitor is a normal Windows display, and the app can already capture one.

## Decision
A fifth glance mark, further up, captures the primary monitor once. The glasses display and virtual monitors are skipped. Windows OCR reads the text on the machine. When `XAI_API_KEY` is set, only that text is sent to the xAI Responses API (`grok-4.7`, `store: false`). The answer is a small card under the reticle for 15 seconds. Without a key, the card shows the recognized text. The rest of the frame stays black.

## Consequences
- The HUD still does not create virtual monitors or change topology, and it still refuses to run beside a workspace.
- Screen text leaves the PC only when the key is set, and the request asks the service not to store it.
- A sentence needs a real font. The card is rasterized with Segoe UI and drawn as one texture. The instrument strokes stay on the bitmap font.
- The Eye camera is unchanged and is not a source for this note.

## Alternatives
- Send the screenshot to a vision model. That uploads the whole screen. Text is enough for this step.
- Leave a caption up all the time. That covers the room and reads the screen continuously.

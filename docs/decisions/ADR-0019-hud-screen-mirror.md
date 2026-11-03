# ADR-0019: The HUD shows the main monitor and asks Grok

- **Status:** Accepted
- **Date:** 2026-11-03

## Context
ADR-0018 reads the main monitor once and draws a small card on a black frame. On the glasses that still looks like the flight HUD. The wearer wants the monitor itself in the glasses, and live suggestions from the installed Grok CLI about everything on that screen. The glasses have no world camera. The HUD and a workspace cannot share the glasses window.

## Decision
While the HUD runs, the app copies the primary monitor (never the glasses, never a virtual monitor) about four times a second and draws it in the glasses, aspect kept, flight marks and glance pins on top. About every ten seconds, and again when the READ mark is held, the latest frame is handed to the local `grok` executable. The reply stays on the screen until the next one. The screenshot is not written to the log.

## Consequences
- The picture leaves the PC through the logged-in Grok CLI. There is no separate API-key request and no `store: false` flag on this path.
- A missing CLI, a signed-out CLI, or a slow reply shows a short fixed line. The monitor picture stays up.
- Black bars, when the monitor and the glasses aspect differ, stay optically clear. UltraWide Off keeps the picture on one panel.
- The one-shot OCR card from ADR-0018 is no longer what the HUD shows.

## Alternatives
- Keep the black frame and a text card. That is what the wearer rejected.
- Run the workspace mirror and the HUD together. Both need the one glasses window.
- Send the screenshot to the Responses API from the app. The wearer asked for the Grok CLI.

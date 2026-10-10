# ADR-0011: Head look for games via relative mouse input (no driver)

- **Status:** Accepted
- **Date:** 2026-10-10

## Context
The user wants to steer the camera and character in games such as Skyrim with head movement from the glasses ("head as a joystick"). Skyrim has no head-tracking (TrackIR/FreeTrack) support. It reads the mouse through DirectInput, and gamepad input through XInput. There are two ways to send input to a game:
- **Relative mouse movement** with Win32 `SendInput`. Raw input and DirectInput consumers see it like real hardware; no driver is needed. [verified-hw] Skyrim accepts it (user, 2026-10-10).
- **Virtual gamepad** (XInput right stick). This needs a kernel bus driver: ViGEmBus is retired (archived 2023), and our own driver needs EV and attestation signing (same blocker as the own IddCx driver, ROADMAP "Later").

## Decision
Inject relative mouse movement with `SendInput` (`Host.Gaming.MouseInjector`). The mapping is pure, tested Core code (`Core.Gaming.HeadMouseMapper`) with two modes:
- **Look**: mouse counts per degree of head rotation (1:1 camera). It uses the absolute world orientation, so the workspace auto-center does not leak in. A rate deadband (default 0.5 °/s, ramping to 1 at 2×) removes jitter and the slow fusion corrections. Sub-count remainders are kept.
- **Stick**: the head's offset from center sets a turn speed (dead zone 5°, full speed at 25°, 1500 counts/s). Toggling on or Ctrl+Alt+C recenters.

**Game on the glasses:** games, and every exclusive-fullscreen game, open on the Windows primary monitor (the one at (0,0)). While head look runs, the layout is shifted so the glasses are at (0,0) (`Display.GameDisplay.GlassesPrimary`, `Host.Gaming.GlassesPrimarySession`). The old layout goes to the workspace snapshot file, so app start and `xrs recover` restore it after a crash; exit restores it. Not while a workspace runs (same snapshot). Windowed/borderless games can also be moved with Ctrl+Alt+G (`WindowMover.MoveForegroundWindow`: a window covering its monitor fills the glasses, others are centered).

First delivery is `xrs imu mouse` (Ctrl+Alt+H toggles, starts off). The glasses are a plain monitor while gaming: OSD **Follow**, UltraWide **Off**, no XrealScreen workspace running.

## Consequences
- + Works in any game with mouse look; no driver, no signing, no admin (unless the game runs elevated, then UIPI blocks injection and xrs must be elevated too).
- + The real mouse still works at the same time (head look adds to it).
- − Games with mouse smoothing or acceleration change the feel; sensitivity must be tuned per game (`--sens` until a 90° head turn is 90° in game).
- − Games that read only XInput (no mouse) are not covered; a virtual gamepad stays a later option behind the same mapper.
- − While the glasses are primary, the taskbar and new windows go there too.
- Follow-up: an app page (enable, mode, sensitivity, hotkey) that runs the mapper on the app's IMU stream.

## Alternatives
- ViGEmBus virtual Xbox pad: retired, needs a separately installed kernel driver.
- Own virtual HID/gamepad driver (VHF/KMDF): EV + attestation signing, same as the own IddCx driver; deferred.
- FreeTrack/TrackIR protocol (OpenTrack style): Skyrim does not support it.

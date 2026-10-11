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

## Later
- Own IddCx driver (WDK + EV cert + attestation signing)
- Air-series HID support
- SBS stereo
- 6DoF (Eye camera)
- **Lean tracking (6DoF-lite)**: the 1S measures rotation only; real lean-in parallax needs head position, e.g. from a webcam (face tracking) or the XREAL Eye camera. Today: neck model + Ctrl+Alt+Up/Down distance (user request 2026-09-26).

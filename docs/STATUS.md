# Project Status

> Read this first. Update it at the end of every task (see skill `docs-handoff`).

## Current
- **Milestone:** M4/M5 (workspace in the app) — **mostly done**; M6 started (crash-safe restore). Last updated 2026-10-11.
- The app is usable end to end: Start menu → **XrealScreen** → Home → **Start workspace**.

## Done
- M0–M3: protocol, virtual monitors, capture, head-tracked rendering (see ROADMAP).
- **Two workspace types** (Screens page, ADR-0010):
  - **Glasses anchor** (user's preferred): glasses OSD Anchor + UltraWide; app splits the glasses image into 1–3 pixel-exact virtual screens with a gap (default 10 px) and aspect (16:9 default); preview picture + **Apply screen settings** button; auto-restart when the glasses UltraWide changes.
  - **App head tracking**: curved screens, stabilizer presets (default Strong), tracking axes (default Turning + level; Full is most comfortable per user), neck model, Ctrl+Alt+Plus/Minus distance, desk refresh sync.
- Engine hardening: no NaN before the first IMU sample, watchdog for glasses re-enumeration, catch-all session errors, forced virtual-monitor resolutions (fixes SetDisplayConfig 87), crash log `%LOCALAPPDATA%\XrealScreen\crash.log`.
- **Tray + background (M6)**: tray icon (Open / Start / Stop / Recenter / Exit), close-to-tray, Ctrl+Alt+W toggles the workspace, Start with Windows (MSIX StartupTask, starts hidden in tray); app preferences in `%LOCALAPPDATA%\XrealScreen\app.json`.
- **Installer (M7 phases 1 + 2)**: `installer\driver\Build-Driver.ps1` → `artifacts\driver\` (virtual-display-rs 0.4 built from source, signed with a separate test certificate); `installer\Build-Package.ps1` → `artifacts\installer\` (signed self-contained MSIX + driver + scripts). Install keeps an existing driver, otherwise asks and installs ours ([verified-local]: keep-existing path; fresh install not yet tested).
- **Less lag + no nod bobbing (App head tracking)**: gyro-speed stabilizer, 20 ms prediction (ADR-0009), tilt correction only when the head is nearly still (ADR-0007 `RateGate`) — verified with the user in the glasses.
- **Crash-safe restore**: at app start (and `xrs recover`) leftover virtual monitors are removed and layout + refresh rates restored ([verified-local] hard-kill test).
- **Settings + workspaces (M6)**: all settings autosave (0.5 s after a change) and load at start (theme too); named workspaces (`%LOCALAPPDATA%\XrealScreen\workspaces\<name>.json`, Home page: save / load / delete / import / export one JSON bundle, `WorkspaceLibrary`); **Exit** item in the navigation footer quits for real. Not yet tested by the user.
- **Glasses connect / primary / windows (M6, 2026-09-29, not yet tested on hardware)**: Settings "Workspace to start automatically" (latest settings or a saved workspace; `AppPreferences.AutoStartWorkspaceName`) used by "Start it when XrealScreen starts" and "Start it when the glasses are connected" (app polls the glasses monitor every 2 s); Screens "Primary monitor while the workspace runs" (`WorkspaceOptions.PrimaryScreen`, layout shifted so that virtual screen is at 0,0; original layout restored on stop); "Move windows from the glasses display" (`WindowMover`, default on; elevated apps' windows cannot be moved).
- **UltraWide guard (2026-09-29, not yet tested on hardware)**: Glasses anchor with UltraWide Off does not start; Home shows a warning, the tray shows a balloon, and the app starts the workspace once the glasses report an UltraWide signal (2 s poll + 2.5 s settle); Stop cancels the wait.
- **Glasses screenshots (2026-09-29)**: Home **Screenshot** button, tray item and **Ctrl+Alt+P** save what the glasses show to `Pictures\XrealScreen\XrealScreen <date time>.png` (`GlassesScreenshot` → `ScreenCapture`: GDI BitBlt of the glasses monitor after DWM composition + WinRT PNG encoder); also `xrs capture glasses [--out <folder>]`. [verified-local] with the CLI on the 1S (3840×1080 PNG); app button/hotkey not yet tested. Repo screenshots in `images/` (all pages, `glasses-anchor.png`, README hero `glasses-workspace.png`); README rewritten with the current features.
- **Head look for games (2026-10-10, ADR-0011)**: app **Games** page (Start/Stop, on/off, recenter, Look or Joystick mode, sensitivity, dead zones, up/down strength, invert, glasses-as-primary toggle; settings in `app.json`, applied live) and `xrs imu mouse`, both on `Host.Gaming.HeadLookEngine`. Head movement → relative mouse via `SendInput` (no driver); [verified-hw] works in Skyrim (user, via xrs). Glasses become primary while it runs (crash-safe snapshot; `xrs recover`), Ctrl+Alt+H on/off, Ctrl+Alt+C recenter, Ctrl+Alt+G moves the active windowed game to the glasses; IMU connection retries every 2 s. Games page [verified-hw] works in Skyrim (user, 2026-10-10). Glasses: Follow, UltraWide Off, no workspace running.
- **Developer SDK + local API (2026-10-10, ADR-0012)**: `XrealScreen.Sdk` (NuGet; `XrealGlasses` pose stream with auto-reconnect, `HeadLookEngine`, `GlassesDisplay`; head-look code moved here from Host), local API = JSON-RPC 2.0 over `\\.\pipe\XrealScreen.Api` (`CurrentUserOnly`; `XrealScreen.Contracts`, `Host.Api.LocalApiServer`, app `AppApiHandler`; Settings → Developers toggle, on by default), .NET client `XrealScreen.Sdk.Client`. Docs `docs/sdk/` (overview, SDK guide, full protocol reference). Samples: `HeadPose.Console` (`--simulate`), `AppClient.Console`, Python, PowerShell. `tools/Pack-Sdk.ps1` → `artifacts/nuget` (6 packages). [verified-local] every client against the running app (status, workspaces, settings, errors, head look start/stop) and both packages from fresh consumer projects; pose streaming over the API not yet tested with the glasses.
- **MSI installer (2026-10-11, ADR-0013)**: `installer\Build-Msi.ps1` → `artifacts\installer\XrealScreen-<ver>-x64.msi` (FalkForge 0.5.0-beta.8, ~86 MB) [verified-local: build, table check, forge validate; ICE skipped (no darice.cub); **not yet installed**]. Unpackaged self-contained app [verified-local: starts; borderless capture allowed without identity]; Start with Windows via HKCU Run when unpackaged (`AppStartup`, `Host.RunKeyStartup`, tests); `xrs driver status|install|uninstall` (`Host.DriverSetup`, C# port of Install-Driver.ps1) [verified-local: status finds VertoXR 0.4.0.0, install keeps it, uninstall no-op]; optional Driver feature only when `artifacts\driver` exists (not on this PC now: no Rust toolchain). Unsigned; `-CertificateThumbprint` ready for later.
- **Microsoft Store package (2026-10-11, ADR-0005 update)**: `installer\Build-StorePackage.ps1` → unsigned self-contained `.msixbundle` (x64 + ARM64, ~207 MB) in `artifacts\store\` [verified-local] (build + bundle; placeholder identity). Identity from `installer\store\StoreIdentity.json` (placeholders until the name is reserved in Partner Center); submission notes + capability justifications in `installer\store\README.md`. No driver in the Store package: Home shows **Virtual display driver not installed / Get the driver** (→ GitHub releases/latest) and Start refuses with an explanation (`Host.VirtualDisplayDriver`; not yet seen in the app — this PC has the driver). `installer\Build-DriverPackage.ps1` → `XrealScreen-Driver.zip` (not yet run: needs `artifacts\driver`). Manifest now targets Windows.Desktop only.
- Pitfall: building `src/XrealScreen.App` while the app runs re-registers the dev package and kills the app without cleanup (leftover virtual monitors) — run `xrs recover`.
- 169/169 tests (RunKeyStartup 7 new).

## Next steps
1. MSI install test (admin): `msiexec /i artifacts\installer\XrealScreen-0.1.0-x64.msi /l*v install.log` → Start menu entry (two "XrealScreen" entries while the dev MSIX is registered), app starts, tray icon, Settings → Start with Windows (HKCU Run value + sign-in start in tray), workspace in the glasses, Repair, uninstall (Run value gone). Then build the driver (`installer\driver\Build-Driver.ps1` needs rustup + LLVM 18) and test the Driver feature in Windows Sandbox (fresh install + uninstall). Payload trimming (Windows App SDK AI components). Signing later (ADR-0013).
2. Store: reserve the name in Partner Center, fill `installer\store\StoreIdentity.json`, run `Build-StorePackage.ps1 -RunWack` (admin), privacy policy URL, submit. Driver for Store users: EV certificate + attestation signing, then a GitHub release with `XrealScreen-Driver.zip` (the app links to releases/latest). Check the driver-missing banner on a PC without the driver (Sandbox).
3. With the glasses: `dotnet run --project samples/AppClient.Console -- poses` and `samples/HeadPose.Console` (pose stream over the API and the SDK); publish the packages (nuget.org) when the API settles.
4. M6: user test of tray icon / start with Windows / Ctrl+Alt+W (2026-09-27) and of saved workspaces / import-export / start latest workspace / Exit / start on glasses connect / primary screen / window move (2026-09-29).
5. M7: clean-PC install test in Windows Sandbox (`installer\test\Start-SandboxTest.ps1` -> `artifacts\sandbox\results\report.txt`). **First run 2026-09-27 stalled** right after "PASS no driver before install", inside `Install-XrealScreen.ps1 -Yes` (no output for 9 min) - probably a dialog in the Sandbox (driver install prompt or Add-AppxPackage). Next: look at the Sandbox window, log each install step separately with timeouts in `Run-InSandbox.ps1`, rerun. Then trusted signing for a public release.
6. App head tracking: glasses A/B of prediction 20 vs 26 ms (measured latch→scan-out ≈ 22 ms, ADR-0009); optional Strong vs Balanced for the last nod bob. Removing DWM (always composed here) needs exclusive fullscreen or DirectComposition — deferred.

## Blockers
- None.

## Open questions / known gaps
- virtual-display-rs is installed on this PC by VertoXR (self-signed); distribution/signing for other PCs → M7 (ADR-0008).
- In App head tracking, partial axis modes (YawRoll/YawPitch) can cause motion sickness; Full felt best; lag is now "a little" (2026-09-27). Glasses anchor avoids this.
- `missed` frame counter in presenter stats is noisy; p99 frame time is the reliable number.
- Control port 52999 not implemented (glasses-side recenter/UltraWide switching from the app would need it).
- Real lean tracking needs a camera (roadmap "Later").

## Last hardware verification
- **2026-09-26 · XREAL 1S · PID 0x043E · fw 15.01.03.522** — NCM link, ports, IMU framing/units/axes, live tracking in app. Details: `docs/findings/xreal-one-protocol.md`, `docs/findings/xreal-1s-hardware.md`.
- **2026-09-27 · XREAL 1S · fw 15.01.03.522** — App head tracking lag A/B (latch/prediction) and nod A/B (tilt-correction rate gate); nod IMU fixture recorded.
- **2026-09-26/27 · XREAL 1S · fw 15.01.03.522** — both workspace types in the glasses with the user: anchor split 2×/3× screens with gap in 32:9 and 21:9, UltraWide auto-restart, app head tracking comfort A/B, mouse, crash recovery.

## Session log
| Date | Agent | Summary |
|------|-------|---------|
| 2026-09-26 | Claude | M0: research, plan, solution, app, docs, CI. |
| 2026-09-26 | Claude | M1 hardware test on 1S: protocol verified, axis mapping/pitch/bias fixed, fixtures + tests, docs updated. |
| 2026-09-26 | Claude | M2: Display project, glasses EDID detection, ADR-0008 virtual-display-rs provider verified on hardware. |
| 2026-09-26 | Claude | M2 done: layout snapshot/restore, 1S mode table, WGC capture of virtual monitors. |
| 2026-09-26 | Claude | M3: renderer in the glasses; user A/B tests → tracker tuning, stabilizer presets, desktop refresh sync (ADR-0009 update). |
| 2026-09-26 | Claude | M3 done: drift test 5 min OK, mouse fixed (desktop order), all verified with the user in the glasses. |
| 2026-09-26 | Claude | M4/M5 start: WorkspaceEngine (Host), app Start/Stop workspace, stabilizer UI, settings persistence. |
| 2026-09-26/27 | Claude | M4/M5: engine + app Start/Stop; user feedback → axes modes, neck model, stabilizer, NaN fix, glasses-anchor workspace (ADR-0010) with gap/aspect/preview/apply, crash-safe restore. |
| 2026-09-27 | Claude | M6 tray/autostart/hotkey; M7 phase 1: icon, signed self-contained MSIX, install scripts. |
| 2026-09-27 | Claude | Lag work: gyro-speed stabilizer, late latch, 20 ms prediction; nod bobbing fixed with Madgwick rate gate (glasses A/B). |
| 2026-09-27 | Claude | Latency measurement in presenter stats (latch→scan-out ≈ 22 ms, always DWM-composed); late latch default back to 0 (no gain while composed). |
| 2026-09-27 | Claude | M7 phase 2: driver built from source (rustup, WDK, LLVM 18), separate driver cert, Install-Driver.ps1 (SetupAPI root device), package bundles the driver. |
| 2026-09-29 | Claude | M6: autosave of all settings, saved workspaces with import/export, start latest workspace at launch, Exit in the window; Host.Tests project; start on glasses connect, primary virtual screen, move windows off the glasses. |
| 2026-10-10 | Claude | Head look for games (ADR-0011): HeadMouseMapper (Look/Stick) + SendInput injector + `xrs imu mouse`. |
| 2026-10-10 | Claude | Head look worked in Skyrim; games on the glasses: glasses primary while head look runs (crash-safe), Ctrl+Alt+G moves the active window. |
| 2026-10-10 | Claude | `xrs imu mouse` waits for and reconnects to the glasses' IMU instead of exiting on a connect timeout. |
| 2026-10-10 | Claude | Games page in the app for head look; loop moved to `HeadLookEngine` (shared with `xrs imu mouse`). |
| 2026-10-10 | Claude | Developer SDK (XrealScreen.Sdk) + local API (JSON-RPC over a named pipe, Contracts, Sdk.Client), docs/sdk, samples, NuGet packing, 48 new tests (ADR-0012). |
| 2026-10-11 | Claude | Microsoft Store packaging: Build-StorePackage.ps1 (unsigned x64+ARM64 bundle), driver-missing detection + Get the driver link, standalone driver zip script, ADR-0005 update. |
| 2026-10-11 | Claude | MSI with FalkForge (ADR-0013): unpackaged app + Run-key autostart, `xrs driver` (C# driver setup), installer/msi + Build-Msi.ps1. |

# XrealScreen — agent guide

Windows 11 app (.NET 10, WinUI 3) that turns XREAL 1S / One / One Pro glasses into a head-tracked multi-monitor workspace (multiple screens, ultrawide Off/21:9/32:9/16:18, auto center).

## Start here (every session)
1. Read `docs/STATUS.md` — current milestone, next steps, blockers.
2. Read `docs/ROADMAP.md` for the milestone's exit criteria, and the ADRs/findings it links.
3. Before finishing: update `docs/STATUS.md` (skill `docs-handoff`) and commit.

## Commands
```powershell
dotnet build XrealScreen.slnx -p:Platform=x64      # everything incl. WinUI app
dotnet test --solution XrealScreen.slnx            # xUnit v3 on Microsoft.Testing.Platform (global.json)
dotnet build src/XrealScreen.App -p:Platform=x64         # build the APP PROJECT before running it: the solution build writes bin\Debug\…, but
dotnet run --project src/XrealScreen.App -p:Platform=x64   # `dotnet run -p:Platform=x64` launches bin\x64\Debug\… (stale app otherwise!)
.\installer\Build-Msi.ps1                            # MSI (FalkForge, ADR-0013): app + xrs + driver page -> artifacts\msi\ (copy only the .msi)
.\installer\Build-Package.ps1                        # signed self-contained MSIX + install scripts -> artifacts\installer\
.\installer\Build-StorePackage.ps1                   # unsigned Store .msixbundle (x64+ARM64) -> artifacts\store\ (installer\store\README.md)
.\tools\Pack-Sdk.ps1                                 # SDK NuGet packages -> artifacts\nuget\ (docs/sdk)
dotnet run --project tools/XrealScreen.Cli -- --help       # xrs: devices | probe | imu … | display … | vdd … | capture test | render
```
WinUI templates: `dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates`.

## Repo map
| Path | What |
|------|------|
| `src/XrealScreen.Core` | Tracking math (Madgwick, bias, auto-center, prediction), workspace/ultrawide model, abstractions, `.xrimu` |
| `src/XrealScreen.Device.XrealOne` | One-series USB-network transport (IMU stream framer, NCM probe, `.xrcap`) |
| `src/XrealScreen.Device.Simulated` | Synthetic / replayed IMU |
| `src/XrealScreen.Display` | CCD topology, glasses EDID detection, layout snapshot/restore, virtual-display-rs provider (ADR-0008) |
| `src/XrealScreen.Render` | D3D11 (Vortice): WGC capture, workspace scene, glasses presenter (ADR-0009) |
| `src/XrealScreen.Host` | `WorkspaceEngine` — one workspace session end to end; settings store |
| `src/XrealScreen.App` | WinUI 3 packaged app (MVVM with CommunityToolkit.Mvvm); Home starts/stops the workspace; Games = head look; serves the local API |
| `src/XrealScreen.Sdk` | Public .NET SDK (NuGet): `XrealGlasses`, `HeadLookEngine`, `GlassesDisplay` (ADR-0011/0012) |
| `src/XrealScreen.Contracts`, `src/XrealScreen.Sdk.Client` | Local API protocol (JSON-RPC over `\\.\pipe\XrealScreen.Api`) and its .NET client (ADR-0012) |
| `samples/` | SDK and local API samples (.NET, Python, PowerShell) |
| `tools/XrealScreen.Cli` | `xrs` hardware/dev tool |
| `tests/` | `XrealScreen.Core.Tests`, `.Device.Tests`, `.Display.Tests`, `.Host.Tests`, `.Render.Tests`, `.Sdk.Tests`, `.Api.Tests`, `fixtures/` |
| `docs/` | STATUS, ROADMAP, ARCHITECTURE, `decisions/` (ADRs), `findings/`, `legal/SOURCES.md`, `testing/`, `sdk/` (developer docs) |
| `.claude/agents/` | Role subagents (architect, winui-dev, device-protocol, graphics-engineer, display-driver-engineer, qa-engineer, tech-writer, legal-security-reviewer) |
| `.claude/skills/` | winui-app, csharp-async, csharp-developer, csharp-xunit, docs-handoff, xreal-protocol, windows-display-ccd, d3d11-vortice, msix-packaging |

Planned projects (Service M6) are created when their milestone starts — see `docs/ARCHITECTURE.md`.

Glasses setup depends on the workspace type (ADR-0010): **Glasses anchor** = OSD **Anchor** + UltraWide 32:9/16:18/21:9; **App head tracking** = UltraWide **Off**, **Follow**, **Stabilizer off** (see `docs/findings/xreal-1s-hardware.md`).
The packaged app appears in the Start menu as **XrealScreen** after `dotnet run`; starting the dev build's `XrealScreen.App.exe` directly crashes (no package identity). The MSI ships an unpackaged build (`WindowsPackageType=None`) that runs without identity (ADR-0013).

## Rules
- **Clean room (ADR-0006):** never copy code from GPL projects, the XREAL SDK (no license) or VertoXR (proprietary; no decompiling). MIT code may be ported with attribution in `THIRD-PARTY-NOTICES.md` and `docs/legal/SOURCES.md`.
- **Tag every finding:** `[verified-hw]`, `[verified-local]`, `[from-source:MIT]`, `[from-GPL:facts-only]`, `[from-sdk:facts-only]`, `[hypothesis]`.
- **Keep docs clean:** a decision goes in an ADR; if direction changes, mark the old ADR "Superseded by ADR-xxxx" and delete stale text elsewhere — don't accumulate history in STATUS.
- **Code:** central package versions (`Directory.Packages.props`), warnings are errors, async methods end in `Async` and take a `CancellationToken` (skill `csharp-async`), `ConfigureAwait(false)` in libraries.
- **Hardware facts:** only `[verified-hw]` after a run on the real glasses; record date, device, firmware in STATUS "Last hardware verification".
- Commit after each completed step with a descriptive message.

# ADR-0013: MSI installer built with FalkForge (unpackaged app)

- **Status:** Accepted
- **Date:** 2026-10-11

## Context
The user wants a real MSI for XrealScreen, built with FalkForge (https://falkesand.github.io/FalkForge/, C# fluent
API → `msi.dll`, NuGet `FalkForge.Core` + `FalkForge.Compiler.Msi`). ADR-0005 ships an MSIX plus PowerShell scripts
for the driver. FalkForge cannot give an MSI-installed app package identity (its MSIX compiler is experimental, has no
StartupTask/desktop extensions and no sparse/external-location packages) [verified-local, source at `877eec7`].

The app used package identity for three things: the `StartupTask` (Start with Windows), borderless capture
(`graphicsCaptureWithoutBorder`) and the Windows App SDK runtime. Spike 2026-10-11 [verified-local]:
- `dotnet publish src/XrealScreen.App -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true --self-contained`
  starts and runs without identity.
- Unpackaged `xrs capture test` gets `GraphicsCaptureAccess.RequestAccessAsync(Borderless)` = **Allowed**, so no
  yellow border without identity.

## Decision
Ship an **MSI of the unpackaged, self-contained app**, built with FalkForge:
- **App:** "Start with Windows" goes through `AppStartup`: StartupTask when the process has identity (MSIX, dev
  registration), otherwise an HKCU `…\Run\XrealScreen` value with `--autostart` (`Host.RunKeyStartup`; Task Manager's
  `StartupApproved` switch is honoured). `Assets` are copied to the output.
- **Driver:** `xrs driver install|uninstall|status` (`Host.DriverSetup`, C# port of `Install-Driver.ps1`, same
  `%ProgramData%\XrealScreen\driver-install.json`): trusts the driver test certificate, creates `Root\VirtualDisplayDriver`
  through SetupAPI, installs the INF, keeps an existing virtual-display-rs driver, removes only what it installed.
  No prompts — the MSI's feature selection is the consent.
- **MSI** (`installer/msi`, `installer/Build-Msi.ps1`): per-machine, `Program Files\XrealScreen`, payload = app + `xrs`
  published into one folder, Start menu shortcut, fixed UpgradeCode `d322530b-9c76-4b91-882d-db8616d6ea57`,
  MajorUpgrade + downgrade block, Restart Manager (the app runs in the tray), Windows 11 x64 launch conditions, Run
  value removed on uninstall, `LICENSE` + `THIRD-PARTY-NOTICES.md` installed. Wizard: Welcome → License (LICENSE as RTF)
  → Install folder → **Virtual display driver** page (custom dialog, check box `INSTALLDRIVER`, default on; only when
  `artifacts\driver` is built) → progress. FalkForge cannot insert a page into a stock wizard (its step-builder
  interface is internal), so the InstallDir page's last button reads **Next** and the driver page follows the wizard
  in the UI sequence. `xrs driver` runs as deferred, elevated PowerShell custom actions (FalkForge has no
  EXE-from-installed-file action); the driver stays on a major upgrade. Windows 11 check: build number from the
  registry (AppSearch/RegLocator as custom tables) — FalkForge's `WindowsBuildNumber` property does not exist in
  Windows Installer, so `Condition.IsWindows11OrLater` always fails [verified-local]. ICE validation with the FalkForge
  CLI pinned in `dotnet-tools.json`.
- **Signing later**: unsigned builds for testing. `Build-Msi.ps1 -CertificateThumbprint` already signs our exes/DLLs
  (signtool) and the MSI (FalkForge `Signing`, `XRS_SIGN_THUMBPRINT`). A public release needs a trusted code-signing
  certificate (or Azure Trusted Signing) and the driver attestation-signed by Microsoft (no self-signed root on user PCs).

The MSIX path (ADR-0005, Store package) stays for development (`dotnet run`) and the Store.

## Consequences
- + Standard Windows install/repair/uninstall, silent installs (`msiexec /i … /qn`, `INSTALLDRIVER=0` without the driver),
  installs the driver itself instead of PowerShell scripts.
- + Same code runs packaged and unpackaged; no identity-only APIs left in the app.
- − ~86 MB MSI: the self-contained Windows App SDK brings components the app does not use (OnnxRuntime, DirectML, …).
  Trimming the payload is follow-up work.
- − FalkForge is a prerelease (0.5.0-beta.8, FSL-1.1 → Apache-2.0 after two years; building installers for our own
  app is a permitted purpose). Pinned exactly; it is a build tool only, nothing of it ships in the MSI.
- − Without the Windows SDK MSI tools (`darice.cub`) ICE validation is skipped; FalkForge then still prints "passed".
- − HKCU Run value cleanup on uninstall only reaches the installing user.

## Alternatives
- **MSI + sparse identity package** (register a signed external-location package from a custom action): keeps the
  StartupTask, but identity is not needed (spike) and FalkForge cannot author it. Rejected.
- **WiX**: mature, but the user chose FalkForge.
- **Keep MSIX + scripts only** (ADR-0005): no MSI; the driver needs a PowerShell bootstrapper.

# XrealScreen installer (test build)

## MSI (recommended, ADR-0013)
```powershell
.\installer\Build-Msi.ps1 -Version 0.1.0     # -> artifacts\msi\XrealScreen-0.1.0-x64.msi (the only file to copy)
```
Double-click the MSI: Welcome → License (MIT) → Install folder (Change...) → Virtual display driver (check box; only
when the driver is bundled) → Install (Windows asks for administrator rights). Or silently:
```powershell
msiexec /i XrealScreen-0.1.0-x64.msi /qn /l*v install.log          # app + driver (when bundled)
msiexec /i XrealScreen-0.1.0-x64.msi /qn INSTALLDRIVER=0            # app only
msiexec /i XrealScreen-0.1.0-x64.msi /qn INSTALLDIR="D:\Apps\XrealScreen\"   # other folder
msiexec /x XrealScreen-0.1.0-x64.msi /qn                            # uninstall
```
- Installs the unpackaged, self-contained app (+ `xrs.exe`) to `C:\Program Files\XrealScreen` and a Start menu shortcut.
- **Virtual display driver** (only when `artifacts\driver` was built, `driver\Build-Driver.ps1`): runs
  `xrs driver install` — keeps an existing virtual-display-rs driver, otherwise trusts the driver's test certificate and
  installs the driver. Uninstall removes only what it installed (`xrs driver status` shows the state).
- Built with FalkForge (`installer\msi\Program.cs`; CLI pinned in `dotnet-tools.json`). ICE validation needs the
  Windows SDK MSI tools (`darice.cub`), otherwise it is skipped.
- Unsigned for now. Signing: `-CertificateThumbprint <thumbprint>` signs our exes/DLLs and the MSI; a public release
  needs a trusted certificate and an attestation-signed driver (ADR-0013).

### GitHub release
`.github/workflows/release.yml` builds the MSI on a GitHub runner and attaches it to the release:
1. Once: add two repository secrets (Settings → Secrets and variables → Actions) so the driver is signed with the
   same test certificate every release:
   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes('installer\.signing\driver\XrealScreen-driver.pfx')) | Set-Clipboard   # DRIVER_PFX_BASE64
   Get-Content installer\.signing\driver\pfx-password.txt | Set-Clipboard                                                    # DRIVER_PFX_PASSWORD
   ```
   Without them the release MSI has no driver page.
2. Publish a release with a tag `vX.Y.Z` (e.g. `v0.2.0`): the workflow builds `XrealScreen-X.Y.Z-x64.msi` and uploads
   it to that release (~20–30 min). **Actions → Release MSI → Run workflow** builds a test MSI as a workflow artifact.

## MSIX + scripts (ADR-0005)

## Install
1. Copy this folder to the PC.
2. Open **PowerShell as administrator** in this folder and run:
   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   .\Install-XrealScreen.ps1
   ```
3. Start **XrealScreen** from the Start menu.

The package is self-contained (.NET and Windows App SDK included). It is signed with a **test certificate**; the script trusts it on this PC (Local Machine → Trusted People).

The **virtual display driver** (virtual-display-rs 0.4, MIT, built from upstream source) is in `driver\`. If the PC already has a virtual-display-rs driver (for example from VertoXR) it is kept; `-UpdateDriver` replaces it with the bundled build. Otherwise the script **asks** before it trusts the driver's test certificate "XrealScreen Driver (test)" in Trusted Root + Trusted Publishers — Windows installs self-signed drivers only then — and installs the driver.

## Requirements
- Windows 11, x64.
- XREAL One-series glasses (tested: XREAL 1S).
- Nothing else: the virtual display driver is bundled (see above).

## Uninstall
```powershell
.\Uninstall-XrealScreen.ps1
```
Removes the app, and the driver + driver certificate only if this installer installed them.

## Build (developers)
```powershell
.\installer\driver\Build-Driver.ps1        # → artifacts\driver\ (needs rustup, WDK 10.0.26100, LLVM 18; see the script)
.\installer\Build-Package.ps1              # → artifacts\installer\ (bundles artifacts\driver)
.\installer\Build-Package.ps1 -Version 0.2.0.0
```

## Microsoft Store
The Store build has no driver: see `store\README.md` (`Build-StorePackage.ps1`, `Build-DriverPackage.ps1`).

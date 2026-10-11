# XrealScreen installer (test build)

## MSI (recommended, ADR-0013)
```powershell
.\installer\Build-Msi.ps1 -Version 0.1.0     # -> artifacts\installer\XrealScreen-0.1.0-x64.msi
```
Double-click the MSI (Windows asks for administrator rights), or silently:
```powershell
msiexec /i XrealScreen-0.1.0-x64.msi /qn /l*v install.log          # app + driver (when bundled)
msiexec /i XrealScreen-0.1.0-x64.msi /qn ADDLOCAL=App               # app only
msiexec /x XrealScreen-0.1.0-x64.msi /qn                            # uninstall
```
- Installs the unpackaged, self-contained app (+ `xrs.exe`) to `C:\Program Files\XrealScreen` and a Start menu shortcut.
- **Virtual display driver** feature (only when `artifacts\driver` was built, `driver\Build-Driver.ps1`): runs
  `xrs driver install` — keeps an existing virtual-display-rs driver, otherwise trusts the driver's test certificate and
  installs the driver. Uninstall removes only what it installed (`xrs driver status` shows the state).
- Built with FalkForge (`installer\msi\Program.cs`; CLI pinned in `dotnet-tools.json`). ICE validation needs the
  Windows SDK MSI tools (`darice.cub`), otherwise it is skipped.
- Unsigned for now. Signing: `-CertificateThumbprint <thumbprint>` signs our exes/DLLs and the MSI; a public release
  needs a trusted certificate and an attestation-signed driver (ADR-0013).

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

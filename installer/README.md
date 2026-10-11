# XrealScreen installer (test build)

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

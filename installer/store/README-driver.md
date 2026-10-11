# XrealScreen virtual display driver

XrealScreen from the Microsoft Store cannot install drivers. Workspaces (virtual screens in the glasses) need this
driver; head look on the Games page works without it.

## Install
Open **PowerShell as administrator** in this folder:
```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Install-Driver.ps1
```
An existing virtual-display-rs driver (for example from VertoXR) is kept; `-Update` replaces it.
Then restart XrealScreen.

## Uninstall
```powershell
.\Install-Driver.ps1 -Uninstall
```
Removes only what this script installed.

The driver is virtual-display-rs 0.4 (MIT), built unmodified from upstream source.

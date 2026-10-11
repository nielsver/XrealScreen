# Microsoft Store submission

## Build
```powershell
.\installer\Build-StorePackage.ps1 -Version 1.0.0.0           # -> artifacts\store\XrealScreen_<version>.msixbundle
.\installer\Build-StorePackage.ps1 -RunWack                   # (admin) also runs the Windows App Certification Kit
.\installer\Build-DriverPackage.ps1                           # -> artifacts\driver-installer\XrealScreen-Driver.zip
```
- Fill in `StoreIdentity.json` first (Partner Center → your app → Product management → **Product identity**:
  `Package/Identity/Name`, `Package/Identity/Publisher`, `Package/Properties/PublisherDisplayName`). With placeholders the
  script builds a local test bundle with the sideload identity that the Store rejects.
- The bundle is unsigned: the Store signs it. The last version number must be 0; each upload needs a higher version.
- Upload the `.msixbundle` in the submission's **Packages** step.

## Driver
The Store package has no virtual display driver (MSIX cannot install drivers, ADR-0005). The app shows
**Get the driver** (→ GitHub releases/latest) when it is missing, so each release should attach `XrealScreen-Driver.zip`.
Before a public release the driver must be attestation-signed (EV certificate + Microsoft Hardware Dev Center);
today it carries the test certificate.

## Restricted capabilities — justification text for the submission
- **runFullTrust** — XrealScreen is a desktop (Win32) WinUI 3 app: it configures the Windows display topology,
  talks to the XREAL glasses over their USB network adapter, injects mouse input for head look, and hosts a local
  named-pipe API.
- **graphicsCaptureProgrammatic** — the app captures its own virtual monitors with Windows.Graphics.Capture to show
  them in the glasses; a picker per monitor at every start would make the product unusable.
- **graphicsCaptureWithoutBorder** — the captured monitors are rendered into the glasses; the yellow capture border
  would appear inside the user's workspace.

## Listing notes
- Requires XREAL One-series glasses (tested: XREAL 1S) and Windows 11 22H2+. Workspaces need the separate driver
  installer; head look for games works without it. Say this in the description so certification testers know.
- Privacy policy URL is required (the app reads screen content locally; nothing leaves the PC).

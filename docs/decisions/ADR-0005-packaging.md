# ADR-0005: MSIX + bootstrapper

- **Status:** Accepted
- **Date:** 2026-09-26

## Context
We want clean install/update/uninstall (MSIX), but MSIX cannot install kernel or UMDF drivers. The VDD (ADR-0001) must be installed separately. The helper service (ADR-0004) can be packaged via `desktop6:Service`, which needs the restricted `packagedServices` capability.

## Decision
- App + service ship as a **signed MSIX** (test certificate during development).
- A **bootstrapper** (`installer/`) installs a **pinned, hash-verified** VirtualDrivers VDD release, then installs the MSIX.
- Capabilities: `runFullTrust`, `graphicsCaptureWithoutBorder`, `graphicsCaptureProgrammatic`, `packagedServices`; `StartupTask` for autostart.

## Consequences
- + Clean app lifecycle; OS handles updates and uninstall of app/service.
- − Uninstalling the MSIX leaves the VDD; bootstrapper/uninstaller must offer removal (only if we installed it and no other app uses it).
- − Code signing cost: publisher certificate needed for release.
- VDD is listed in THIRD-PARTY-NOTICES but not bundled in the MSIX.

## Alternatives
- MSI/WiX only — can install drivers but loses MSIX lifecycle and packaged identity (needed for some capture capabilities).
- Unpackaged + winget script — fragile, no identity.

## Update 2026-09-27 — phase 1 implemented (user decision: "step 1 now, driver later")
- `installer/Build-Package.ps1` builds a **self-contained** MSIX (x64; .NET + Windows App SDK inside, no framework package dependency) signed with a self-signed test certificate `CN=Niklas Ryden` (private key in git-ignored `installer/.signing/`).
- `installer/Install-XrealScreen.ps1` (admin): trusts the certificate (LocalMachine\TrustedPeople), **checks** for the virtual-display-rs driver and explains if missing, removes a developer registration, installs the MSIX. `Uninstall-XrealScreen.ps1` removes it.
- The driver is **not bundled yet** (ADR-0008: 0.4.0 has no official release; building it needs Rust + WDK). Phase 2: build and sign virtual-display-rs ourselves and let the bootstrapper install it.
- Trimming stays off (WinUI + reflection risk); ReadyToRun on in Release.

## Update 2026-09-27 — phase 2: bundled virtual display driver (user decision: separate driver certificate)
- `installer/driver/Build-Driver.ps1` builds virtual-display-rs **unmodified** at commit `22fcd2e0` (crate 0.4.0, the
  protocol `VirtualDisplayRsProvider` speaks), stamps the INF as **0.4.0.1**, and signs the DLL and catalog with a
  **separate** self-signed certificate `CN=XrealScreen Driver (test)` (key in git-ignored `installer/.signing/driver/`).
  Toolchain [verified-local]: rustup (pinned nightly-2024-07-26 from the upstream `rust-toolchain.toml`), WDK 10.0.26100,
  VS 2026 C++ tools, **LLVM 18.1.8** as libclang — LLVM 23 made bindgen 0.70 emit empty structs, and newer bindgen emits
  syntax the pinned nightly cannot parse.
- `installer/Install-Driver.ps1` (called by `Install-XrealScreen.ps1`): keeps an existing virtual-display-rs driver
  (e.g. VertoXR's 0.4.0.0) unless `-UpdateDriver`; otherwise **asks** before adding the driver certificate to
  LocalMachine Root + TrustedPublisher, creates the root device `Root\VirtualDisplayDriver` (SetupAPI, what
  `devcon install` does) and installs the driver. It records what it did in `%ProgramData%\XrealScreen\driver-install.json`;
  uninstall removes only that (device, driver package, certificate).
- Security: trusting a self-signed root lets whoever holds its key sign code this PC trusts. Hence a dedicated driver
  certificate, an explicit prompt, and removal on uninstall. For a public release: EV certificate + Microsoft
  attestation signing (no root install), and a full license list of the Rust crates statically linked into the DLL.

## Update 2026-10-11 — Microsoft Store package (user decision: "route 2")
- MSIX cannot install drivers (platform limit, independent of the driver), so the **Store package has no driver**.
  The app checks the driver (`Host.VirtualDisplayDriver`, at start and before every workspace start) and, when it is
  missing, shows a Home warning with **Get the driver** → GitHub releases (`installer\Build-DriverPackage.ps1` →
  `XrealScreen-Driver.zip`). Head look (Games) and the local API work without the driver.
- `installer\Build-StorePackage.ps1` → unsigned `.msixbundle` (x64 + ARM64, self-contained) in `artifacts\store\`;
  identity from `installer\store\StoreIdentity.json` (Partner Center); `-RunWack` runs the App Certification Kit.
  Submission notes and capability justifications: `installer\store\README.md`.
- Manifest targets `Windows.Desktop` only (the template's `Windows.Universal` family was dropped).
- Open: the public driver needs an EV certificate + Microsoft attestation signing (no self-signed root on user PCs).
- Rejected: an EXE/MSI Store submission that installs the driver itself (loses the MSIX lifecycle; revisit once the
  driver is attestation-signed); Windows Update delivery (a root-enumerated software device has no hardware ID to match).

## Update 2026-10-11 — MSI
- An MSI of the unpackaged app (built with FalkForge) installs app + driver without PowerShell scripts: **ADR-0013**.
  The MSIX stays for development and the Store.

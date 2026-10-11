# Virtual display driver 0.4.0.1 (test-signed) — build input, not an app release

The virtual display driver used by XrealScreen: [virtual-display-rs](https://github.com/MolotovCherry/virtual-display-rs) 0.4 (MIT, commit `22fcd2e0`), built **unmodified** from source; only the INF version (0.4.0.1) and the signature are ours.

**Most people do not need this download — install the XrealScreen MSI from the latest release instead; it contains this driver.**

- `XrealScreen-Driver.zip` = `driver\` (DLL, INF, catalog, `XrealScreenDriver.cer`, MIT license) + `Install-Driver.ps1`.
- Signed with the **self-signed test certificate** *XrealScreen Driver (test)*. Installing it adds that certificate to the PC's Trusted Root and Trusted Publishers stores (the script asks first); uninstalling removes it.
- The XrealScreen release workflow downloads this pre-release to build the MSI, so the driver is not rebuilt for every app release.

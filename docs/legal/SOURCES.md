# External sources

Every external source consulted. **How used:** `facts` (IDs/constants/behaviour only), `reference` (read for understanding, no code), `code-ported` (code adapted — must also appear in THIRD-PARTY-NOTICES.md), `imported` (file copied verbatim). Policy: ADR-0006.

| Source | URL | License | How used | Accessed |
|--------|-----|---------|----------|----------|
| XREAL SDK com.xreal.xr 3.1.0 | local `E:\XREAL SDK\com.xreal.xr.tar` | none shipped; license-key gated | facts | 2026-09-26 |
| Skarian/one-xr | https://github.com/Skarian/one-xr | MIT | code-ported (stream framer, `OneReportFramer.cs`; control-port bytes, `OneControlWire.cs`); reference for axes/units | 2026-09-26 |
| SamiMitwalli/One-Pro-IMU-Retriever-Demo | https://github.com/SamiMitwalli/One-Pro-IMU-Retriever-Demo | MIT | reference | 2026-09-26 |
| Aloim Grayscale-Feed | https://github.com/Aloim/Grayscale-Feed-Xreal-One-Pro-Eye-Windows-Nebula-Beta-needed- | research-only | facts (port list) | 2026-09-26 |
| wheaney/XRLinuxDriver | https://github.com/wheaney/XRLinuxDriver | GPL-3.0 | facts (PIDs) | 2026-09-26 |
| ar-drivers-rs | https://github.com/badicsalex/ar-drivers-rs | MIT | reference | 2026-09-26 |
| MSmithDev/AirAPI_Windows | https://github.com/MSmithDev/AirAPI_Windows | GPL-3.0 | facts | 2026-09-26 |
| VirtualDrivers/Virtual-Display-Driver | https://github.com/VirtualDrivers/Virtual-Display-Driver | MIT | reference; installed by bootstrapper | 2026-09-26 |
| Microsoft IddSampleDriver | https://github.com/microsoft/Windows-driver-samples/tree/main/video/IndirectDisplay | MIT | reference | 2026-09-26 |
| MolotovCherry/virtual-display-rs (main 22fcd2e) | https://github.com/MolotovCherry/virtual-display-rs | MIT | code-ported (IPC wire format, `VdrsProtocol.cs`); driver built unmodified from source and redistributed with its MIT license (`installer/driver/Build-Driver.ps1`, ADR-0005 phase 2) | 2026-09-26 |
| parsec-vdd | https://github.com/nomi-san/parsec-vdd | MIT wrapper; driver Parsec-owned | facts | 2026-09-26 |
| Amyuni usbmmidd | https://www.amyuni.com | commercial | facts | 2026-09-26 |
| VertoXR v0.2.10+18 (installed files) | https://vertoxr.com | proprietary | facts (file names/strings only; no decompilation) | 2026-09-26 |
| VR-Compare XREAL 1S | https://vr-compare.com/headset/xreal1s | web page | facts | 2026-09-26 |
| Tom's Guide XREAL 1S review | https://www.tomsguide.com/computing/smart-glasses/xreal-1s-review | web page | facts | 2026-09-26 |
| XREAL ultrawide mode tutorial | https://tutorials.xreal.com/docs/glasses/one-series/osd/ultrawide-mode/ | web page | facts | 2026-09-26 |
| Madgwick 2010, "An efficient orientation filter for inertial and inertial/magnetic sensor arrays" | https://x-io.co.uk/open-source-imu-and-ahrs-algorithms/ | paper | algorithm (own implementation) | 2026-09-26 |
| github/awesome-copilot `skills/csharp-xunit` | https://github.com/github/awesome-copilot/tree/main/skills/csharp-xunit | MIT | imported → `.claude/skills/csharp-xunit/SKILL.md` | 2026-09-26 |
| `csharp-async` skill | likely github/awesome-copilot `skills/csharp-async` (same name/format) | MIT (verify origin) | imported | 2026-09-26 |
| `csharp-developer` skill | https://github.com/Jeffallan (per SKILL.md metadata) | MIT (per frontmatter) | imported | 2026-09-26 |
| `winui-app` skill | see `.claude/skills/winui-app/` | Apache-2.0 (`LICENSE.txt`) | imported | 2026-09-26 |
| XREAL tutorials: One-series OSD (UltraWide, Stabilizer) + One-series user guide | https://tutorials.xreal.com/docs/glasses/one-series/osd/stabilizer/ , https://us.shop.xreal.com/blogs/buying-guide/user-guide_xreal-one-series | vendor docs | facts (OSD usage) | 2026-09-26 |
| FalkForge 0.5.0-beta.8 (NuGet `FalkForge.Core`, `FalkForge.Compiler.Msi`, tool `FalkForge.Tool`; docs + source `877eec7`) | https://github.com/Falkesand/FalkForge | FSL-1.1 (Apache-2.0 after 2 years; building installers for own software permitted) | build tool for the MSI (ADR-0013); nothing of it ships in the MSI | 2026-10-11 |

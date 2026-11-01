# External sources

Every external source consulted. **How used:** `facts` (IDs/constants/behaviour only), `reference` (read for understanding, no code), `code-ported` (code adapted — must also appear in THIRD-PARTY-NOTICES.md), `imported` (file copied verbatim). Policy: ADR-0006.

| Source | URL | License | How used | Accessed |
|--------|-----|---------|----------|----------|
| XREAL SDK com.xreal.xr 3.1.0 | local `E:\XREAL SDK\com.xreal.xr.tar` | none shipped; license-key gated | facts (capture/Blend Mode code read as reference 2026-10-31, `xreal-sdk-reference.md`) | 2026-09-26 |
| XREAL SDK com.xreal.xr.experimental 3.1.0 | local `E:\XREAL SDK\com.xreal.xr.experimental.tar` | none shipped | facts (C# function names/enums, DLL strings, 2026-10-29; all C#/Unity code + samples + shaders read as reference for behaviour, described in own words in `xreal-sdk-reference.md`, 2026-10-31; no code copied, no disassembly; DLLs never shipped or loaded) | 2026-10-29 |
| Skarian/one-xr | https://github.com/Skarian/one-xr | MIT | code-ported (stream framer `OneReportFramer.cs`; control-port framing `ControlWire.cs`, 2026-10-29); reference for axes/units | 2026-09-26 |
| SamiMitwalli/One-Pro-IMU-Retriever-Demo | https://github.com/SamiMitwalli/One-Pro-IMU-Retriever-Demo | MIT | reference | 2026-09-26 |
| Aloim Grayscale-Feed | https://github.com/Aloim/Grayscale-Feed-Xreal-One-Pro-Eye-Windows-Nebula-Beta-needed- | research-only | facts (port list; README claims about One Pro feed format and prerequisites, 2026-10-29) | 2026-09-26 |
| XREAL developer docs: Camera, Access RGB Camera | https://docs.xreal.com/Camera/Camera | web page | facts (SDK camera API is Unity/Android only) | 2026-10-29 |
| XREAL tutorials: Eye specs/FAQ, One-series Gesture Control pages | https://tutorials.xreal.com/docs/accessories/eye/spec, https://tutorials.xreal.com/docs/glasses/one-series/gesture/gesture-setting/ | web page | facts (Eye specs, gesture OSD switch, pinch thresholds) | 2026-10-29 |
| Google MediaPipe Hand Landmarker model (float16, version 1) | https://ai.google.dev/edge/mediapipe/solutions/vision/hand_landmarker | Apache-2.0 | model: downloaded and converted to ONNX by `tools/Convert-HandModels.ps1` (weights unchanged, not in git; shipped later with LICENSE); graph parameters (palm anchors/decoding, hand-region rules, input range 0..1) re-implemented in `src/XrealScreen.Vision/Hands/PalmDecoder.cs`, `HandRoi.cs` (code-ported, 2026-10-31); 2026-10-29 hand test (Python, not in the repo) | 2026-10-29 |
| Casiez, Roussel, Vogel 2012, "1€ Filter" (CHI) | https://gery.casiez.net/1euro/ | paper | algorithm (own implementation, `OneEuroFilter.cs`) | 2026-10-31 |
| onnx/tensorflow-onnx (tf2onnx) 1.17.0, TensorFlow 2.21.0 | https://github.com/onnx/tensorflow-onnx | Apache-2.0 | build tool for the model conversion only; nothing ships | 2026-10-31 |
| Microsoft.ML.OnnxRuntime 1.31.1 | https://github.com/microsoft/onnxruntime | MIT | NuGet dependency of `XrealScreen.Vision` | 2026-10-31 |
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

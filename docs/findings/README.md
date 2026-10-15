# Findings

Facts gathered by research and hardware tests. Every claim carries a tag:

| Tag | Meaning |
|-----|---------|
| `[verified-hw]` | Observed on real glasses by us (note date, device, firmware) |
| `[verified-local]` | Observed on the development PC |
| `[from-source:MIT]` | Read in a permissively licensed source (listed in `docs/legal/SOURCES.md`) |
| `[from-GPL:facts-only]` | Fact (ID, port, constant) from a GPL source; no code used |
| `[from-sdk:facts-only]` | Fact from the XREAL SDK (no license; never redistributed) |
| `[hypothesis]` | Unverified; must be confirmed before relying on it |

When a hypothesis is confirmed or refuted, edit the line in place (change the tag, add date) — do not append contradicting notes.

| File | Topic |
|------|-------|
| [xreal-sdk.md](xreal-sdk.md) | XREAL Unity SDK 3.1.0: what is (not) usable |
| [xreal-1s-hardware.md](xreal-1s-hardware.md) | XREAL 1S specs and OSD features |
| [xreal-one-protocol.md](xreal-one-protocol.md) | One-series USB network protocol (IMU/control) |
| [vertoxr-cleanroom.md](vertoxr-cleanroom.md) | Competitor architecture, observed only |
| [virtual-display-drivers.md](virtual-display-drivers.md) | IddCx driver options |
| [capture-render.md](capture-render.md) | Capture and rendering pipeline |
| [local-environment.md](local-environment.md) | Dev machine toolchain |
| [arrealone-pc.md](arrealone-pc.md) | This PC: build, tests, and desktop capture |

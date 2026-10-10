# Architecture Decision Records

One file per decision: `ADR-NNNN-short-title.md`. Never rewrite an accepted ADR's decision; supersede it with a new ADR and set the old one's status to `Superseded by ADR-NNNN`.

| ADR | Title | Status |
|-----|-------|--------|
| [0001](ADR-0001-virtual-display-provider.md) | Virtual display provider: VirtualDrivers VDD behind `IVirtualDisplayProvider` | Superseded by 0008 |
| [0002](ADR-0002-tech-stack.md) | WinUI 3 + .NET 10 + Windows App SDK + CommunityToolkit.Mvvm + Vortice | Accepted |
| [0003](ADR-0003-operating-modes.md) | Two operating modes: native vs virtual workspace | Accepted |
| [0004](ADR-0004-service-scope.md) | User-session engine, elevated helper service only | Accepted |
| [0005](ADR-0005-packaging.md) | MSIX + bootstrapper | Accepted |
| [0006](ADR-0006-clean-room-licensing.md) | Clean-room and licensing policy | Accepted |
| [0007](ADR-0007-head-tracking-fusion.md) | Own Madgwick fusion + gyro bias + pose prediction | Accepted |
| [0008](ADR-0008-virtual-display-rs.md) | Virtual display provider: virtual-display-rs behind `IVirtualDisplayProvider` | Accepted |
| [0009](ADR-0009-presenter-pacing.md) | Presenter: DWM-composed window paced by the glasses' vblank | Accepted |
| [0010](ADR-0010-glasses-anchor-workspace.md) | Glasses-anchor workspace: glasses hold the image, app splits it into screens | Accepted |
| [0011](ADR-0011-head-look-for-games.md) | Head look for games via relative mouse input (no driver) | Accepted |
| [0012](ADR-0012-developer-sdk-and-local-api.md) | Developer SDK (NuGet) and local API (JSON-RPC over a named pipe) | Accepted |

## Template
```markdown
# ADR-NNNN: Title

- **Status:** Proposed | Accepted | Superseded by ADR-NNNN
- **Date:** YYYY-MM-DD

## Context
Problem, forces, constraints. Link findings.

## Decision
What we do, stated in one or two sentences first.

## Consequences
Positive, negative, follow-up work.

## Alternatives
Option — why rejected.
```

# ADR-0012: Developer SDK and local API

- **Status:** Accepted
- **Date:** 2026-10-10

## Context
The user wants developers to build on XrealScreen with a well-documented, tested API. There are two kinds of programs:
- Programs that want the glasses' head tracking themselves (a game mod, a visualizer, a research tool) and do not need XrealScreen running.
- Programs that want to drive the running app (start a workspace, read the pose the app already tracks, switch head look on for a game) from any language.

The glasses' IMU stream serves one reader at a time in practice, so a second program reading the glasses while the app runs a workspace would compete with it. ADR-0004 already planned a named-pipe IPC with versioned DTOs in `XrealScreen.Contracts`.

## Decision
Ship both, as two layers:

1. **`XrealScreen.Sdk`** (NuGet, `net10.0-windows10.0.26100.0`): in-process access to the glasses. `XrealGlasses` (pose stream with automatic reconnect, `LatestPose`, `Recenter`, any `IImuSource` for tests), `GlassesPose` (degrees, documented conventions), `HeadLookEngine` + `IMouseOutput` + `MouseInjector` (ADR-0011), `GlassesDisplay` (find the glasses monitor, make it primary with crash-safe restore, move a window onto it). Depends on the packages `XrealScreen.Core`, `.Device.XrealOne` and `.Display`. The head-look code moved here from `Host.Gaming`; the app and `xrs` use the SDK too.
2. **Local API** of the running app: **JSON-RPC 2.0**, one JSON object per line (UTF-8, `\n`), over the named pipe **`\\.\pipe\XrealScreen.Api`**, created with `PipeOptions.CurrentUserOnly`. 17 methods (`api.hello`, `status.*`, `pose.*`, `workspace.*`, `glasses.screenshot`, `headLook.*`) and two notifications (`pose`, `status`). Protocol types and framing in **`XrealScreen.Contracts`** (`net10.0`), server in `Host.Api.LocalApiServer` (app side behind `ILocalApiHandler`), .NET client **`XrealScreen.Sdk.Client`** (`net10.0`). On by default; Settings → Developers switches it off.

Reference: `docs/sdk/README.md`, `docs/sdk/dotnet-sdk.md`, `docs/sdk/local-api.md`.

## Consequences
- + Any language with named pipes and JSON works (Python, PowerShell samples verified against the app); .NET gets typed clients.
- + Security: a named pipe is not reachable from web pages (a localhost WebSocket or HTTP server would be, unless Origin checks and tokens are added), and `CurrentUserOnly` restricts it to the user who runs the app. Programs of that user can already control the desktop, so no extra authentication. Bad input never crashes the server: parse errors, 64 KiB line limit, unknown methods and handler exceptions become JSON-RPC errors (no stack traces).
- + Tested: SDK tests with the simulator (poses, recenter, reconnect, head look mouse output) and API tests end to end over real pipes (typed client, raw JSON-RPC lines, bad input, concurrency, disconnects).
- − `XrealScreen.Sdk` needs a Windows 10.0.26100 TFM; Unity (.NET Standard 2.1) cannot use the NuGet packages directly but can speak the JSON protocol.
- − Pose notifications only flow while the app tracks (a workspace or head look runs); `status.poseSource` says which.
- − Versioning: `ApiProtocol.Version` = 1. New methods or members are additive; a breaking change raises the version, and clients check `api.hello`.

## Alternatives
- **Localhost WebSocket/HTTP**: easiest from browsers, but every web page the user visits could connect; needs Origin checks + a token handshake. Rejected for v1.
- **gRPC over a pipe**: typed, but heavy for Python/PowerShell users and the app.
- **Only an SDK, no app API**: other languages and programs that cooperate with the running app would be left out.
- **Only an app API, no SDK**: programs that just want head tracking would need the whole app running.

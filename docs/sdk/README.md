# XrealScreen for developers

There are two ways to build on XrealScreen ([ADR-0012](../decisions/ADR-0012-developer-sdk-and-local-api.md)):

| | [.NET SDK](dotnet-sdk.md) | [Local API](local-api.md) |
|---|---|---|
| What | Read the glasses yourself, in your process | Control the running XrealScreen app |
| Needs the app | No | Yes, running, with Settings → Developers on |
| Languages | .NET 10 (`XrealScreen.Sdk`) | Any language: JSON-RPC over the named pipe `\\.\pipe\XrealScreen.Api`. For .NET there is `XrealScreen.Sdk.Client`. |
| Head pose | ~1000 Hz, full control (recenter, raw tracker pose) | Up to 250 Hz, while the app runs a workspace or head look |
| Also | Head-to-mouse for games, glasses monitor helpers | Start/stop workspaces, head look on/off and settings, glasses screenshots, status events |
| Use it for | A game mod, a visualizer, a research tool, your own head tracking | Scripts and tools that work together with the app: hotkeys, stream decks, automation |

You can't use both on the same glasses at the same time, because the glasses' head tracking serves one reader. While the app tracks, read its poses through the local API.

## Quick start
**.NET SDK:** read the glasses directly.
```powershell
dotnet run --project samples/HeadPose.Console -- --simulate     # no glasses needed
dotnet run --project samples/HeadPose.Console                   # real glasses
```
**Local API:** control the app. Start XrealScreen first.
```powershell
dotnet run --project samples/AppClient.Console                  # status, workspaces, settings
dotnet run --project samples/AppClient.Console -- poses         # 10 s of head poses
python samples/python/xrealscreen_api.py
.\samples\powershell\XrealScreenApi.ps1 -Method workspace.list
```

## Packages
| Package | Target | Contents |
|---|---|---|
| `XrealScreen.Sdk` | net10.0-windows10.0.26100.0 | `XrealGlasses`, `GlassesPose`, `HeadLookEngine`, `GlassesDisplay` |
| `XrealScreen.Sdk.Client` | net10.0 | `XrealScreenClient`, a typed client of the local API |
| `XrealScreen.Contracts` | net10.0 | Local API message types, error codes, framing |
| `XrealScreen.Core`, `.Device.XrealOne`, `.Display` | net10.0-windows10.0.26100.0 | Dependencies of the SDK: tracking math, IMU transport, display topology |

The packages are not on nuget.org yet. Build them yourself:
```powershell
.\tools\Pack-Sdk.ps1                       # runs the SDK/API tests, then packs into artifacts\nuget
dotnet nuget add source "$PWD\artifacts\nuget" --name XrealScreenLocal
```
If you rebuild with the same version, clear the cached copy with `dotnet nuget locals global-packages --clear`, or pass `-Version 0.1.1`.

## Quality
- **API reference:** every public member of `XrealScreen.Sdk`, `XrealScreen.Contracts` and `XrealScreen.Sdk.Client` has XML documentation, and the build fails if one is missing. IntelliSense shows it.
- **SDK tests:** `tests/XrealScreen.Sdk.Tests` uses the simulator to test poses and conventions, recenter, reconnect, the single-reader rule, and head-look mouse output in both modes.
- **API tests:** `tests/XrealScreen.Api.Tests` runs the server and client end to end over real named pipes. It covers the typed client, raw JSON-RPC lines (the way Python sends them), parse errors, too-long lines, wrong versions, bad params, handler crashes, concurrent and slow requests, several clients, pose and status subscriptions, and disconnects. It also tests the line framing on its own.
- Run them: `dotnet test --solution XrealScreen.slnx`.

## Security
- The local API pipe accepts only the Windows user who runs XrealScreen (`CurrentUserOnly`).
- Web pages cannot open named pipes. A localhost HTTP or WebSocket server could be reached from any web page, which is why the API doesn't use one.
- The API can switch the primary monitor and move the mouse (head look), but it cannot run programs or read files. Switch it off under Settings → Developers.
- Bad input cannot crash the app. Errors come back as JSON-RPC errors without stack traces.

## Versioning
SDK packages use semantic versioning, starting at 0.1.0 (preview). The local API reports `apiVersion` 1 in `api.hello`. New methods and members are additive. A breaking change raises `apiVersion`.

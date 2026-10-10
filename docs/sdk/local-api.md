# XrealScreen local API

Control the running **XrealScreen** app from your own program: read the head pose, start and stop workspaces, switch head look for games on and off, take screenshots of the glasses. Any language that can open a Windows named pipe and read and write JSON can use it. .NET programs can use the **`XrealScreen.Sdk.Client`** package instead of writing JSON by hand.

To read the glasses without the app, use the .NET SDK instead ([dotnet-sdk.md](dotnet-sdk.md)). For help choosing, see [README.md](README.md).

- [Connecting](#connecting)
- [Message format](#message-format)
- [Methods](#methods)
- [Notifications](#notifications)
- [Errors](#errors)
- [Clients: .NET, Python, PowerShell](#clients)
- [Conventions and limits](#conventions-and-limits)

## Connecting

| | |
|---|---|
| Pipe | `\\.\pipe\XrealScreen.Api` |
| Who can connect | Only the Windows user who runs XrealScreen. Web pages cannot open named pipes. |
| Switch | XrealScreen → **Settings → Developers → Let programs on this PC control XrealScreen**. On by default. |
| Clients | Up to 8 at the same time. |

If the pipe does not exist, either XrealScreen is not running or the API is switched off.

Start every connection with `api.hello`. It returns the API version, so your program can check it is compatible.

## Message format

[JSON-RPC 2.0](https://www.jsonrpc.org/specification). Each message is **one line** of UTF-8 JSON ending with `\n`. Never put a raw newline inside a message; JSON escapes newlines inside strings anyway. A line may be up to 64 KiB long.

Request (you to the app):
```json
{"jsonrpc":"2.0","id":1,"method":"status.get"}
```
Response (the app to you, same `id`):
```json
{"jsonrpc":"2.0","id":1,"result":{"workspace":{"state":"stopped"},"headLook":{"running":false,"enabled":false,"connected":false},"glasses":{"displayConnected":true,"displayName":"XREAL 1S","model":"XReal1S"},"poseSource":"none"}}
```
Error response:
```json
{"jsonrpc":"2.0","id":2,"error":{"code":-32001,"message":"No saved workspace \"Nope\"."}}
```
Notification (the app to you, no `id`; only after you subscribe):
```json
{"jsonrpc":"2.0","method":"pose","params":{"timestampNs":95123000000,"yaw":12.5,"pitch":-3.1,"roll":0.4,"orientation":{"x":0.0,"y":0.027,"z":0.108,"w":0.994},"angularVelocity":{"x":0.1,"y":-0.4,"z":20.2},"source":"headLook"}}
```

- `id` may be a number or a string. Requests run **concurrently**, so responses can arrive in a different order than you sent the requests. Match them by `id`.
- Members whose value is `null` are left out. Parameter and result member names are camelCase.
- If you send a request without `id` (a JSON-RPC notification), the app does not answer. Don't use this; it gives you no way to know whether the call worked.

## Methods

| Method | Params | Result |
|---|---|---|
| [`api.hello`](#apihello) | `{ clientName? }` | `{ apiVersion, appVersion, methods[] }` |
| [`status.get`](#statusget) | – | [Status](#status-object) |
| [`status.subscribe`](#statussubscribe--statusunsubscribe) | – | `{ ok }`, then `status` notifications |
| `status.unsubscribe` | – | `{ ok }` |
| [`pose.subscribe`](#posesubscribe--poseunsubscribe) | `{ rateHz? }` | `{ ok }`, then `pose` notifications |
| `pose.unsubscribe` | – | `{ ok }` |
| [`workspace.list`](#workspacelist) | – | `{ names[] }` |
| [`workspace.start`](#workspacestart) | `{ name? }` | `{ ok }` |
| [`workspace.stop`](#workspacestop) | – | `{ ok }` |
| [`workspace.recenter`](#workspacerecenter) | – | `{ ok }` |
| [`glasses.screenshot`](#glassesscreenshot) | – | `{ path }` |
| [`headLook.start`](#headlookstart) | – | `{ ok }` |
| [`headLook.stop`](#headlookstop) | – | `{ ok }` |
| [`headLook.setEnabled`](#headlooksetenabled) | `{ enabled }` | `{ ok }` |
| [`headLook.recenter`](#headlookrecenter) | – | `{ ok }` |
| [`headLook.getSettings`](#headlookgetsettings--headlooksetsettings) | – | [Head look settings](#head-look-settings-object) |
| [`headLook.setSettings`](#headlookgetsettings--headlooksetsettings) | any [settings](#head-look-settings-object) members | all settings |

### api.hello
Handshake. `clientName` is optional and appears in the app log on the Home page.
```json
→ {"jsonrpc":"2.0","id":1,"method":"api.hello","params":{"clientName":"My tool"}}
← {"jsonrpc":"2.0","id":1,"result":{"apiVersion":1,"appVersion":"0.1.0.0","methods":["api.hello","status.get", …]}}
```
`apiVersion` changes only for breaking changes. New methods and new members can appear in any version, so ignore members you don't know.

### status.get
Returns the current [status](#status-object).

#### Status object
| Member | Type | Meaning |
|---|---|---|
| `workspace.state` | string | `stopped`, `starting`, `running`, `stopping`, `faulted` |
| `workspace.name` | string? | Saved workspace last loaded or started |
| `workspace.message` | string? | The status line from the Home page, e.g. why a start failed |
| `headLook.running` | bool | Head look is started (it reads the glasses) |
| `headLook.enabled` | bool | Mouse output is on |
| `headLook.connected` | bool | Head-look poses are arriving from the glasses |
| `glasses.displayConnected` | bool | The glasses are connected as a monitor |
| `glasses.displayName` | string? | e.g. `XREAL 1S` |
| `glasses.model` | string? | `One`, `OnePro`, `XReal1S` |
| `poseSource` | string | Where `pose` notifications come from now: `none`, `workspace`, `headLook` |

### status.subscribe / status.unsubscribe
After `status.subscribe` the app sends a `status` notification right away, then one each time the status changes. The app checks for changes 4 times per second. Use this to react when a workspace finishes starting or someone presses Ctrl+Alt+H.

### pose.subscribe / pose.unsubscribe
```json
→ {"jsonrpc":"2.0","id":5,"method":"pose.subscribe","params":{"rateHz":60}}
```
`rateHz` can be 1–250 and defaults to 60. The app then sends `pose` notifications at up to that rate, but only **new** poses, and only while it is tracking the head:
- a **workspace** runs (`poseSource: "workspace"`), or
- **head look** runs (`poseSource: "headLook"`). Head look works without a workspace, and its mouse output is off until `headLook.setEnabled`. Start it if you only want poses.

When nothing is tracking, no notifications arrive, and the subscription stays active until you unsubscribe. A new `pose.subscribe` replaces the old one with the new rate.

### workspace.list
Names of the saved workspaces (Home page → Save).

### workspace.start
Starts a workspace and returns when it runs. That usually takes 2–5 seconds, because virtual monitors are created.
- `{}` or no params: the latest settings.
- `{"name":"Desk"}`: loads the saved workspace "Desk" first, like the Home page's Load.

Errors: `-32000` a workspace is already running or starting; `-32001` no such saved workspace, or the glasses are not in the UltraWide mode a Glasses anchor workspace needs (the app waits for it; see `message`); `-32002` the start failed (`message` says why). If head look is running, it is stopped first.

### workspace.stop
Stops the workspace and returns after the monitor layout is restored. Does nothing if no workspace is running.

### workspace.recenter
Recenters the running workspace (App head tracking). Error `-32000` if no workspace is running.

### glasses.screenshot
Saves what the glasses show to `Pictures\XrealScreen\XrealScreen <date time>.png` and returns `{"path": "..."}`. It works whenever the glasses are connected as a monitor. Error `-32001` if they are not.

### headLook.start
Starts head look (the Games page's **Start head look**). Mouse output stays **off**; switch it on with `headLook.setEnabled` or Ctrl+Alt+H in the game. If the setting *glasses primary* is on, the glasses become the primary monitor until head look stops. Error `-32000` while a workspace runs. Does nothing if head look already runs.

### headLook.stop
Stops head look and restores the monitor layout. Does nothing if head look is not running.

### headLook.setEnabled
```json
→ {"jsonrpc":"2.0","id":9,"method":"headLook.setEnabled","params":{"enabled":true}}
```
Switches the mouse output on or off. Switching it on recenters. Error `-32000` if head look is not started; `-32602` if `enabled` is missing or not a boolean.

### headLook.recenter
Joystick mode: the current head direction becomes the center. Error `-32000` if head look is not started.

### headLook.getSettings / headLook.setSettings
`setSettings` changes only the members you send and returns all settings. Out-of-range values are clamped to the Games page slider ranges. Changes apply immediately, also during a game, and are saved.
```json
→ {"jsonrpc":"2.0","id":10,"method":"headLook.setSettings","params":{"mode":"joystick","stickSpeed":2000}}
← {"jsonrpc":"2.0","id":10,"result":{"mode":"joystick","sensitivity":20,"verticalScale":1,"invertY":false,"deadband":0.5,"stickDeadZone":5,"stickFull":25,"stickSpeed":2000,"glassesPrimary":true}}
```

#### Head look settings object
| Member | Range | Meaning |
|---|---|---|
| `mode` | `look`, `joystick` | Look: the camera turns as much as your head. Joystick: turning away from center keeps turning. |
| `sensitivity` | 1–100 | Look: mouse steps per degree of head rotation |
| `verticalScale` | 0–2 | Up/down strength; 0 = left/right only |
| `invertY` | bool | Invert up/down |
| `deadband` | 0–3 | Look: ignore head rates below this many degrees per second |
| `stickDeadZone` | 0–30 | Joystick: center zone in degrees |
| `stickFull` | 5–60 | Joystick: degrees from center for full speed |
| `stickSpeed` | 100–5000 | Joystick: mouse steps per second at full speed |
| `glassesPrimary` | bool | Make the glasses the primary monitor while head look runs (applies at the next start) |

## Notifications

| Method | Params | When |
|---|---|---|
| `pose` | [Pose](#pose-object) | After `pose.subscribe`, while the app tracks the head |
| `status` | [Status](#status-object) | After `status.subscribe`: once right away, then on each change |

#### Pose object
| Member | Unit | Meaning |
|---|---|---|
| `timestampNs` | ns | Glasses clock (monotonic) |
| `yaw` | degrees | + = turned **left** of the center |
| `pitch` | degrees | + = looking **up**. Gravity-referenced, does not drift. |
| `roll` | degrees | + = tilted toward the **right** shoulder |
| `orientation` | unit quaternion `{x,y,z,w}` | Head orientation relative to the center. Right-handed, Z-up frame: X forward, Y left, Z up. |
| `angularVelocity` | degrees/s `{x,y,z}` | Rotation speed in the head frame (z = turning left) |
| `source` | | `workspace` or `headLook` |

The center is where the head pointed at the last recenter (`workspace.recenter`, `headLook.recenter`, or the start).

## Errors

| Code | Name | Meaning |
|---|---|---|
| -32700 | Parse error | The line is not valid JSON or is longer than 64 KiB. `id` is `null`. The connection stays open. |
| -32600 | Invalid request | `jsonrpc` is not `"2.0"` |
| -32601 | Method not found | Unknown method |
| -32602 | Invalid params | Missing or wrong params (e.g. `rateHz` out of range, unknown `mode`) |
| -32603 | Internal error | Unexpected error in the app; `message` has the reason |
| -32000 | Conflict | Not possible right now (e.g. head look while a workspace runs) |
| -32001 | Unavailable | Something needed is missing (no glasses, no such workspace, app closing) |
| -32002 | Failed | The operation was tried and failed; `message` says why |

## Clients

### .NET: `XrealScreen.Sdk.Client`
```xml
<PackageReference Include="XrealScreen.Sdk.Client" Version="0.1.0" />
```
Targets `net10.0`.
```csharp
using XrealScreen.Contracts;
using XrealScreen.Sdk.Client;

await using var app = await XrealScreenClient.ConnectAsync("My tool");     // throws XrealScreenApiException(-32001) if the app is not running
Console.WriteLine($"API v{app.ServerInfo.ApiVersion}");

var status = await app.GetStatusAsync();
await app.StartWorkspaceAsync("Desk");                                       // returns when it runs
await app.StopWorkspaceAsync();

await app.StartHeadLookAsync();                                              // poses without a workspace; mouse off
await app.SetHeadLookSettingsAsync(new HeadLookSettings { Mode = HeadLookModes.Joystick });
await foreach (var pose in app.ReadPosesAsync(rateHz: 30, cancellationToken))
{
    Console.WriteLine($"{pose.Yaw:F1} {pose.Pitch:F1}");
}

app.StatusChanged += (_, s) => Console.WriteLine(s.Workspace.State);       // events run on a background thread
await app.SubscribeStatusAsync();

try { await app.RecenterWorkspaceAsync(); }
catch (XrealScreenApiException ex) when (ex.Code == ApiErrorCodes.Conflict) { /* no workspace running */ }
```
- All methods are thread-safe and can run at the same time.
- `ReadPosesAsync` subscribes, yields poses (a slow consumer loses the oldest ones) and unsubscribes when you stop. Only one may run at a time; use the `PoseReceived` event for more listeners.
- `Disconnected` is raised when the app exits or switches the API off. After that, calls throw with code -32001.
- `CallRawAsync(method, params)` calls methods that are newer than the client.

Full sample: [`samples/AppClient.Console`](../../samples/AppClient.Console/Program.cs).

### Python (standard library)
[`samples/python/xrealscreen_api.py`](../../samples/python/xrealscreen_api.py) is a single-file client you can copy.
```python
from xrealscreen_api import XrealScreenApi

with XrealScreenApi("my script") as api:
    print(api.call("status.get")["workspace"]["state"])
    api.call("headLook.start")
    api.call("pose.subscribe", {"rateHz": 10})
    for _ in range(50):
        message = api.next_notification()
        if message["method"] == "pose":
            print(message["params"]["yaw"])
```
A synchronous pipe handle in Python cannot read and write at the same time, so the sample client is single-threaded. `call()` keeps notifications that arrive before its response, and `next_notification()` returns them first. `next_notification()` blocks until a notification arrives, so only wait for poses while something is tracking. For asynchronous reading, use one thread per direction on two handles, or `asyncio` with `ProactorEventLoop.create_pipe_connection`.

### PowerShell
[`samples/powershell/XrealScreenApi.ps1`](../../samples/powershell/XrealScreenApi.ps1) makes one call per run:
```powershell
.\samples\powershell\XrealScreenApi.ps1                                   # status.get
.\samples\powershell\XrealScreenApi.ps1 -Method workspace.start -Params @{ name = 'Desk' }
.\samples\powershell\XrealScreenApi.ps1 -Method headLook.setEnabled -Params @{ enabled = $true }
```

### Other languages
Open `\\.\pipe\XrealScreen.Api` for reading and writing, write one JSON request per line, and read lines. Node.js: `net.connect('\\\\.\\pipe\\XrealScreen.Api')` plus `readline`. C/C++: `CreateFileW` + `ReadFile`/`WriteFile`. Unity (Mono, .NET Standard 2.1): `System.IO.Pipes.NamedPipeClientStream` plus any JSON library.

## Conventions and limits
- **Units:** degrees, degrees per second, nanoseconds.
- **Rates:** at most 250 pose notifications per second per client. The glasses deliver about 1000 poses per second; the app sends the newest one at your rate.
- **Message size:** 64 KiB per line.
- **One tracker:** the glasses' IMU serves one reader. While the app tracks, programs using `XrealScreen.Sdk` directly should not read the glasses at the same time; read the app's poses through this API instead.
- **Threads:** commands run like the matching buttons in the app, one after the other on the app's UI thread. Status and pose reads never wait for the UI.
- **Versioning:** `apiVersion` 1. Additive changes keep the version.

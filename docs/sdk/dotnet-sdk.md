# XrealScreen.Sdk for .NET

Head tracking from **XREAL One / One Pro / 1S** glasses in your own .NET program on Windows. You don't need the XrealScreen app. The package also includes head look for games (head movement to mouse movement) and helpers for the glasses monitor.

To control the *running* XrealScreen app instead (workspaces, its head look, its pose stream), use the [local API](https://github.com/nikryden/Screen-XREAL-1S/blob/main/docs/sdk/local-api.md). For help choosing, see [README.md](https://github.com/nikryden/Screen-XREAL-1S/blob/main/docs/sdk/README.md).

```xml
<PackageReference Include="XrealScreen.Sdk" Version="0.1.0" />
```
- **Target framework:** `net10.0-windows10.0.26100.0` or later.
- **Platform:** Windows 11 (x64 or ARM64).
- **Dependencies:** the packages `XrealScreen.Core`, `XrealScreen.Device.XrealOne` and `XrealScreen.Display`.

## Glasses setup
- Connect the glasses with USB-C (DisplayPort alt mode). Windows then shows a display named "XREAL 1S" (or One / One Pro) and a USB network adapter. The SDK reads the head tracking from the glasses at `169.254.2.1` over that adapter.
- Right after you plug in the glasses, their network takes about a minute to come up. The SDK keeps retrying, so you don't need to handle that.
- Only one program should read the glasses at a time. Don't run an XrealScreen workspace or the app's head look while your program reads them.

## Head pose
```csharp
using XrealScreen.Sdk;

var glasses = new XrealGlasses();                          // default address 169.254.2.1
glasses.ConnectionChanged += (_, e) =>
    Console.WriteLine(e.IsConnected ? "connected" : $"waiting: {e.Error}");

await foreach (var pose in glasses.ReadPosesAsync(cancellationToken))
{
    // ~1000 poses per second
    Console.WriteLine($"yaw {pose.Yaw:F1}  pitch {pose.Pitch:F1}  roll {pose.Roll:F1}");
}
```

### `XrealGlasses`
| Member | |
|---|---|
| `XrealGlasses()` / `XrealGlasses(XrealGlassesOptions)` | Real glasses. Options: `Host` (default `169.254.2.1`), `ConnectTimeout` (3 s), `RetryDelay` (2 s). |
| `XrealGlasses(IImuSource, TimeSpan? retryDelay)` | Any IMU source: a recording, the simulator, your own device. |
| `ReadPosesAsync(ct)` | Streams poses until `ct` is cancelled, **reconnecting by itself**. It never throws for connection problems. Only one may run at a time (`InvalidOperationException`). |
| `LatestPose` | The newest pose (null before the first). Thread-safe: read it from a render loop while another thread runs `ReadPosesAsync`. |
| `Recenter()` | The current head direction becomes yaw 0 on the next sample. Thread-safe. |
| `IsConnected`, `ConnectionChanged` | Connection state. The event runs on the reading thread, once when poses start arriving and once per outage. |
| `SampleCount`, `SourceName` | Diagnostics. |

### `GlassesPose`
| Member | Unit | Meaning |
|---|---|---|
| `Yaw` | degrees | + = turned **left** of the center |
| `Pitch` | degrees | + = looking **up**. Gravity-referenced, does not drift. |
| `Roll` | degrees | + = tilted toward the **right** shoulder |
| `Orientation` | `Quaternion` | Relative to the center. Right-handed, Z-up frame: X forward, Y left, Z up. |
| `WorldOrientation` | `Quaternion` | Absolute; the yaw origin is arbitrary and a recenter does not change it |
| `AngularVelocity` | degrees/s | Gyro-bias-corrected, head frame (Z = turning left) |
| `TimestampNs` | ns | Glasses clock |
| `Tracker` | `HeadPose` | The raw tracker pose, for `HeadMouseMapper` and other `XrealScreen.Core` APIs |

The first pose sets the center. Yaw drifts slowly, because the glasses have no magnetometer (gyro bias is estimated while the head is still). Pitch and roll do not drift. Call `Recenter()` when the user looks straight ahead.

## Head look for games
Turns head movement into relative mouse movement (Win32 `SendInput`, no driver), so any game with mouse look can be steered with the head. It is verified in Skyrim.
```csharp
using XrealScreen.Core.Gaming;
using XrealScreen.Sdk;

var engine = new HeadLookEngine(new XrealGlasses())
{
    Settings = new HeadMouseSettings { Mode = HeadMouseMode.Look, CountsPerDegree = 20 },
};
engine.Message += Console.WriteLine;                      // connection, hotkeys
engine.Enabled = true;                                     // or Ctrl+Alt+H in the game
await engine.RunAsync(cancellationToken);
```
- **Modes:** `Look` sends `CountsPerDegree` mouse steps per degree; tune it until a 90° head turn turns the camera 90°. `Stick` (joystick) turns at a speed that depends on how far the head is from center (`StickDeadZoneDegrees`, `StickFullDegrees`, `StickMaxCountsPerSecond`).
- **Hotkeys:** while running, Ctrl+Alt+H toggles on/off, Ctrl+Alt+C recenters, and Ctrl+Alt+G moves the active window to the glasses. They are polled, so other programs keep these keys. Set `PollHotkeys = false` to turn them off.
- `Settings` and `Enabled` can be changed while it runs. `Sends` and `Rejected` count mouse moves. Windows refuses moves into a game that runs as administrator, unless your program does too.
- **Testing:** pass your own `IMouseOutput` to record the mouse moves instead of moving the real mouse.
- To use the mapping in your own loop, `HeadMouseMapper` (in `XrealScreen.Core.Gaming`) turns `pose.Tracker` into a `MouseDelta`.

## Glasses monitor
```csharp
if (GlassesDisplay.Find() is { } monitor)
    Console.WriteLine($"{monitor.FriendlyName} at {monitor.Bounds}, {monitor.RefreshHz} Hz, primary: {monitor.IsPrimary}");

// Games open on the primary monitor: make it the glasses until the session is disposed.
await using (var primary = await GlassesDisplay.MakePrimaryAsync())   // null if the glasses already are primary
{
    // run the game …
}

GlassesDisplay.MoveForegroundWindowToGlasses();             // borderless windows fill the glasses
```
`MakePrimaryAsync` first saves the current layout to `%LOCALAPPDATA%\XrealScreen\topology-snapshot.json`. If your program crashes, the XrealScreen app (at its next start) or `xrs recover` restores the layout. It throws `InvalidOperationException` if the glasses are not connected or a workspace owns the snapshot.

## Without glasses: tests and demos
```csharp
using XrealScreen.Device.Simulated;   // project reference; not part of the NuGet packages

var glasses = new XrealGlasses(new SyntheticImuSource(new SyntheticImuOptions
{
    Motion = SyntheticMotion.YawSweep, RealTime = true, DurationSeconds = 3600,
}));
```
You can implement `XrealScreen.Core.Abstractions.IImuSource` yourself to replay your own data. A finite source looks like a disconnect when it ends, and `ReadPosesAsync` then retries.

## Threads
- `ReadPosesAsync` and `HeadLookEngine.RunAsync` run on whatever thread awaits them. Events are raised on that thread.
- `LatestPose`, `Recenter()`, `HeadLookEngine.Settings` and `HeadLookEngine.Enabled` are safe to use from any thread.

## Troubleshooting
| Symptom | Cause |
|---|---|
| `waiting: … operation was canceled` for about a minute after plugging in | The glasses' USB network is still starting. Wait. |
| Never connects | The XrealScreen app (workspace or head look) or another tool is reading the glasses. Or the cable is not data-capable: check that a "UsbNcm Host Device" network adapter exists. |
| Mouse moves are rejected | The game runs as administrator. Run your program as administrator too. |
| The game opens on the wrong monitor | Use `GlassesDisplay.MakePrimaryAsync()` before starting the game. |

Sample: [`samples/HeadPose.Console`](https://github.com/nikryden/Screen-XREAL-1S/blob/main/samples/HeadPose.Console/Program.cs) (`--simulate` works without glasses). Source and issues: https://github.com/nikryden/Screen-XREAL-1S

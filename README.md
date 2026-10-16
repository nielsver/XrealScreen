# XrealScreen

![Two virtual screens in the glasses — Visual Studio on the left, a console on the right](images/glasses-workspace.png)

*A glasses-anchor workspace. Black areas are see-through in the glasses.*

Turn XREAL One, One Pro, and 1S glasses into a Windows 11 multi-monitor workspace. The glasses can hold up to six virtual screens still, or the app can move curved screens with your head. They are real Windows monitors. Stopping the app, or recovering after a crash, puts your previous monitors and refresh rates back.

## Set up

You need Windows 11 and the glasses on USB-C. Windows has to treat the glasses as an extended monitor, not a duplicate of the laptop. Press Win+P and choose **Extend**.

### Install

The [Releases](https://github.com/nielsver/XrealScreen/releases) page has a pre-release installer, `XrealScreen-0.1.1-x64.msi`. A release can be older than the `main` branch. This page describes the current source.

1. Run the MSI. Windows asks for administrator approval.
2. Leave **Virtual display driver** checked the first time you install. The extra monitors come from that driver. The installer trusts its test certificate on this PC. You do not turn on Windows test signing, and you do not disable driver signature enforcement.
3. Start **XrealScreen** from the Start menu.

If a virtual-display driver is already installed, the installer keeps it.

Building from source is in [Build from source](#build-from-source) below. That path needs the .NET SDK. The installed app does not.

### On the glasses

The X button on the glasses changes the picture. One click switches between **Anchor** and **Follow**. A double-click opens the menu. Under **Spatial Screen** you set **UltraWide** and **Stabilizer**.

| You want | Turn on | Turn off |
| --- | --- | --- |
| Glasses anchor (everyday) | **Anchor**. **UltraWide** 32:9 (3840×1080, the full width), or 21:9, or 16:18. | Leave Stabilizer as you like. The glasses hold the image. |
| Head tracking | **Follow**. **UltraWide 32:9** so the picture is 3840×1080. | **Stabilizer**. If it stays on, the glasses and the app both move the picture. |

32:9 runs at 60 Hz. UltraWide Off is a narrower 1920×1200 picture, so the screens stop before the side edges.

Head tracking also needs the small USB network link inside the cable (the glasses at `169.254.2.1`). If the app cannot see motion, unplug the glasses and plug them back in. Brightness and the electrochromic dimmer use that same link.

### In the app

On **Screens**, pick the workspace type, then press **Start**.

Choices you can change, all optional:

- **How many screens.** One, two, or three stay in one row. Choose **Grid**, then add a fourth, fifth, or sixth screen. Those sit in rows of three. **Arc** keeps every screen in one row and stops at three in glasses anchor. Press **Apply screen settings** after a size or count change.
- **Size.** Width and height at 100% fill the glasses picture. Lower them to leave space. **Space between screens** is the seam. **Space around the screens** is a border on every side. Glasses anchor also draws a color frame around the screens.
- **Where new windows go.** The primary-monitor box chooses the taskbar screen while the workspace runs. **Move windows from the glasses display** brings windows off the glasses onto that screen at start.
- **Send a window.** While the workspace is running, Ctrl+Alt+1 through 6 sends the window you are using to that screen. Buttons for screens 4, 5, and 6 appear when those screens exist. Ctrl+Alt+0 puts the cursor on the main virtual screen.
- **Colors, brightness, dimmer.** On the Screens page in glasses anchor. Brightness and dimmer are sent to the glasses. They need the USB link.
- **Head tracking feel.** On the Tracking page: distance, which axes follow your head, the neck model, and the app's own stabilizer. That stabilizer is separate from the glasses Stabilizer switch.
- **Look.** Settings → Look: Stage (default), Lens, Instrument, or Classic.
- **Start by itself.** Settings can start the app when you sign in, start a saved workspace when the app opens, and start one when the glasses are plugged in. Close the window to keep the app in the tray. **Exit** in the tray menu quits.
- **Local API.** Settings can let other programs on this PC talk to the app. Leave it off if you do not want that.
- **Games.** The Games page turns your head into mouse movement. It does not run at the same time as a workspace. Ctrl+Alt+H turns it on or off.

Ctrl+Alt+W starts or stops the workspace from anywhere. Ctrl+Alt+Q stops it while you are looking at the glasses picture. Ctrl+Alt+P saves a screenshot to `Pictures\XrealScreen`. In head tracking, Ctrl+Alt+R recenters and Ctrl+Alt+Plus / Minus moves the screens closer or farther.

If the app closes in the middle of a workspace, run `xrs recover` (installed next to the app) or start XrealScreen again. Recovery puts monitors and refresh rates back.

## What you get

- **Glasses anchor.** The glasses hold one UltraWide image still. XrealScreen splits it into real screens at the glasses' own pixels. One to three screens are one row. Four, five, or six use a grid of three across. This is the coding layout.
- **App head tracking.** Up to six curved screens in one row or in a grid of three. The app reads the glasses' IMU at about 1000 Hz. At 100% width and height the screens share the full 3840×1080 picture.
- **Real Windows monitors.** You drag windows onto them the same way as a physical display. A new app opened while the pointer is on a virtual screen opens there. A window that was already open stays where it is.
- **Saved workspaces.** Save, load, delete, import, and export from Home. Settings reload at start.
- **Four looks.** Stage, Lens, Instrument, and Classic.
- **A command-line tool and an API.** `xrs` lists monitors and recovers a crashed session. The local API is documented in [docs/sdk/local-api.md](docs/sdk/local-api.md).

## How it works

The glasses connect over USB-C. Windows sees a normal monitor for the picture. Head tracking arrives on the glasses' USB network adapter. No XREAL Eye camera is used.

Extra monitors come from [virtual-display-rs](https://github.com/MolotovCherry/virtual-display-rs) 0.4, an Indirect Display Driver. XrealScreen captures them with Windows.Graphics.Capture and draws them with Direct3D 11. The driver package is signed with a test certificate. Installing it trusts that certificate in the machine Root and TrustedPublisher stores. The private key is not in git.

Architecture: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Build from source

Requirements: Windows 11, .NET SDK 10.0.401 or newer, Windows SDK 10.0.26100.

The source app is an MSIX package. `dotnet run` needs **Developer Mode** (Settings → System → For developers). The MSI build does not. Do not rebuild the app project while a workspace is running.

```powershell
dotnet build XrealScreen.slnx -p:Platform=x64
dotnet test --solution XrealScreen.slnx -p:Platform=x64
dotnet run --project src/XrealScreen.App -p:Platform=x64
```

The virtual display driver is built with `installer\driver\Build-Driver.ps1` and installed with `installer\Install-Driver.ps1`. See [installer/README.md](installer/README.md).

```powershell
dotnet run --project tools/XrealScreen.Cli -p:Platform=x64 -- display list
dotnet run --project tools/XrealScreen.Cli -p:Platform=x64 -- recover
```

## Screenshots

**Looks.** Pick one under Settings.

| Stage (default) | Lens | Instrument |
| --- | --- | --- |
| ![Stage look: screens in a dark room, with a dock for Start, Recenter, workspace type and number of screens](images/look-stage.png) | ![Lens look: round Start button and live yaw, pitch, and roll](images/look-lens.png) | ![Instrument look: degree-scale field map and presets](images/look-instrument.png) |

The pages below use the Classic look.

| Home | Screens |
| --- | --- |
| ![Home page: start, stop, saved workspaces, layout preview](images/home.png) | ![Screens page: workspace type, split preview, primary monitor](images/screens.png) |

| Tracking | Settings |
| --- | --- |
| ![Tracking page: axes, stabilizer, neck model, auto center](images/tracking.png) | ![Settings page: automatic start, tray, theme](images/settings.png) |

**Games**

<img src="images/games.png" width="49%" alt="Games page: head look, Look or Joystick mode, sensitivity">

## Developers

- [.NET SDK](docs/sdk/dotnet-sdk.md) (`XrealScreen.Sdk`): pose stream at about 1000 Hz. The app does not have to be running.
- [Local API](docs/sdk/local-api.md): JSON-RPC 2.0 on `\\.\pipe\XrealScreen.Api`. Turn it on under Settings if you want it.
- Samples live in [`samples/`](samples/).

```csharp
var glasses = new XrealGlasses();
await foreach (var pose in glasses.ReadPosesAsync(ct))
    Console.WriteLine($"yaw {pose.Yaw:F1}  pitch {pose.Pitch:F1}");
```

## License

MIT. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Not affiliated with or endorsed by XREAL.

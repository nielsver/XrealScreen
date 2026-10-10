using System.CommandLine;
using System.Globalization;
using XrealScreen.Core.Gaming;
using XrealScreen.Device.XrealOne;
using XrealScreen.Host.Gaming;

namespace XrealScreen.Cli;

/// <summary><c>xrs imu mouse</c>: head look for games via relative mouse input (ADR-0011).</summary>
internal static class HeadMouseCommands
{
    public static Command Create()
    {
        var mode = new Option<HeadMouseMode>("--mode") { Description = "Look = head turns the camera 1:1; Stick = head offset sets a turn speed (joystick).", DefaultValueFactory = _ => HeadMouseMode.Look };
        var sens = new Option<float>("--sens") { Description = "Look: mouse counts per degree of head rotation.", DefaultValueFactory = _ => 20f };
        var vertical = new Option<float>("--vertical") { Description = "Vertical multiplier (0 = horizontal only).", DefaultValueFactory = _ => 1f };
        var invertY = new Option<bool>("--invert-y") { Description = "Look up moves the mouse down." };
        var deadband = new Option<float>("--deadband") { Description = "Look: ignore head rates below this, °/s.", DefaultValueFactory = _ => 0.5f };
        var deadZone = new Option<float>("--stick-deadzone") { Description = "Stick: degrees from center that do nothing.", DefaultValueFactory = _ => 5f };
        var full = new Option<float>("--stick-full") { Description = "Stick: degrees from center for full speed.", DefaultValueFactory = _ => 25f };
        var speed = new Option<float>("--stick-speed") { Description = "Stick: mouse counts per second at full speed.", DefaultValueFactory = _ => 1500f };
        var keepPrimary = new Option<bool>("--keep-primary") { Description = "Do not make the glasses the primary monitor (games open on the primary monitor)." };
        var host = new Option<string>("--host") { DefaultValueFactory = _ => OneEndpoints.Default.Host };
        var command = new Command("mouse", "Head look for games: glasses rotation → mouse movement. Makes the glasses the primary monitor until exit. Ctrl+Alt+H toggles on/off (starts off), Ctrl+Alt+C recenters, Ctrl+Alt+G moves the active window to the glasses; Ctrl+C quits.")
        {
            mode, sens, vertical, invertY, deadband, deadZone, full, speed, keepPrimary, host,
        };
        command.SetAction(async (parse, ct) =>
        {
            var settings = new HeadMouseSettings
            {
                Mode = parse.GetValue(mode),
                CountsPerDegree = parse.GetValue(sens),
                VerticalScale = parse.GetValue(vertical),
                InvertY = parse.GetValue(invertY),
                DeadbandDegreesPerSecond = parse.GetValue(deadband),
                StickDeadZoneDegrees = parse.GetValue(deadZone),
                StickFullDegrees = parse.GetValue(full),
                StickMaxCountsPerSecond = parse.GetValue(speed),
            };
            var source = new XrealOneImuSource(new XrealOneImuOptions { Endpoints = OneEndpoints.Default with { Host = parse.GetValue(host)! } });
            GlassesPrimarySession? primary = null;
            if (!parse.GetValue(keepPrimary))
            {
                try
                {
                    primary = await GlassesPrimarySession.StartAsync(ct).ConfigureAwait(false);
                    Console.WriteLine(primary is null ? "The glasses are already the primary monitor." : "The glasses are now the primary monitor (restored when xrs exits; after a crash: xrs recover).");
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Console.Error.WriteLine($"Primary monitor not changed: {ex.Message}");
                }
            }

            try
            {
                return await RunAsync(source, settings, ct).ConfigureAwait(false);
            }
            finally
            {
                if (primary is not null)
                {
                    await primary.DisposeAsync().ConfigureAwait(false);
                    Console.WriteLine("Monitor layout restored.");
                }
            }
        });
        return command;
    }

    private static async Task<int> RunAsync(XrealOneImuSource source, HeadMouseSettings settings, CancellationToken ct)
    {
        var engine = new HeadLookEngine(source) { Settings = settings };
        engine.Message += Console.WriteLine;
        Console.WriteLine($"Source: {engine.SourceName}  mode={settings.Mode}");
        Console.WriteLine("Switch to the game, then press Ctrl+Alt+H to start head look (again to pause). Ctrl+Alt+C recenters. Ctrl+Alt+G moves the active (windowed/borderless) game to the glasses. Ctrl+C quits.");
        var run = engine.RunAsync(ct);
        try
        {
            while (!run.IsCompleted)
            {
                await Task.WhenAny(run, Task.Delay(1000, ct)).ConfigureAwait(false);
                if (engine.Enabled && engine.IsConnected)
                {
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"yaw={engine.YawDegrees,7:F1} pitch={engine.PitchDegrees,7:F1} sends={engine.Sends} rejected={engine.Rejected}"));
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }

        await run.ConfigureAwait(false);
        if (engine.Rejected > 0)
        {
            Console.WriteLine("Some mouse moves were rejected: is the game running as administrator? Then run xrs as administrator too.");
        }

        return 0;
    }
}

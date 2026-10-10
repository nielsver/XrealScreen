using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using XrealScreen.Core.Gaming;
using XrealScreen.Core.Tracking;
using XrealScreen.Device.XrealOne;
using XrealScreen.Host.Gaming;

namespace XrealScreen.Cli;

/// <summary><c>xrs imu mouse</c>: head look for games via relative mouse input (ADR-0011).</summary>
internal static class HeadMouseCommands
{
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12; // Alt
    private const int VkH = 0x48;
    private const int VkC = 0x43;
    private static readonly long MinSendIntervalTicks = Stopwatch.Frequency / 500; // ≤ 500 SendInput calls/s

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
        var host = new Option<string>("--host") { DefaultValueFactory = _ => OneEndpoints.Default.Host };
        var command = new Command("mouse", "Head look for games: glasses rotation → mouse movement. Ctrl+Alt+H toggles on/off (starts off), Ctrl+Alt+C recenters; Ctrl+C quits.")
        {
            mode, sens, vertical, invertY, deadband, deadZone, full, speed, host,
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
            return await RunAsync(source, settings, ct).ConfigureAwait(false);
        });
        return command;
    }

    private static async Task<int> RunAsync(XrealOneImuSource source, HeadMouseSettings settings, CancellationToken ct)
    {
        var tracker = new HeadTracker();
        var mapper = new HeadMouseMapper { Settings = settings };
        bool enabled = false;
        bool toggleWasDown = false;
        bool recenterWasDown = false;
        int pendingX = 0, pendingY = 0;
        long lastSend = 0, nextKeyPoll = 0, nextStatus = 0, sent = 0, rejected = 0;

        Console.WriteLine($"Source: {source.Name}  mode={settings.Mode}");
        Console.WriteLine("Switch to the game, then press Ctrl+Alt+H to start head look (again to pause). Ctrl+Alt+C recenters. Ctrl+C quits.");
        try
        {
            await foreach (var sample in source.ReadAsync(ct).ConfigureAwait(false))
            {
                var pose = tracker.Update(sample);
                long now = Stopwatch.GetTimestamp();

                if (now >= nextKeyPoll)
                {
                    nextKeyPoll = now + Stopwatch.Frequency / 50;
                    bool chord = MouseInjector.IsKeyDown(VkControl) && MouseInjector.IsKeyDown(VkMenu);
                    bool toggleDown = chord && MouseInjector.IsKeyDown(VkH);
                    bool recenterDown = chord && MouseInjector.IsKeyDown(VkC);
                    if (toggleDown && !toggleWasDown)
                    {
                        enabled = !enabled;
                        mapper.Reset();
                        pendingX = pendingY = 0;
                        Console.WriteLine(enabled ? "head look ON" : "head look OFF");
                    }

                    if (recenterDown && !recenterWasDown)
                    {
                        mapper.Reset();
                        Console.WriteLine("recentered");
                    }

                    toggleWasDown = toggleDown;
                    recenterWasDown = recenterDown;
                }

                if (!enabled)
                {
                    continue;
                }

                var delta = mapper.Update(pose);
                pendingX += delta.X;
                pendingY += delta.Y;
                if ((pendingX != 0 || pendingY != 0) && now - lastSend >= MinSendIntervalTicks)
                {
                    if (MouseInjector.Move(new MouseDelta(pendingX, pendingY)))
                    {
                        sent++;
                    }
                    else
                    {
                        rejected++;
                    }

                    pendingX = pendingY = 0;
                    lastSend = now;
                }

                if (now >= nextStatus)
                {
                    nextStatus = now + Stopwatch.Frequency;
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"yaw={pose.YawDegrees,7:F1} pitch={pose.PitchDegrees,7:F1} sends={sent} rejected={rejected}"));
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        if (rejected > 0)
        {
            Console.WriteLine("Some mouse moves were rejected: is the game running as administrator? Then run xrs as administrator too.");
        }

        return 0;
    }
}

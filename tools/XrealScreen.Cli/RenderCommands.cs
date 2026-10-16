using System.CommandLine;
using XrealScreen.Core.Tracking;
using XrealScreen.Core.Workspace;
using XrealScreen.Host;

namespace XrealScreen.Cli;

/// <summary>Runs a workspace session (same engine as the app) from the command line.</summary>
internal static class RenderCommands
{
    public static Command Create()
    {
        var kind = new Option<WorkspaceKind>("--workspace") { Description = "AppTracking (curved, head-tracked by the app) | GlassesAnchor (glasses hold the image; set OSD Anchor + UltraWide).", DefaultValueFactory = _ => WorkspaceKind.AppTracking };
        var gap = new Option<int>("--gap") { Description = "Glasses anchor: pixels between the screens.", DefaultValueFactory = _ => 16 };
        var aspect = new Option<AnchorAspect>("--aspect") { Description = "Glasses anchor: Ratio16x9 | Ratio16x10 | Fill.", DefaultValueFactory = _ => AnchorAspect.Fill };
        var screens = new Option<int>("--screens") { Description = "Number of virtual monitors (1-6).", DefaultValueFactory = _ => 3 };
        var mode = new Option<UltrawideMode>("--mode") { Description = "Virtual monitor size: Off (16:9), Wide21x9, Wide32x9, Tall16x18.", DefaultValueFactory = _ => UltrawideMode.Off };
        var source = new Option<TrackingSource>("--source") { Description = "Head tracking: Glasses | Simulated | Fixed (no tracking, diagnostic).", DefaultValueFactory = _ => TrackingSource.Glasses };
        var seconds = new Option<double>("--seconds") { Description = "Stop after this long (Ctrl+Alt+Q stops earlier).", DefaultValueFactory = _ => 120 };
        var follow = new Option<bool>("--follow") { Description = "Auto center: follow the head beyond a 20° dead-zone." };
        var hz = new Option<int>("--hz") { Description = "Glasses refresh to request (UltraWide Off offers 60/90/120).", DefaultValueFactory = _ => 120 };
        var predict = new Option<double>("--predict-ms") { Description = "Pose prediction ahead of the late latch.", DefaultValueFactory = _ => 20 };
        var lead = new Option<double>("--latch-lead-ms") { Description = "Late latch: read the pose this many ms before the next glasses vblank (0 = right after vblank).", DefaultValueFactory = _ => 0 };
        var beta = new Option<float>("--beta") { Description = "Madgwick tilt-correction gain.", DefaultValueFactory = _ => 0.02f };
        var gate = new Option<float>("--accel-gate") { Description = "Skip tilt correction when | |a| - g | exceeds this (m/s²); 0 = off.", DefaultValueFactory = _ => 0.6f };
        var rateGate = new Option<float>("--rate-gate") { Description = "Skip tilt correction while the head turns faster than this (rad/s; stops up/down bobbing when nodding); 0 = off.", DefaultValueFactory = _ => 0.09f };
        var stabilize = new Option<string>("--stabilize") { Description = "Pose stabilizer: off | balanced | strong | ultra.", DefaultValueFactory = _ => "strong" };
        var axes = new Option<TrackingAxes>("--axes") { Description = "Head rotations that move the screens: YawRoll (level, no up/down) | YawOnly | YawPitch | Full.", DefaultValueFactory = _ => TrackingAxes.YawRoll };
        var neck = new Option<bool>("--neck-model") { Description = "Neck model: screens come closer when you lean/nod in.", DefaultValueFactory = _ => true };
        var syncDesktop = new Option<bool>("--sync-desktop") { Description = "Set other monitors to the glasses refresh for the session (avoids judder; restored on exit).", DefaultValueFactory = _ => true };
        var command = new Command("render", "Show virtual monitors fixed in space in the glasses (Ctrl+Alt+R recenter, Ctrl+Alt+Plus/Minus closer/farther, Ctrl+Alt+Q stop).")
        {
            screens, mode, source, seconds, follow, hz, predict, beta, gate, stabilize, syncDesktop, neck, axes, kind, gap, aspect, lead, rateGate,
        };
        command.SetAction(async (parse, ct) =>
        {
            var options = new WorkspaceOptions
            {
                ScreenCount = parse.GetValue(screens),
                Mode = parse.GetValue(mode),
                Source = parse.GetValue(source),
                AutoCenter = new AutoCenterSettings { Policy = parse.GetValue(follow) ? RecenterPolicy.Follow : RecenterPolicy.Manual },
                GlassesRefreshHz = parse.GetValue(hz),
                PredictMs = parse.GetValue(predict),
                LatchLeadMs = parse.GetValue(lead),
                FilterBeta = parse.GetValue(beta),
                AccelGate = parse.GetValue(gate),
                RateGate = parse.GetValue(rateGate),
                Stabilizer = parse.GetValue(stabilize)!,
                SyncDesktopRefresh = parse.GetValue(syncDesktop),
                NeckModel = parse.GetValue(neck),
                Axes = parse.GetValue(axes),
                Kind = parse.GetValue(kind),
                AnchorGapPixels = parse.GetValue(gap),
                AnchorAspect = parse.GetValue(aspect),
            };
            return await RunAsync(options, parse.GetValue(seconds), ct).ConfigureAwait(false);
        });
        return command;
    }

    private static async Task<int> RunAsync(WorkspaceOptions options, double seconds, CancellationToken ct)
    {
        await using var engine = new WorkspaceEngine();
        engine.Log += (_, message) => Console.WriteLine(message);
        try
        {
            await engine.StartAsync(options, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or IOException or TimeoutException)
        {
            Console.Error.WriteLine($"could not start: {ex.Message}");
            return 2;
        }

        try
        {
            await Task.WhenAny(engine.Completion, Task.Delay(TimeSpan.FromSeconds(seconds), ct)).ConfigureAwait(false);
        }
        finally
        {
            await engine.StopAsync().ConfigureAwait(false);
        }

        if (engine.LastSummary is { } summary)
        {
            Console.WriteLine($"captured frames={summary.CapturedFrames}; imu samples={summary.ImuSamples}; final yaw={summary.FinalPose.YawDegrees:F1}° pitch={summary.FinalPose.PitchDegrees:F1}° bias={summary.GyroBias}");
        }

        return engine.LastError is null ? 0 : 1;
    }
}

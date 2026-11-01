using System.CommandLine;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using XrealScreen.Display;
using XrealScreen.Host.Hands;
using XrealScreen.Sdk;
using XrealScreen.Vision.Hands;

namespace XrealScreen.Cli;

/// <summary>`xrs hands control` and `xrs hands targets` (M9 E3): hand control with calibration and the 40 px target check.</summary>
internal static class HandControlCommands
{
    private const int VkControl = 0x11;
    private const int VkAlt = 0x12;
    private const int VkJ = 0x4A;

    /// <summary>Fits worse than this (screen units) are rejected; a good calibration is below ~0.05.</summary>
    private const double MaxCalibrationError = 0.1;

    /// <summary>XREAL Eye grayscale image size (port 52997).</summary>
    private const int EyeWidth = 504;
    private const int EyeHeight = 378;

    /// <summary>Every screen corner must be reachable with the knuckle at least this far (camera pixels) inside the image.</summary>
    private const double CameraEdgeMargin = 25;

    private static readonly (double U, double V)[] CalibrationTargets = [(0.15, 0.15), (0.85, 0.15), (0.85, 0.85), (0.15, 0.85), (0.5, 0.5)];

    public static Command CreateControl()
    {
        var calibrate = new Option<bool>("--calibrate") { Description = "Calibrate first (5 dots on the glasses; point and pinch on each). Saved for next time." };
        var seconds = new Option<double>("--seconds") { Description = "How long to run (0 = until Ctrl+C).", DefaultValueFactory = _ => 0 };
        var models = HandsCommands.ModelsOption();
        var log = LogOption();
        var click = ClickOption();
        var command = new Command("control", "Hand control: the hand moves the pointer on the glasses; click by pinching or holding still (dwell); pinch-hold = drag, thumb+middle pinch moved up/down = scroll. Ctrl+Alt+J pauses.") { calibrate, seconds, models, log, click };
        command.SetAction((parse, ct) => RunAsync(parse.GetValue(models), parse.GetValue(calibrate), parse.GetValue(seconds), targets: 0, targetSize: 0, parse.GetValue(log), parse.GetValue(click), ct));
        return command;
    }

    public static Command CreateTargets()
    {
        var count = new Option<int>("--count") { Description = "Targets to hit.", DefaultValueFactory = _ => 10 };
        var size = new Option<int>("--size") { Description = "Target diameter in pixels (E3 check: 40).", DefaultValueFactory = _ => 40 };
        var calibrate = new Option<bool>("--calibrate") { Description = "Calibrate first (always done when no calibration is saved)." };
        var models = HandsCommands.ModelsOption();
        var log = LogOption();
        var click = ClickOption();
        var command = new Command("targets", "E3 check: click targets on the glasses with hand control; reports hits, misses and time.") { count, size, calibrate, models, log, click };
        command.SetAction((parse, ct) => RunAsync(parse.GetValue(models), parse.GetValue(calibrate), 0, parse.GetValue(count), parse.GetValue(size), parse.GetValue(log), parse.GetValue(click), ct));
        return command;
    }

    private static Option<ClickMode> ClickOption() => new("--click")
    {
        Description = "How to click: Pinch, Dwell (hold the pointer still 0.8 s) or Both.",
        DefaultValueFactory = _ => ClickMode.Both,
    };

    private static Option<string> LogOption() => new("--log")
    {
        Description = "Write one CSV line per frame (state, pinch ratio 2D/3D, pointer; numbers only, no image).",
    };

    private static async Task<int> RunAsync(string? modelsOption, bool calibrate, double seconds, int targets, int targetSize, string? logPath, ClickMode click, CancellationToken ct)
    {
        string? models = HandsCommands.ResolveModels(modelsOption);
        if (models is null || !HandTracker.ModelsExist(models))
        {
            Console.WriteLine("Hand models not found. Run .\\tools\\Convert-HandModels.ps1 (or pass --models).");
            return 2;
        }

        var glasses = GlassesDisplayLocator.FindGlasses(new CcdDisplayTopology().GetActiveMonitors());
        var area = glasses is null
            ? System.Windows.Forms.Screen.PrimaryScreen!.Bounds
            : new Rectangle(glasses.X, glasses.Y, glasses.Resolution.Width, glasses.Resolution.Height);
        Console.WriteLine(glasses is null ? $"Glasses monitor not found; using the primary monitor {area}." : $"Target area: glasses monitor {area}.");

        var store = new HandCalibrationStore();
        var saved = store.Load();
        if (saved is not null && (saved.RmsError > MaxCalibrationError || !saved.Calibration.CoversScreenInsideCamera(EyeWidth, EyeHeight, CameraEdgeMargin)))
        {
            // a bad calibration can even swap the axes (user, E3 test 2026-11-01): never use it
            Console.WriteLine($"Saved calibration ignored (error {saved.RmsError:F3}); using the default mapping.");
            saved = null;
        }

        var session = new Session(targets, targetSize) { Calibrating = calibrate || (targets > 0 && saved is null) };
        Console.WriteLine(saved is null ? "No saved calibration." : $"Calibration from {saved.Created:yyyy-MM-dd HH:mm} (error {saved.RmsError:F3}).");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (seconds > 0)
        {
            cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        }

        // while calibrating, the ring follows the default mapping, never an old calibration
        var engine = new HandControlEngine(new HandControlOptions
        {
            ModelDirectory = models,
            TargetArea = area,
            Calibration = session.Calibrating ? HandCalibration.Default(EyeWidth, EyeHeight) : saved?.Calibration,
            PointerOptions = new HandPointerOptions { Click = click },
        })
        {
            OutputEnabled = !session.Calibrating,
        };
        using var window = session.Calibrating || targets > 0 ? HandTargetWindow.Open(area, cts.Cancel) : null;
        session.Window = window;

        if (session.Calibrating)
        {
            session.ShowCalibrationTarget();
            Console.WriteLine("Calibrating: point at each green dot on the glasses and pinch.");
        }
        else if (targets > 0)
        {
            session.NextTarget();
        }

        if (window is not null && targets > 0)
        {
            window.Clicked += (_, hit) => session.OnClick(hit, cts);
        }

        long lastPrint = 0, frames = 0, found = 0;
        bool buttonDown = false;
        using var csv = logPath is null ? null : new StreamWriter(logPath);
        csv?.WriteLine("unixSeconds,deviceSeconds,state,found,pinch2d,pinch3d,presence,action,u,v,screenX,screenY");
        var clock = Stopwatch.StartNew();
        engine.FrameProcessed += f =>
        {
            if (f.State == HandControlState.Reconnecting)
            {
                Console.WriteLine("Camera stream lost; reconnecting (is the XREAL Eye attached and the glasses awake?).");
                return;
            }

            frames++;
            found += f.Hand is null ? 0 : 1;
            buttonDown = f.Output.Action switch { PointerAction.ButtonDown => true, PointerAction.ButtonUp => false, _ => buttonDown && f.Hand is not null };
            window?.SetHandPoint(f.ScreenPoint is { } sp ? new Point(sp.X - area.X, sp.Y - area.Y) : null, f.Output.DwellProgress);
            window?.SetHand(f.Camera, f.Hand, f.Hand?.PinchRatio ?? float.NaN, buttonDown, engine.Calibration);
            csv?.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0:F3},{(f.Hand?.TimestampNs ?? 0) / 1e9:F3},{f.State},{(f.Hand is null ? 0 : 1)},{f.Hand?.PinchRatio ?? float.NaN:F3},{f.Hand?.PinchRatio3D ?? float.NaN:F3},{f.Hand?.Presence ?? 0:F2},{f.Output.Action},{f.Output.U:F4},{f.Output.V:F4},{f.ScreenPoint?.X},{f.ScreenPoint?.Y}"));
            if (session.Calibrating && session.OnCalibrationFrame(f.Hand, f.Output.Action) is { } points)
            {
                var fit = HandCalibration.Fit(points);
                double rms = fit.RmsError(points);
                bool reachable = f.Camera is not { } cam || fit.CoversScreenInsideCamera(cam.Width, cam.Height, CameraEdgeMargin);
                if (rms > MaxCalibrationError || !reachable)
                {
                    // a wrong point (double pinch, pinch at the wrong dot) skews the whole screen: start over (E3 test 2026-11-01)
                    Console.WriteLine($"Calibration rejected ({(reachable ? $"error {rms:F3} > {MaxCalibrationError}" : "a screen corner is outside the camera view")}); starting again.");
                    session.RestartCalibration();
                    return;
                }

                store.Save(new HandCalibrationStore.Saved(fit, rms, DateTimeOffset.Now, points.Count));
                engine.Calibration = fit;
                Console.WriteLine($"Calibration saved (error {rms:F3}{(rms > 0.05 ? " - high, consider calibrating again" : "")}) to {store.Path}.");
                if (targets > 0)
                {
                    session.NextTarget();
                }
                else
                {
                    window?.Dispose();
                }

                engine.OutputEnabled = true;
            }

            if (clock.ElapsedMilliseconds - lastPrint >= 1000)
            {
                lastPrint = clock.ElapsedMilliseconds;
                Console.WriteLine($"  {clock.Elapsed.TotalSeconds,6:F1} s  {f.State,-17}  found {100.0 * found / Math.Max(1, frames),5:F1} %  {f.ProcessMs,4:F1} ms  pointer {(f.ScreenPoint is { } p ? $"{p.X},{p.Y}" : "-")}");
                frames = found = 0;
            }
        };

        var hotkeys = PollHotkeyAsync(engine, session, cts.Token);
        Console.WriteLine("Hand control running. Ctrl+Alt+J pauses/resumes, Ctrl+C (or Esc in the target window) stops.");
        int exit = 0;
        try
        {
            await engine.RunAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Camera stream ended: {ex.Message}. Is the XREAL Eye attached?");
            exit = 3;
        }

        await cts.CancelAsync().ConfigureAwait(false);
        await hotkeys.ConfigureAwait(false);
        if (targets > 0 && exit == 0)
        {
            Console.WriteLine(session.Result());
            exit = session.Hits >= targets && session.Misses <= 1 ? 0 : 4;
        }

        return exit;
    }

    private static async Task PollHotkeyAsync(HandControlEngine engine, Session session, CancellationToken ct)
    {
        bool wasDown = false;
        while (!ct.IsCancellationRequested)
        {
            bool down = MouseInjector.IsKeyDown(VkControl) && MouseInjector.IsKeyDown(VkAlt) && MouseInjector.IsKeyDown(VkJ);
            if (down && !wasDown && !session.Calibrating)
            {
                engine.OutputEnabled = !engine.OutputEnabled;
                Console.WriteLine(engine.OutputEnabled ? "Hand control ON (Ctrl+Alt+J)" : "Hand control OFF (Ctrl+Alt+J)");
            }

            wasDown = down;
            try
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Calibration and target-test state shared by the engine thread and the window thread.</summary>
    private sealed class Session(int targets, int targetSize)
    {
        private readonly object _gate = new();
        private readonly List<CalibrationPoint> _points = [];
        private readonly Queue<(double T, float X, float Y)> _anchors = new();
        private readonly Random _random = new(7);
        private readonly Stopwatch _targetClock = new();
        private readonly List<double> _times = [];

        private double _lastPointSeconds;
        private bool _openedSincePoint;

        public volatile bool Calibrating;

        public HandTargetWindow? Window { get; set; }

        public void RestartCalibration()
        {
            _points.Clear();
            _openedSincePoint = false;
            Calibrating = true;
            ShowCalibrationTarget();
            Window?.SetText("That calibration was not accurate. Again: calibration 1 of 5 - point your knuckle at the green dot, then pinch.");
        }

        public int Hits { get; private set; }

        public int Misses { get; private set; }

        public void ShowCalibrationTarget()
        {
            var (u, v) = CalibrationTargets[_points.Count];
            Window?.ShowTarget(u, v, 36, Color.LimeGreen);
            Window?.SetText($"Calibration {_points.Count + 1} of {CalibrationTargets.Length}: hold your hand relaxed, point the white-ring knuckle at the green dot, then pinch or hold still until the ring fills.");
        }

        /// <summary>Feeds a frame during calibration; returns all points once the last dot was pinched.</summary>
        public List<CalibrationPoint>? OnCalibrationFrame(HandResult? hand, PointerAction action)
        {
            // a release can come in a frame without a hand (hand briefly lost): it still counts as "opened"
            if (action == PointerAction.ButtonUp)
            {
                _openedSincePoint = true;
            }

            if (hand is null)
            {
                return null;
            }

            var (ax, ay) = HandPointer.Anchor(hand);
            double t = hand.TimestampNs / 1e9;
            _anchors.Enqueue((t, ax, ay));
            while (_anchors.Count > 0 && _anchors.Peek().T < t - 1)
            {
                _anchors.Dequeue();
            }

            // a point only counts after the hand opened again and 1 s passed, so a double pinch cannot take the next dot
            if (action != PointerAction.ButtonDown || (_points.Count > 0 && (!_openedSincePoint || t - _lastPointSeconds < 1.0)))
            {
                return null;
            }

            // median knuckle position over 0.3 s just before the pinch started (the hand moves while pinching)
            var window = _anchors.Where(a => a.T >= t - 0.43 && a.T <= t - 0.13).ToList();
            if (window.Count == 0)
            {
                window = [_anchors.First(a => a.T >= t - 0.13)];
            }

            float mx = window.Select(a => a.X).Order().ElementAt(window.Count / 2);
            float my = window.Select(a => a.Y).Order().ElementAt(window.Count / 2);
            var (u, v) = CalibrationTargets[_points.Count];
            _points.Add(new CalibrationPoint(mx, my, u, v));
            _lastPointSeconds = t;
            _openedSincePoint = false;
            Console.WriteLine($"  calibration point {_points.Count} of {CalibrationTargets.Length}");
            if (_points.Count < CalibrationTargets.Length)
            {
                ShowCalibrationTarget();
                return null;
            }

            Calibrating = false;
            return [.. _points];
        }

        public void NextTarget()
        {
            lock (_gate)
            {
                Window?.ShowTarget(0.1 + 0.8 * _random.NextDouble(), 0.2 + 0.7 * _random.NextDouble(), targetSize, Color.Orange);
                Window?.SetText(Status());
                _targetClock.Restart();
            }
        }

        public void OnClick(bool hit, CancellationTokenSource stop)
        {
            if (Calibrating)
            {
                return;
            }

            lock (_gate)
            {
                if (!hit)
                {
                    Misses++;
                    Console.WriteLine($"  miss {Misses}");
                    Window?.SetText(Status());
                    return;
                }

                Hits++;
                _times.Add(_targetClock.Elapsed.TotalSeconds);
                Console.WriteLine($"  hit {Hits}/{targets} after {_times[^1]:F1} s");
            }

            if (Hits >= targets)
            {
                Window?.ShowTarget(null, null, 0, Color.Orange);
                Window?.SetText($"Done: {Hits} hits, {Misses} misses. Closing...");
                stop.CancelAfter(TimeSpan.FromSeconds(2));
                return;
            }

            NextTarget();
        }

        public string Result() =>
            $"RESULT: {Hits} of {targets} targets hit, {Misses} misses" +
            (_times.Count > 0 ? $", median {_times.Order().ElementAt(_times.Count / 2):F1} s per target" : "") + $" (target {targetSize} px).";

        private string Status() => $"Target {Hits + 1} of {targets}: point at the orange dot, then pinch or hold still.   Hits {Hits}, misses {Misses}.   Esc stops.";
    }
}

using System.Diagnostics;
using System.Numerics;
using XrealScreen.Core.Abstractions;
using XrealScreen.Core.Tracking;
using XrealScreen.Core.Workspace;
using XrealScreen.Device.Simulated;
using XrealScreen.Device.XrealOne;
using XrealScreen.Display;
using XrealScreen.Display.VirtualDisplayRs;
using XrealScreen.Render;
using XrealScreen.Render.Capture;
using XrealScreen.Render.Presenter;
using XrealScreen.Render.Scene;

namespace XrealScreen.Host;

public enum WorkspaceState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted,
}

/// <summary>Why the last session ended.</summary>
public enum WorkspaceStopReason
{
    None,
    UserStopped,
    Error,

    /// <summary>The glasses changed resolution (e.g. OSD UltraWide switched) or were disconnected.</summary>
    GlassesDisplayChanged,
}

/// <summary>Summary of a finished session.</summary>
public sealed record WorkspaceSummary(PresenterStats? Render, long CapturedFrames, long ImuSamples, HeadPose FinalPose, Vector3 GyroBias);

/// <summary>
/// One virtual-workspace session: virtual monitors → desktop order → refresh sync → capture →
/// head tracking → head-tracked panels on the glasses. Always restores the desktop on stop,
/// error or cancellation. Runs in the user session (ADR-0004). One session at a time.
/// </summary>
public sealed class WorkspaceEngine : IAsyncDisposable
{
    private readonly Lock _poseLock = new();
    private CancellationTokenSource? _stopCts;
    private Task? _runTask;
    private TaskCompletionSource? _running;
    private HeadTracker? _tracker;
    private PoseStabilizer? _stabilizer;
    // Straight ahead until the first IMU sample arrives (a default HeadPose has a zero quaternion).
    private HeadPose _latest = new(0, Quaternion.Identity, Quaternion.Identity, Vector3.Zero);
    private WorkspaceState _state = WorkspaceState.Stopped;
    private Action<float>? _setDistance;

    /// <summary>Current viewing distance in metres (changes with Ctrl+Alt+Up/Down or <see cref="SetDistance"/>).</summary>
    public float DistanceMeters { get; private set; }

    /// <summary>Raised when the viewing distance changes (any thread).</summary>
    public event EventHandler<float>? DistanceChanged;

    /// <summary>Moves the screens closer/farther while running (0.5–4 m).</summary>
    public void SetDistance(float meters) => _setDistance?.Invoke(meters);

    /// <summary>Frame style and colors read every anchor frame. Null uses the options captured at start.</summary>
    public LiveBackdrop? LiveBackdrop { get; set; }

    private System.Drawing.Rectangle[] _virtualBounds = [];

    /// <summary>Desktop rectangles of the virtual screens, in screen-number order. Empty when the workspace is stopped.</summary>
    public IReadOnlyList<System.Drawing.Rectangle> VirtualScreenBounds => Volatile.Read(ref _virtualBounds);

    public event EventHandler<WorkspaceState>? StateChanged;

    /// <summary>Progress/diagnostic messages (any thread).</summary>
    public event EventHandler<string>? Log;

    public WorkspaceState State => _state;

    public WorkspaceSummary? LastSummary { get; private set; }

    public WorkspaceStopReason LastStopReason { get; private set; }

    public Exception? LastError { get; private set; }

    /// <summary>Completes when the current session ends (Stop, Ctrl+Alt+Q, error).</summary>
    public Task Completion => _runTask ?? Task.CompletedTask;

    /// <summary>Latest head pose (thread-safe copy).</summary>
    public HeadPose CurrentPose
    {
        get
        {
            lock (_poseLock)
            {
                return _latest;
            }
        }
    }

    public void Recenter()
    {
        _tracker?.RequestRecenter();
        _stabilizer?.Reset();
    }

    /// <summary>Starts a session; returns when the glasses show the workspace (or throws).</summary>
    public async Task StartAsync(WorkspaceOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_state is WorkspaceState.Starting or WorkspaceState.Running or WorkspaceState.Stopping)
        {
            throw new InvalidOperationException($"Workspace is {_state}.");
        }

        LastError = null;
        LastStopReason = WorkspaceStopReason.None;
        _stopCts = new CancellationTokenSource();
        _running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetState(WorkspaceState.Starting);
        _runTask = Task.Run(() => RunAsync(options, _stopCts.Token), CancellationToken.None);

        using var reg = cancellationToken.Register(() => _stopCts.Cancel());
        await Task.WhenAny(_running.Task, _runTask).ConfigureAwait(false);
        if (_state != WorkspaceState.Running)
        {
            await _runTask.ConfigureAwait(false); // wait for cleanup to finish
            throw LastError ?? new OperationCanceledException("Workspace start was cancelled.");
        }
    }

    public async Task StopAsync()
    {
        if (_runTask is null)
        {
            return;
        }

        if (_stopCts is not null)
        {
            await _stopCts.CancelAsync().ConfigureAwait(false);
        }

        await _runTask.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stopCts?.Dispose();
    }

    private void SetState(WorkspaceState state)
    {
        _state = state;
        StateChanged?.Invoke(this, state);
    }

    private void Info(string message) => Log?.Invoke(this, message);

    /// <summary>HMONITOR for a monitor. The top-left pixel can belong to the neighbour, so the centre is tried first.</summary>
    private static IntPtr MonitorHandle(DisplayMonitor monitor)
    {
        int cx = monitor.X + Math.Max(0, monitor.Resolution.Width / 2);
        int cy = monitor.Y + Math.Max(0, monitor.Resolution.Height / 2);
        IntPtr center = MonitorCapture.MonitorAt(cx, cy);
        return center != IntPtr.Zero ? center : MonitorCapture.MonitorAt(monitor.X, monitor.Y);
    }

    private Task RunAsync(WorkspaceOptions o, CancellationToken stop) =>
        o.Kind == WorkspaceKind.GlassesAnchor ? RunAnchorAsync(o, stop) : RunTrackingAsync(o, stop);

    /// <summary>
    /// Glasses-anchor workspace (ADR-0010): the glasses hold their ultrawide image still (X1 chip);
    /// we split it into 1–3 virtual monitors drawn pixel-exact. No head tracking, no refresh changes.
    /// </summary>
    private async Task RunAnchorAsync(WorkspaceOptions o, CancellationToken stop)
    {
        GlassesPresenter.EnablePerMonitorDpi();
        var topology = new CcdDisplayTopology();
        var store = new LayoutSnapshotStore();
        using var provider = new VirtualDisplayRsProvider(topology);
        var captures = new List<MonitorCapture>();
        long capturedFrames = 0;
        bool changedDesktop = false;
        try
        {
            var glasses = GlassesDisplayLocator.FindGlasses(topology.GetActiveMonitors())
                ?? throw new InvalidOperationException("Glasses monitor not found. Connect the glasses and use an extended display.");
            if (!AnchorSplit.IsUltrawideSignal(glasses.Resolution))
            {
                throw new InvalidOperationException(
                    $"The glasses send {glasses.Resolution}. In the glasses menu set Spatial Screen → UltraWide Mode to 32:9, 21:9 or 16:18, and single-click X for Anchor mode.");
            }

            if (!store.Exists)
            {
                await store.SaveAsync(topology.CaptureLayout(), stop).ConfigureAwait(false);
            }

            changedDesktop = true;
            // A frame leaves room for the color around the screens. See-through uses the whole image.
            FrameStyle frame = FrameStyles.Resolve(o.AnchorFrame, o.AnchorGradient);
            int framePx = frame == FrameStyle.SeeThrough ? 0 : FrameStyles.FramePixels;
            var signal = glasses.Resolution;
            var rects = AnchorSplit.Place(signal, o.ScreenCount, o.AnchorGapPixels, o.AnchorAspect, framePx);
            var virtuals = new List<VirtualDisplay>();
            foreach (var rect in rects)
            {
                var vd = await provider.AddAsync(new Resolution(rect.Width, rect.Height), 60, stop).ConfigureAwait(false);
                virtuals.Add(vd);
                Info($"virtual screen {virtuals.Count}: {vd.Resolution} → {vd.GdiDeviceName ?? "(not attached)"}");
            }

            await PrepareDesktopAsync(o, topology, virtuals, glasses, stop).ConfigureAwait(false);

            var monitors = topology.GetActiveMonitors();
            glasses = GlassesDisplayLocator.FindGlasses(monitors) ?? glasses;
            RememberVirtualScreens(virtuals, monitors);
            Info($"glasses: {glasses.Resolution} @ {glasses.RefreshHz} Hz — held still by the glasses (Anchor mode)");

            using var gd = GraphicsDevice.CreateForMonitor(MonitorCapture.MonitorAt(glasses.X, glasses.Y));
            var look = ScreenFrameColors.Create(o, rects, signal.Width, signal.Height);
            using var scene = new WorkspaceScene(gd)
            {
                GradientBackdrop = look.Draw,
                BackdropStyle = look.Style,
                BackdropScreenCount = look.ScreenCount,
                BackdropAxis = look.Axis,
                BackdropSplit1 = look.Split1,
                BackdropSplit2 = look.Split2,
                GradientLeft = look.A0,
                GradientRight = look.B0,
                BackdropColorA1 = look.A1,
                BackdropColorB1 = look.B1,
                BackdropColorA2 = look.A2,
                BackdropColorB2 = look.B2,
                Live = LiveBackdrop,
            };
            await MonitorCapture.RequestBorderlessAsync().ConfigureAwait(false);
            for (int i = 0; i < virtuals.Count; i++)
            {
                var m = monitors.FirstOrDefault(x => x.GdiDeviceName == virtuals[i].GdiDeviceName);
                if (m is null)
                {
                    Info($"virtual screen {i + 1} not attached; skipped");
                    continue;
                }

                var surface = scene.AddFlatScreen(rects[i], glasses.Resolution.Width, glasses.Resolution.Height, m.Resolution.Width, m.Resolution.Height);
                // A brand-new indirect monitor can be in the desktop topology before Windows.Graphics.Capture
                // will accept its HMONITOR (E_INVALIDARG). The point lookup also hits the glasses when the
                // new screen still shares their origin, so the device name is the handle we capture.
                MonitorCapture? capture = null;
                for (int attempt = 1; attempt <= 4 && capture is null; attempt++)
                {
                    IntPtr byPoint = MonitorHandle(m);
                    IntPtr byName = MonitorCapture.MonitorForDevice(m.GdiDeviceName);
                    IntPtr hmon = byName != IntPtr.Zero ? byName : byPoint;
                    Info($"capture {m.GdiDeviceName} at ({m.X},{m.Y}) {m.Resolution} byPoint=0x{byPoint:X} ({MonitorCapture.DeviceName(byPoint)}) byName=0x{byName:X} attempt {attempt}");
                    if (hmon == IntPtr.Zero)
                    {
                        await Task.Delay(400, stop).ConfigureAwait(false);
                        continue;
                    }

                    try
                    {
                        capture = new MonitorCapture(gd.Device, hmon);
                    }
                    catch (ArgumentException ex) when (attempt < 4)
                    {
                        Info($"capture {m.GdiDeviceName} not ready: {ex.Message}");
                        await Task.Delay(500, stop).ConfigureAwait(false);
                    }
                }

                if (capture is null)
                {
                    throw new InvalidOperationException($"Windows.Graphics.Capture rejected {m.GdiDeviceName}.");
                }
                capture.FrameArrived += f =>
                {
                    surface.Update(f.Texture, f.Width, f.Height);
                    Interlocked.Increment(ref capturedFrames);
                };
                capture.Start();
                captures.Add(capture);
            }

            using var presenter = new GlassesPresenter(gd, scene, GlassesOptics.Xreal1S, static () => default, glasses.X, glasses.Y, glasses.Resolution.Width, glasses.Resolution.Height) { Flat = true };
            presenter.Start();
            Info($"running: {captures.Count} screen(s) in the glasses' own anchored image. Recenter: long-press X on the glasses. Ctrl+Alt+Q stops.");
            SetState(WorkspaceState.Running);
            _running?.TrySetResult();

            await WaitWhileRunningAsync(presenter, topology, glasses, stop).ConfigureAwait(false);

            SetState(WorkspaceState.Stopping);
            presenter.Stop();
            try
            {
                await presenter.Completion.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or System.Runtime.InteropServices.COMException)
            {
                Info($"render error: {ex.Message}");
            }

            LastSummary = new WorkspaceSummary(presenter.GetStats(), Interlocked.Read(ref capturedFrames), 0, default, Vector3.Zero);
            Info($"render: {presenter.GetStats()}");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            Info("start cancelled");
        }
        catch (Exception ex)
        {
            // Never let a session failure escape to the caller (it crashed the app, [verified-hw]).
            LastError = ex;
            LastStopReason = WorkspaceStopReason.Error;
            Info($"error: {ex}");
        }
        finally
        {
            foreach (var c in captures)
            {
                c.Dispose();
            }

            if (changedDesktop)
            {
                await RestoreDesktopAsync(provider, topology, store, []).ConfigureAwait(false);
            }

            Volatile.Write(ref _virtualBounds, []);
            SetState(LastError is null ? WorkspaceState.Stopped : WorkspaceState.Faulted);
            _running?.TrySetResult();
        }
    }

    private async Task RunTrackingAsync(WorkspaceOptions o, CancellationToken stop)
    {
        GlassesPresenter.EnablePerMonitorDpi();
        var topology = new CcdDisplayTopology();
        var store = new LayoutSnapshotStore();
        using var provider = new VirtualDisplayRsProvider(topology);
        var captures = new List<MonitorCapture>();
        var desktopRestore = new List<(string Gdi, DisplayMode Mode)>();
        using var stopTracking = new CancellationTokenSource();
        Task trackingTask = Task.CompletedTask;
        long capturedFrames = 0;
        bool changedDesktop = false;

        try
        {
            var glasses = GlassesDisplayLocator.FindGlasses(topology.GetActiveMonitors())
                ?? throw new InvalidOperationException("Glasses monitor not found. Connect the glasses and use an extended display.");

            var modes = topology.GetSupportedModes(glasses.GdiDeviceName).Where(m => m.BitsPerPixel == 32).ToList();
            var native = modes.OrderByDescending(m => m.Resolution.Width * m.Resolution.Height).ThenByDescending(m => m.RefreshHz).First();
            var wanted = modes.FirstOrDefault(m => m.Resolution == native.Resolution && m.RefreshHz == o.GlassesRefreshHz);
            if (wanted == default)
            {
                Info($"{o.GlassesRefreshHz} Hz not offered at {native.Resolution} (glasses UltraWide must be Off for 120 Hz); using {native}.");
                wanted = native;
            }

            if (!store.Exists)
            {
                await store.SaveAsync(topology.CaptureLayout(), stop).ConfigureAwait(false);
            }

            changedDesktop = true;

            // Virtual monitors.
            var specs = Enumerable.Range(1, Math.Clamp(o.ScreenCount, 1, WorkspaceLayout.MaxScreens)).Select(i => new ScreenSpec(i, o.Mode)).ToList();
            var placements = WorkspaceLayout.Compute(specs, new WorkspaceSettings { Preset = o.Preset, DistanceMeters = o.DistanceMeters });
            var virtuals = new List<VirtualDisplay>();
            foreach (var spec in specs)
            {
                var vd = await provider.AddAsync(spec.Resolution, 60, stop).ConfigureAwait(false);
                virtuals.Add(vd);
                Info($"virtual monitor {spec.Id}: {vd.Resolution} → {vd.GdiDeviceName ?? "(not attached)"}");
            }

            await PrepareDesktopAsync(o, topology, virtuals, glasses, stop).ConfigureAwait(false);

            // Other monitors to the glasses refresh (DWM clock, ADR-0009).
            if (o.SyncDesktopRefresh)
            {
                foreach (var m in topology.GetActiveMonitors())
                {
                    if (GlassesDisplayLocator.Identify(m) is not null || m.EdidManufacturer == "CHY" || Math.Abs(m.RefreshHz - wanted.RefreshHz) <= 1)
                    {
                        continue;
                    }

                    var match = topology.GetSupportedModes(m.GdiDeviceName).FirstOrDefault(x => x.Resolution == m.Resolution && x.RefreshHz == wanted.RefreshHz && x.BitsPerPixel == 32);
                    if (match == default)
                    {
                        Info($"{m.FriendlyName}: no {wanted.RefreshHz} Hz mode; left at {m.RefreshHz} Hz (may judder).");
                        continue;
                    }

                    desktopRestore.Add((m.GdiDeviceName, new DisplayMode(m.Resolution, (int)Math.Round(m.RefreshHz), 32)));
                    Info($"{m.FriendlyName} → {match.RefreshHz} Hz for the session: {(topology.TrySetMode(m.GdiDeviceName, match) ? "ok" : "rejected")}");
                }
            }

            // Glasses mode last (topology changes re-apply its saved mode).
            glasses = GlassesDisplayLocator.FindGlasses(topology.GetActiveMonitors()) ?? glasses;
            if (glasses.Resolution != wanted.Resolution || Math.Abs(glasses.RefreshHz - wanted.RefreshHz) > 1)
            {
                Info($"glasses → {wanted}: {(topology.TrySetMode(glasses.GdiDeviceName, wanted) ? "ok" : "rejected")}");
                await Task.Delay(1500, stop).ConfigureAwait(false);
            }

            var monitors = topology.GetActiveMonitors();
            glasses = GlassesDisplayLocator.FindGlasses(monitors) ?? glasses;
            RememberVirtualScreens(virtuals, monitors);
            double? measuredHz = OutputTiming.MeasureVblankHz(glasses.GdiDeviceName, TimeSpan.FromSeconds(0.5));
            Info($"glasses: {glasses.Resolution} @ {glasses.RefreshHz} Hz (vblank {measuredHz:F1} Hz)");

            // GPU, scene, capture.
            using var gd = GraphicsDevice.CreateForMonitor(MonitorCapture.MonitorAt(glasses.X, glasses.Y));
            Info($"GPU: {gd.AdapterName}");
            using var scene = new WorkspaceScene(gd) { ClearColor = new Vortice.Mathematics.Color4(0.02f, 0.02f, 0.03f, 1f) };
            await MonitorCapture.RequestBorderlessAsync().ConfigureAwait(false);
            for (int i = 0; i < virtuals.Count; i++)
            {
                var m = monitors.FirstOrDefault(x => x.GdiDeviceName == virtuals[i].GdiDeviceName);
                if (m is null)
                {
                    Info($"virtual monitor {i + 1} not attached; skipped");
                    continue;
                }

                var surface = scene.AddScreen(placements[i], m.Resolution.Width, m.Resolution.Height);
                var capture = new MonitorCapture(gd.Device, MonitorCapture.MonitorAt(m.X, m.Y));
                capture.FrameArrived += f =>
                {
                    surface.Update(f.Texture, f.Width, f.Height);
                    Interlocked.Increment(ref capturedFrames);
                };
                capture.Start();
                captures.Add(capture);
            }

            // Tracking.
            lock (_poseLock)
            {
                _latest = new HeadPose(0, Quaternion.Identity, Quaternion.Identity, Vector3.Zero);
            }

            _tracker = new HeadTracker(new MadgwickFilter(o.FilterBeta) { AccelGate = o.AccelGate, RateGate = o.RateGate });
            _tracker.AutoCenter.Settings = o.AutoCenter;
            _stabilizer = new PoseStabilizer(StabilizerSettings.FromName(o.Stabilizer));
            if (o.Source != TrackingSource.Fixed)
            {
                IImuSource imu = o.Source == TrackingSource.Simulated
                    ? new SyntheticImuSource(new SyntheticImuOptions { Motion = SyntheticMotion.YawSweep, DurationSeconds = 24 * 3600, RealTime = true, AmplitudeDegrees = 50, PeriodSeconds = 8 })
                    : new XrealOneImuSource();
                var tracker = _tracker;
                trackingTask = Task.Run(async () =>
                {
                    await foreach (var sample in imu.ReadAsync(stopTracking.Token).ConfigureAwait(false))
                    {
                        var pose = tracker.Update(sample);
                        lock (_poseLock)
                        {
                            _latest = pose;
                        }
                    }
                }, stopTracking.Token);
            }

            float predictSeconds = (float)(o.PredictMs / 1000.0);
            var stabilizer = _stabilizer;
            bool fixedPose = o.Source == TrackingSource.Fixed;
            long lastLatch = 0;
            var neckToEye = o.NeckModel ? ViewMath.DefaultNeckToEye : Vector3.Zero;
            HeadView Latch()
            {
                if (fixedPose)
                {
                    return new HeadView(Quaternion.Identity, Vector3.Zero);
                }

                Quaternion predicted;
                float speed;
                lock (_poseLock)
                {
                    predicted = _latest.PredictRelative(predictSeconds);
                    speed = _latest.AngularVelocity.Length() * 180f / MathF.PI;
                }

                long now = Stopwatch.GetTimestamp();
                float dt = lastLatch == 0 ? 0f : (float)Stopwatch.GetElapsedTime(lastLatch, now).TotalSeconds;
                lastLatch = now;
                var full = stabilizer.Filter(predicted, dt, speed);
                // Eye position from the full head rotation (lean-in still brings screens closer),
                // view orientation limited to the axes the user wants the screens to follow.
                return new HeadView(o.Axes.Constrain(full), ViewMath.EyeOffset(full, neckToEye));
            }

            // Present until stopped.
            using var presenter = new GlassesPresenter(gd, scene, GlassesOptics.Xreal1S, Latch, glasses.X, glasses.Y, glasses.Resolution.Width, glasses.Resolution.Height) { LatchLeadMs = o.LatchLeadMs };
            presenter.RecenterRequested += Recenter;
            DistanceMeters = o.DistanceMeters;
            var layoutSpecs = specs.Take(captures.Count).ToList();
            _setDistance = meters =>
            {
                DistanceMeters = Math.Clamp(meters, 0.5f, 4f);
                scene.SetPlacements(WorkspaceLayout.Compute(layoutSpecs, new WorkspaceSettings { Preset = o.Preset, DistanceMeters = DistanceMeters }));
                Info($"distance {DistanceMeters:F2} m");
                DistanceChanged?.Invoke(this, DistanceMeters);
            };
            presenter.DistanceStepRequested += step => _setDistance?.Invoke(DistanceMeters + step * 0.1f);
            presenter.Start();
            if (presenter.UnavailableHotkeys.Count > 0)
            {
                Info($"hotkeys taken by another program: {string.Join(", ", presenter.UnavailableHotkeys)}");
            }

            Info($"running: {captures.Count} screen(s), stabilizer {o.Stabilizer}, axes {o.Axes}, neck model {(o.NeckModel ? "on" : "off")} — Ctrl+Alt+R recenter, Ctrl+Alt+Plus/Minus closer/farther, Ctrl+Alt+Q stop");
            SetState(WorkspaceState.Running);
            _running?.TrySetResult();

            await WaitWhileRunningAsync(presenter, topology, glasses, stop).ConfigureAwait(false);

            SetState(WorkspaceState.Stopping);
            presenter.Stop();
            try
            {
                await presenter.Completion.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or System.Runtime.InteropServices.COMException)
            {
                Info($"render error: {ex.Message}");
            }

            LastSummary = new WorkspaceSummary(presenter.GetStats(), Interlocked.Read(ref capturedFrames), _tracker.SampleCount, _tracker.Current, _tracker.GyroBias);
            Info($"render: {presenter.GetStats()}");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            Info("start cancelled");
        }
        catch (Exception ex)
        {
            // Never let a session failure escape to the caller (it crashed the app, [verified-hw]).
            LastError = ex;
            LastStopReason = WorkspaceStopReason.Error;
            Info($"error: {ex}");
        }
        finally
        {
            await stopTracking.CancelAsync().ConfigureAwait(false);
            try
            {
                await trackingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // normal stop
            }
            catch (IOException ex)
            {
                Info($"tracking stopped: {ex.Message}");
            }

            foreach (var c in captures)
            {
                c.Dispose();
            }

            if (changedDesktop)
            {
                await RestoreDesktopAsync(provider, topology, store, desktopRestore).ConfigureAwait(false);
            }

            _tracker = null;
            _stabilizer = null;
            _setDistance = null;
            Volatile.Write(ref _virtualBounds, []);
            SetState(LastError is null ? WorkspaceState.Stopped : WorkspaceState.Faulted);
            _running?.TrySetResult(); // unblock StartAsync if we failed before running
        }
    }

    /// <summary>
    /// Waits until the presenter ends, a stop is requested, or the glasses change resolution / disappear
    /// (switching the OSD UltraWide mode re-enumerates the glasses monitor).
    /// </summary>
    private async Task WaitWhileRunningAsync(GlassesPresenter presenter, CcdDisplayTopology topology, DisplayMonitor glasses, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested && !presenter.Completion.IsCompleted)
        {
            await Task.WhenAny(presenter.Completion, Task.Delay(1000, CancellationToken.None)).ConfigureAwait(false);
            if (stop.IsCancellationRequested || presenter.Completion.IsCompleted)
            {
                break;
            }

            DisplayMonitor? now;
            try
            {
                now = GlassesDisplayLocator.FindGlasses(topology.GetActiveMonitors());
            }
            catch (System.ComponentModel.Win32Exception)
            {
                continue; // topology changing right now; look again next second
            }

            if (now is null || now.Resolution != glasses.Resolution)
            {
                Info(now is null ? "glasses disconnected — stopping" : $"glasses changed to {now.Resolution} — stopping");
                LastStopReason = WorkspaceStopReason.GlassesDisplayChanged;
                return;
            }
        }

        if (LastStopReason == WorkspaceStopReason.None)
        {
            LastStopReason = WorkspaceStopReason.UserStopped;
        }
    }

    /// <summary>
    /// After creating virtual monitors: force their exact resolution, then order the desktop
    /// [desk][virtual 1..n][glasses]. Windows remembers a mode per monitor identity and our virtual
    /// monitors reuse ids, so a new 1280×1080 monitor came up at an old 1920×1080 mode and the
    /// reorder failed with ERROR_INVALID_PARAMETER (87) ([verified-hw] 2026-09-26). The order is a
    /// convenience (mouse path), so failing to apply it is logged, not fatal.
    /// Also makes the chosen virtual screen primary and moves app windows off the glasses monitor.
    /// </summary>
    private void RememberVirtualScreens(List<VirtualDisplay> virtuals, IReadOnlyList<DisplayMonitor> monitors)
    {
        var bounds = new List<System.Drawing.Rectangle>(virtuals.Count);
        foreach (var vd in virtuals)
        {
            var monitor = monitors.FirstOrDefault(m => m.GdiDeviceName == vd.GdiDeviceName);
            if (monitor is not null)
            {
                bounds.Add(new System.Drawing.Rectangle(monitor.X, monitor.Y, monitor.Resolution.Width, monitor.Resolution.Height));
            }
        }

        Volatile.Write(ref _virtualBounds, bounds.ToArray());
    }

    private async Task PrepareDesktopAsync(WorkspaceOptions o, CcdDisplayTopology topology, List<VirtualDisplay> virtuals, DisplayMonitor glasses, CancellationToken stop)
    {
        await ArrangeDesktopAsync(o, topology, virtuals, glasses, stop).ConfigureAwait(false);
        if (o.MoveWindowsFromGlasses)
        {
            MoveWindowsFromGlasses(o, topology, virtuals);
        }
    }

    private void MoveWindowsFromGlasses(WorkspaceOptions o, CcdDisplayTopology topology, List<VirtualDisplay> virtuals)
    {
        try
        {
            var monitors = topology.GetActiveMonitors();
            var glasses = GlassesDisplayLocator.FindGlasses(monitors);
            string? targetName = virtuals.ElementAtOrDefault(Math.Max(0, o.PrimaryScreen - 1))?.GdiDeviceName ?? virtuals.FirstOrDefault()?.GdiDeviceName;
            var target = monitors.FirstOrDefault(m => m.GdiDeviceName == targetName);
            if (glasses is null || target is null)
            {
                return;
            }

            static System.Drawing.Rectangle Bounds(DisplayMonitor m) => new(m.X, m.Y, m.Resolution.Width, m.Resolution.Height);
            int moved = WindowMover.MoveWindows(Bounds(glasses), Bounds(target));
            if (moved > 0)
            {
                Info($"moved {moved} window(s) from the glasses display to virtual screen {virtuals.FindIndex(v => v.GdiDeviceName == targetName) + 1}");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Info($"could not move windows off the glasses display: {ex.Message}");
        }
    }

    private async Task ArrangeDesktopAsync(WorkspaceOptions o, CcdDisplayTopology topology, List<VirtualDisplay> virtuals, DisplayMonitor glasses, CancellationToken stop)
    {
        await Task.Delay(1000, stop).ConfigureAwait(false);
        foreach (var vd in virtuals.Where(v => v.GdiDeviceName is not null))
        {
            var current = topology.GetActiveMonitors().FirstOrDefault(m => m.GdiDeviceName == vd.GdiDeviceName);
            if (current is not null && current.Resolution != vd.Resolution)
            {
                bool ok = topology.TrySetMode(vd.GdiDeviceName!, new DisplayMode(vd.Resolution, vd.RefreshHz, 32));
                Info($"{vd.GdiDeviceName}: Windows restored {current.Resolution}; set {vd.Resolution}: {(ok ? "ok" : "rejected")}");
            }
        }

        await Task.Delay(800, stop).ConfigureAwait(false);
        var names = virtuals.Where(v => v.GdiDeviceName is not null).Select(v => v.GdiDeviceName!).ToList();
        int primaryIndex = o.PrimaryScreen > 0 && virtuals.ElementAtOrDefault(o.PrimaryScreen - 1)?.GdiDeviceName is { } primaryName
            ? names.IndexOf(primaryName)
            : -1;
        if (o.PrimaryScreen > 0)
        {
            Info(primaryIndex >= 0 ? $"primary monitor: virtual screen {o.PrimaryScreen}" : $"virtual screen {o.PrimaryScreen} does not exist; primary monitor kept");
        }

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                topology.ApplyLayout(WorkspaceArrangement.Arrange(topology.GetActiveMonitors(), names, glasses.GdiDeviceName, primaryIndex));
                await Task.Delay(1000, stop).ConfigureAwait(false);
                return;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Info($"desktop order attempt {attempt} failed: {ex.Message}");
                await Task.Delay(700, stop).ConfigureAwait(false);
            }
        }

        Info("could not reorder the desktop; the workspace runs anyway (the mouse may need to cross the glasses area)");
    }

    private async Task RestoreDesktopAsync(VirtualDisplayRsProvider provider, CcdDisplayTopology topology, LayoutSnapshotStore store, List<(string Gdi, DisplayMode Mode)> desktopRestore)
    {
        try
        {
            await provider.RemoveAllOwnedAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(1000, CancellationToken.None).ConfigureAwait(false);
            var saved = await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            if (saved is not null)
            {
                topology.ApplyLayout(saved);
                store.Delete();
            }

            foreach (var (gdi, original) in desktopRestore)
            {
                topology.TrySetMode(gdi, original);
            }

            Info($"cleanup: virtual monitors removed, layout restored{(desktopRestore.Count > 0 ? ", desktop refresh restored" : "")}");
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            Info($"cleanup incomplete: {ex.Message} — run `xrs vdd clear` / `xrs display restore`.");
        }
    }
}

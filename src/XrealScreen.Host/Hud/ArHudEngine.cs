using System.Numerics;
using XrealScreen.Core.Abstractions;
using XrealScreen.Core.Hud;
using XrealScreen.Core.Tracking;
using XrealScreen.Device.XrealOne;
using XrealScreen.Render.Scene;

namespace XrealScreen.Host.Hud;

public enum ArHudState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted,
}

/// <summary>
/// Clear-view HUD session (ADR-0015): IMU → head tracker → instrument overlay on a black glasses frame.
/// No virtual monitors, no desktop capture, no topology changes. One session at a time.
/// </summary>
public sealed class ArHudEngine : IAsyncDisposable
{
    private readonly IImuSource? _imu;
    private readonly IHudOutput? _injected;
    private readonly Lock _poseLock = new();
    private CancellationTokenSource? _stopCts;
    private Task? _runTask;
    private TaskCompletionSource? _running;
    private HeadTracker? _tracker;
    private HeadPose _latest = new(0, Quaternion.Identity, Quaternion.Identity, Vector3.Zero);
    private ArHudState _state = ArHudState.Stopped;
    private HudStyle _style = new();
    private long _imuSamples;

    public ArHudEngine(IImuSource? imu = null, IHudOutput? output = null)
    {
        _imu = imu;
        _injected = output;
    }

    public event EventHandler<ArHudState>? StateChanged;

    public event EventHandler<string>? Log;

    public ArHudState State => _state;

    public Exception? LastError { get; private set; }

    public long ImuSamples => Interlocked.Read(ref _imuSamples);

    public HudStyle Style => Volatile.Read(ref _style);

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

    public void UpdateStyle(HudStyle style) => Volatile.Write(ref _style, style ?? throw new ArgumentNullException(nameof(style)));

    public void Recenter() => _tracker?.RequestRecenter();

    /// <summary>Starts a session; returns when the HUD is on the glasses (or the injected output has started).</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_state is ArHudState.Starting or ArHudState.Running or ArHudState.Stopping)
        {
            throw new InvalidOperationException($"HUD is {_state}.");
        }

        LastError = null;
        Interlocked.Exchange(ref _imuSamples, 0);
        _stopCts = new CancellationTokenSource();
        _running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetState(ArHudState.Starting);
        _runTask = Task.Run(() => RunAsync(_stopCts.Token), CancellationToken.None);

        using var registration = cancellationToken.Register(() => _stopCts.Cancel());
        await Task.WhenAny(_running.Task, _runTask).ConfigureAwait(false);
        if (_state != ArHudState.Running)
        {
            await _runTask.ConfigureAwait(false);
            throw LastError ?? new OperationCanceledException("HUD start was cancelled.");
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

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task RunAsync(CancellationToken stop)
    {
        IHudOutput? output = null;
        bool owned = false;
        Task? trackingTask = null;
        using var stopTracking = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Action? onRecenter = null;
        try
        {
            _tracker = new HeadTracker();
            var tracker = _tracker;
            IImuSource imu = _imu ?? new XrealOneImuSource();
            trackingTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var sample in imu.ReadAsync(stopTracking.Token).ConfigureAwait(false))
                    {
                        HeadPose pose = tracker.Update(sample);
                        lock (_poseLock)
                        {
                            _latest = pose;
                        }

                        Interlocked.Increment(ref _imuSamples);
                    }
                }
                catch (OperationCanceledException)
                {
                    // normal stop
                }
                catch (Exception ex)
                {
                    Info($"tracking stopped: {ex.Message}");
                }
            }, CancellationToken.None);

            output = _injected ?? new GlassesHudOutput(() => Style);
            owned = _injected is null;
            onRecenter = Recenter;
            output.RecenterRequested += onRecenter;
            output.Start(Latch);
            Info("running: clear HUD — Ctrl+Alt+R recenter, Ctrl+Alt+Q or Ctrl+Alt+U stops");
            SetState(ArHudState.Running);
            _running?.TrySetResult();

            await Task.WhenAny(output.Completion, Task.Delay(Timeout.Infinite, stop)).ConfigureAwait(false);
            SetState(ArHudState.Stopping);
            output.RequestStop();
            await output.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            Info("start cancelled");
        }
        catch (Exception ex)
        {
            LastError = ex;
            Info($"error: {ex.Message}");
        }
        finally
        {
            if (output is not null && onRecenter is not null)
            {
                output.RecenterRequested -= onRecenter;
            }

            await stopTracking.CancelAsync().ConfigureAwait(false);
            if (trackingTask is not null)
            {
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
            }

            if (owned)
            {
                output?.Dispose();
            }

            _tracker = null;
            SetState(LastError is null ? ArHudState.Stopped : ArHudState.Faulted);
            _running?.TrySetResult();
        }
    }

    private HeadView Latch()
    {
        Quaternion predicted;
        lock (_poseLock)
        {
            predicted = _latest.PredictRelative(0.020f);
        }

        return new HeadView(predicted, Vector3.Zero);
    }

    private void SetState(ArHudState state)
    {
        _state = state;
        StateChanged?.Invoke(this, state);
    }

    private void Info(string message) => Log?.Invoke(this, message);
}

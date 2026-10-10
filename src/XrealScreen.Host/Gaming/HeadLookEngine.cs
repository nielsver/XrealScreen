using System.Diagnostics;
using XrealScreen.Core.Abstractions;
using XrealScreen.Core.Gaming;
using XrealScreen.Core.Tracking;
using XrealScreen.Display;

namespace XrealScreen.Host.Gaming;

/// <summary>
/// Head look for games (ADR-0011): IMU → <see cref="HeadTracker"/> → <see cref="HeadMouseMapper"/> →
/// <see cref="MouseInjector"/>. Retries the IMU connection until cancelled (right after plugging in,
/// the glasses' USB network is not up yet, [verified-hw] 2026-10-10). Polls the global hotkeys
/// Ctrl+Alt+H (on/off), Ctrl+Alt+C (recenter) and Ctrl+Alt+G (move the active window to the glasses)
/// with GetAsyncKeyState, so they work while a game has focus and are not taken from other apps.
/// </summary>
public sealed class HeadLookEngine(IImuSource source)
{
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12; // Alt
    private const int VkH = 0x48;
    private const int VkC = 0x43;
    private const int VkG = 0x47;
    private static readonly long MinSendIntervalTicks = Stopwatch.Frequency / 500; // ≤ 500 SendInput calls/s
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly HeadMouseMapper _mapper = new();
    private volatile HeadMouseSettings _settings = new();
    private volatile bool _enabled;
    private volatile bool _resetPending;
    private volatile bool _connected;
    private bool _waiting; // IMU thread only: the "waiting for the glasses" message was shown
    private long _sends;
    private long _rejected;
    private float _yaw;
    private float _pitch;

    /// <summary>Raised from the IMU thread: connection changes, hotkey actions, moved windows.</summary>
    public event Action<string>? Message;

    /// <summary>Raised from the IMU thread or the caller's thread when head look is switched on or off.</summary>
    public event Action<bool>? EnabledChanged;

    public string SourceName => source.Name;

    /// <summary>Mapping settings; can be changed while running.</summary>
    public HeadMouseSettings Settings
    {
        get => _settings;
        set => _settings = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Mouse output on/off (Ctrl+Alt+H). Switching on recenters.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            _resetPending = true;
            EnabledChanged?.Invoke(value);
        }
    }

    public bool IsConnected => _connected;

    public long Sends => Interlocked.Read(ref _sends);

    /// <summary>Mouse moves Windows refused (game running as administrator, secure desktop).</summary>
    public long Rejected => Interlocked.Read(ref _rejected);

    public float YawDegrees => Volatile.Read(ref _yaw);

    public float PitchDegrees => Volatile.Read(ref _pitch);

    /// <summary>Stick center = current head direction; Look: drops any partial movement.</summary>
    public void Recenter() => _resetPending = true;

    /// <summary>Moves the foreground window onto the glasses; returns a user-facing result.</summary>
    public static string MoveActiveWindowToGlasses()
    {
        var glasses = GlassesDisplayLocator.FindGlasses(new CcdDisplayTopology().GetActiveMonitors());
        if (glasses is null)
        {
            return "Glasses display not found.";
        }

        string? title = WindowMover.MoveForegroundWindow(new System.Drawing.Rectangle(glasses.X, glasses.Y, glasses.Resolution.Width, glasses.Resolution.Height));
        return title is null
            ? "Could not move the active window (exclusive fullscreen, running as administrator, or no app window)."
            : $"Moved to the glasses: {title}";
    }

    /// <summary>Runs until cancelled; never throws for connection problems.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var tracker = new HeadTracker();
        var keys = new HotkeyState();
        _waiting = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PumpAsync(tracker, keys, cancellationToken).ConfigureAwait(false);
                Message?.Invoke("IMU stream ended; reconnecting...");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException ex)
            {
                if (!_waiting)
                {
                    Message?.Invoke($"Waiting for the glasses to connect (retrying every 2 s). Last error: {ex.Message}");
                }
            }

            _waiting = true;
            _connected = false;
            _resetPending = true;
            try
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _connected = false;
    }

    private async Task PumpAsync(HeadTracker tracker, HotkeyState keys, CancellationToken cancellationToken)
    {
        int pendingX = 0, pendingY = 0;
        long lastSend = 0, nextKeyPoll = 0;
        await foreach (var sample in source.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!_connected)
            {
                _connected = true;
                _waiting = false;
                Message?.Invoke("Glasses connected.");
            }

            var pose = tracker.Update(sample);
            Volatile.Write(ref _yaw, pose.YawDegrees);
            Volatile.Write(ref _pitch, pose.PitchDegrees);
            long now = Stopwatch.GetTimestamp();
            if (now >= nextKeyPoll)
            {
                nextKeyPoll = now + Stopwatch.Frequency / 50;
                PollHotkeys(keys);
            }

            if (_resetPending)
            {
                _resetPending = false;
                _mapper.Reset();
                pendingX = pendingY = 0;
            }

            if (!_enabled)
            {
                continue;
            }

            _mapper.Settings = _settings;
            var delta = _mapper.Update(pose);
            pendingX += delta.X;
            pendingY += delta.Y;
            if ((pendingX != 0 || pendingY != 0) && now - lastSend >= MinSendIntervalTicks)
            {
                Interlocked.Increment(ref MouseInjector.Move(new MouseDelta(pendingX, pendingY)) ? ref _sends : ref _rejected);
                pendingX = pendingY = 0;
                lastSend = now;
            }
        }
    }

    private void PollHotkeys(HotkeyState keys)
    {
        bool chord = MouseInjector.IsKeyDown(VkControl) && MouseInjector.IsKeyDown(VkMenu);
        if (keys.Pressed(0, chord && MouseInjector.IsKeyDown(VkH)))
        {
            Enabled = !Enabled;
            Message?.Invoke(Enabled ? "Head look ON." : "Head look OFF.");
        }

        if (keys.Pressed(1, chord && MouseInjector.IsKeyDown(VkC)))
        {
            Recenter();
            Message?.Invoke("Recentered.");
        }

        if (keys.Pressed(2, chord && MouseInjector.IsKeyDown(VkG)))
        {
            Message?.Invoke(MoveActiveWindowToGlasses());
        }
    }

    /// <summary>Edge detection for the polled hotkeys.</summary>
    private sealed class HotkeyState
    {
        private readonly bool[] _down = new bool[3];

        public bool Pressed(int key, bool isDown)
        {
            bool pressed = isDown && !_down[key];
            _down[key] = isDown;
            return pressed;
        }
    }
}

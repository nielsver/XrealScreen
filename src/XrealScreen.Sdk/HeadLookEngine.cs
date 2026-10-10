using System.Diagnostics;
using XrealScreen.Core.Gaming;

namespace XrealScreen.Sdk;

/// <summary>
/// Head look for games (ADR-0011): head poses from <see cref="XrealGlasses"/> become relative mouse
/// movement through <see cref="HeadMouseMapper"/>, so any game with mouse look can be steered with
/// the head. Starts switched off; see <see cref="Enabled"/>.
/// </summary>
/// <remarks>
/// With <see cref="PollHotkeys"/> (default) the global hotkeys work while a game has focus:
/// Ctrl+Alt+H switches head look on/off, Ctrl+Alt+C recenters, Ctrl+Alt+G moves the active window
/// to the glasses. They are polled (GetAsyncKeyState), so other programs keep these keys.
/// </remarks>
/// <example>
/// <code>
/// var engine = new HeadLookEngine(new XrealGlasses())
/// {
///     Settings = new HeadMouseSettings { Mode = HeadMouseMode.Look, CountsPerDegree = 20 },
///     Enabled = true,
/// };
/// await engine.RunAsync(cancellationToken);
/// </code>
/// </example>
public sealed class HeadLookEngine
{
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12; // Alt
    private const int VkH = 0x48;
    private const int VkC = 0x43;
    private const int VkG = 0x47;
    private static readonly long MinSendIntervalTicks = Stopwatch.Frequency / 500; // ≤ 500 mouse moves/s

    private readonly XrealGlasses _glasses;
    private readonly IMouseOutput _mouse;
    private readonly HeadMouseMapper _mapper = new();
    private volatile HeadMouseSettings _settings = new();
    private volatile bool _enabled;
    private volatile bool _resetPending;
    private long _sends;
    private long _rejected;

    /// <param name="glasses">Pose source. The engine reads it with <see cref="XrealGlasses.ReadPosesAsync"/>.</param>
    /// <param name="mouse">Mouse output; default <see cref="MouseInjector.Instance"/> (the system mouse).</param>
    public HeadLookEngine(XrealGlasses glasses, IMouseOutput? mouse = null)
    {
        _glasses = glasses ?? throw new ArgumentNullException(nameof(glasses));
        _mouse = mouse ?? MouseInjector.Instance;
        _glasses.ConnectionChanged += (_, e) =>
        {
            // After a reconnect the head may have moved: start fresh instead of jumping.
            _resetPending |= e.IsConnected;
            Message?.Invoke(e.IsConnected
                ? "Glasses connected."
                : e.Error is null ? "Glasses disconnected." : $"Waiting for the glasses to connect (retrying). Last error: {e.Error}");
        };
    }

    /// <summary>Raised on the reading thread: connection changes, hotkey actions, moved windows.</summary>
    public event Action<string>? Message;

    /// <summary>Raised when head look is switched on or off (by the property or Ctrl+Alt+H).</summary>
    public event Action<bool>? EnabledChanged;

    /// <summary>The pose source.</summary>
    public XrealGlasses Glasses => _glasses;

    /// <summary>Poll Ctrl+Alt+H / C / G while running. Default true.</summary>
    public bool PollHotkeys { get; init; } = true;

    /// <summary>Mapping settings; can be changed while running (thread-safe).</summary>
    public HeadMouseSettings Settings
    {
        get => _settings;
        set => _settings = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Mouse output on/off (Ctrl+Alt+H). Switching on recenters. Default off.</summary>
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

    /// <summary>True while poses are arriving.</summary>
    public bool IsConnected => _glasses.IsConnected;

    /// <summary>Mouse moves sent.</summary>
    public long Sends => Interlocked.Read(ref _sends);

    /// <summary>Mouse moves Windows refused (game running as administrator, secure desktop).</summary>
    public long Rejected => Interlocked.Read(ref _rejected);

    /// <summary>Joystick mode: the current head direction becomes the center. Look mode: drops partial movement.</summary>
    public void Recenter() => _resetPending = true;

    /// <summary>Moves the foreground window onto the glasses; returns a user-facing result.</summary>
    public static string MoveActiveWindowToGlasses() => GlassesDisplay.MoveForegroundWindowToGlasses() is { } title
        ? $"Moved to the glasses: {title}"
        : "Could not move the active window (glasses not found, exclusive fullscreen, running as administrator, or no app window).";

    /// <summary>Runs until <paramref name="cancellationToken"/> is cancelled; reconnects to the glasses as needed.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var keys = new HotkeyState();
        int pendingX = 0, pendingY = 0;
        long lastSend = 0, nextKeyPoll = 0;
        await foreach (var pose in _glasses.ReadPosesAsync(cancellationToken).ConfigureAwait(false))
        {
            long now = Stopwatch.GetTimestamp();
            if (PollHotkeys && now >= nextKeyPoll)
            {
                nextKeyPoll = now + Stopwatch.Frequency / 50;
                PollKeys(keys);
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
            var delta = _mapper.Update(pose.Tracker);
            pendingX += delta.X;
            pendingY += delta.Y;
            if ((pendingX != 0 || pendingY != 0) && now - lastSend >= MinSendIntervalTicks)
            {
                Interlocked.Increment(ref _mouse.Move(new MouseDelta(pendingX, pendingY)) ? ref _sends : ref _rejected);
                pendingX = pendingY = 0;
                lastSend = now;
            }
        }
    }

    private void PollKeys(HotkeyState keys)
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

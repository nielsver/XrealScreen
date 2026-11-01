using System.Diagnostics;
using System.Drawing;
using XrealScreen.Device.XrealOne;
using XrealScreen.Vision.Hands;

namespace XrealScreen.Host.Hands;

public enum HandControlState
{
    /// <summary>Connecting to the Eye camera.</summary>
    Starting,

    /// <summary>No hand in view.</summary>
    NoHand,

    /// <summary>The hand moves the pointer.</summary>
    Active,

    /// <summary>The real mouse or keyboard was used recently; hand control waits.</summary>
    PausedByRealInput,

    /// <summary>Tracking runs but nothing is sent (calibration, preview).</summary>
    OutputOff,
}

/// <summary>One processed camera frame, for status displays, previews and calibration.</summary>
public sealed record HandControlFrame(HandControlState State, HandResult? Hand, PointerFrame Output, Point? ScreenPoint, double ProcessMs, EyeFrame? Camera);

public sealed record HandControlOptions
{
    public required string ModelDirectory { get; init; }

    /// <summary>Screen area the hand covers (virtual-desktop pixels), e.g. the glasses monitor.</summary>
    public required Rectangle TargetArea { get; init; }

    /// <summary>Null = <see cref="HandCalibration.Default"/> for the camera size.</summary>
    public HandCalibration? Calibration { get; init; }

    public OneEndpoints Endpoints { get; init; } = OneEndpoints.Default;

    /// <summary>How long hand control waits after real mouse/keyboard input.</summary>
    public TimeSpan PauseAfterRealInput { get; init; } = TimeSpan.FromSeconds(2);

    public HandPointerOptions PointerOptions { get; init; } = new();
}

/// <summary>
/// Hand control (ADR-0016, M9 E3): Eye camera → <see cref="HandTracker"/> → <see cref="HandPointer"/> → pointer input.
/// The camera socket is open only while <see cref="RunAsync"/> runs; stop it to turn hand control off.
/// </summary>
public sealed class HandControlEngine(HandControlOptions options, IPointerOutput? output = null)
{
    private readonly IPointerOutput _output = output ?? SendInputPointer.Instance;
    private Point? _lastSent;
    private bool _buttonDown;

    /// <summary>Raised for every camera frame (on the engine's thread; keep handlers short). <see cref="HandControlFrame.Camera"/> is only valid during the call.</summary>
    public event Action<HandControlFrame>? FrameProcessed;

    /// <summary>False = track only, send nothing (calibration). Turning it off releases a held button.</summary>
    public bool OutputEnabled { get; set; } = true;

    public HandCalibration? Calibration { get; set; } = options.Calibration;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var tracker = new HandTracker(new HandTrackerOptions { ModelDirectory = options.ModelDirectory });
        HandPointer? pointer = null;
        var source = new XrealEyeCameraSource(new XrealEyeCameraOptions { Endpoints = options.Endpoints });
        FrameProcessed?.Invoke(new HandControlFrame(HandControlState.Starting, null, default, null, 0, null));
        try
        {
            await foreach (var frame in source.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                using (frame)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    var hand = tracker.Process(frame.Pixels, frame.Width, frame.Height, frame.Stride, frame.TimestampNs);
                    pointer ??= new HandPointer(Calibration ?? HandCalibration.Default(frame.Width, frame.Height), options.PointerOptions);
                    if (Calibration is { } c && !ReferenceEquals(pointer.Calibration, c))
                    {
                        pointer.Calibration = c;
                    }

                    var p = pointer.Update(hand, frame.TimestampNs / 1e9);
                    var (state, screen) = Send(p);
                    FrameProcessed?.Invoke(new HandControlFrame(state, hand, p, screen, Stopwatch.GetElapsedTime(t0).TotalMilliseconds, frame));
                }
            }
        }
        finally
        {
            ReleaseButton();
        }
    }

    private (HandControlState State, Point? Screen) Send(PointerFrame p)
    {
        var area = options.TargetArea;
        Point? screen = p.HasPointer
            ? new Point(area.X + (int)Math.Round(p.U * (area.Width - 1)), area.Y + (int)Math.Round(p.V * (area.Height - 1)))
            : null;
        if (!OutputEnabled)
        {
            ReleaseButton();
            return (HandControlState.OutputOff, screen);
        }

        if (RealInputMonitor.MillisecondsSinceRealInput() is { } ms && ms < options.PauseAfterRealInput.TotalMilliseconds)
        {
            ReleaseButton();
            return (HandControlState.PausedByRealInput, screen);
        }

        if (screen is { } s && s != _lastSent)
        {
            _output.MoveTo(s.X, s.Y);
            _lastSent = s;
        }

        if (p.Action == PointerAction.ButtonDown && !_buttonDown)
        {
            _buttonDown = _output.LeftButton(true);
        }
        else if (p.Action == PointerAction.ButtonUp)
        {
            ReleaseButton();
        }

        _output.Wheel(p.ScrollNotches);
        return (screen is null ? HandControlState.NoHand : HandControlState.Active, screen);
    }

    private void ReleaseButton()
    {
        if (_buttonDown)
        {
            _output.LeftButton(false);
            _buttonDown = false;
        }
    }
}

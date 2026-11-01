namespace XrealScreen.Vision.Hands;

public enum PointerAction
{
    None,

    /// <summary>Thumb + index pinch closed: left button down (held = drag).</summary>
    ButtonDown,

    /// <summary>Pinch opened (or the hand was lost): left button up.</summary>
    ButtonUp,
}

/// <summary>One frame of hand control: where the pointer is (0..1 of the target area) and what to do.</summary>
/// <param name="HasPointer">False when no hand is tracked (leave the pointer where it is).</param>
/// <param name="ScrollNotches">Mouse-wheel notches to send (positive = up), from a thumb + middle-finger pinch moved up/down.</param>
/// <param name="DwellProgress">0..1 while the pointer is held still towards a dwell click (for a progress ring); 0 otherwise.</param>
public readonly record struct PointerFrame(bool HasPointer, double U, double V, PointerAction Action, int ScrollNotches, double DwellProgress = 0);

/// <summary>How a click is made.</summary>
public enum ClickMode
{
    /// <summary>Thumb + index pinch (hold = drag).</summary>
    Pinch,

    /// <summary>Hold the pointer still for <see cref="HandPointerOptions.DwellTime"/> (works when the camera cannot see the fingers clearly).</summary>
    Dwell,

    /// <summary>Either: a pinch or a dwell clicks.</summary>
    Both,
}

public sealed record HandPointerOptions
{
    /// <summary>
    /// Default <see cref="ClickMode.Both"/>: E3 test 2026-11-01 showed that with the hand held up to aim the Eye
    /// often cannot see thumb and index meet (pinch ratio only 0.19–0.37), so dwell is the reliable fallback.
    /// </summary>
    public ClickMode Click { get; init; } = ClickMode.Both;

    /// <summary>How long the pointer must stay within <see cref="DwellRadius"/> for a dwell click.</summary>
    public TimeSpan DwellTime { get; init; } = TimeSpan.FromMilliseconds(800);

    /// <summary>Allowed wobble during a dwell, in screen units (0.012 ≈ 23 px on 1920).</summary>
    public double DwellRadius { get; init; } = 0.012;

    /// <summary>After a dwell click the pointer must move this far (screen units) before the next dwell can start.</summary>
    public double DwellRearm { get; init; } = 0.03;

    /// <summary>One Euro smoothing of the pointer (on top of the landmark smoothing), in screen units (0..1).</summary>
    public float MinCutoffHz { get; init; } = 1.0f;

    public float Beta { get; init; } = 2.0f;

    /// <summary>
    /// The pointer stays still this long after a press and a release, so closing or opening the fingers does not
    /// move it off the target (a click is a click, not a tiny drag).
    /// </summary>
    public TimeSpan ClickFreeze { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>A press uses the pointer position from this long before it was detected (the hand moves while pinching).</summary>
    public TimeSpan PressLookBack { get; init; } = TimeSpan.FromMilliseconds(130);

    /// <summary>Vertical hand movement (screen units) per wheel notch while scrolling.</summary>
    public double ScrollStep { get; init; } = 0.04;
}

/// <summary>
/// Hand → pointer logic (ADR-0016, M9 E3): the index-finger base is the pointer (it barely moves while pinching),
/// mapped through <see cref="HandCalibration"/>; thumb + index pinch = left button (hold to drag);
/// thumb + middle pinch and move up/down = scroll. Pure logic: the caller sends the input.
/// </summary>
public sealed class HandPointer
{
    private readonly HandPointerOptions _options;
    private readonly OneEuroFilter _u;
    private readonly OneEuroFilter _v;
    private readonly PinchDetector _click = new();
    private readonly PinchDetector _scroll = new();
    private readonly Queue<(double T, double U, double V)> _history = new();
    private double _frozenUntil;
    private (double U, double V) _frozenAt;
    private double _scrollAnchorV;
    private (double U, double V) _last;
    private (double U, double V) _dwellCenter;
    private double _dwellStart = double.NaN;
    private bool _dwellArmed = true;
    private (double U, double V) _dwellClickAt;
    private bool _dwellUpPending;

    public HandPointer(HandCalibration calibration, HandPointerOptions? options = null)
    {
        Calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
        _options = options ?? new HandPointerOptions();
        _u = new OneEuroFilter(_options.MinCutoffHz, _options.Beta);
        _v = new OneEuroFilter(_options.MinCutoffHz, _options.Beta);
    }

    public HandCalibration Calibration { get; set; }

    public bool IsButtonDown => _click.IsPressed;

    public bool IsScrolling => _scroll.IsPressed;

    /// <summary>The camera point that drives the pointer (also what calibration records).</summary>
    public static (float X, float Y) Anchor(HandResult hand)
    {
        ArgumentNullException.ThrowIfNull(hand);
        var p = hand.Landmarks[HandJoint.IndexBase];
        return (p.X, p.Y);
    }

    /// <summary>Thumb tip ↔ middle-finger tip distance / hand size (the scroll pinch).</summary>
    public static float MiddlePinchRatio(HandResult hand)
    {
        ArgumentNullException.ThrowIfNull(hand);
        var a = hand.Landmarks[HandJoint.ThumbTip];
        var b = hand.Landmarks[HandJoint.MiddleTip];
        float d = MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        return hand.Scale > 0 ? d / hand.Scale : float.NaN;
    }

    public PointerFrame Update(HandResult? hand, double timeSeconds)
    {
        float clickRatio = hand?.PinchRatio ?? float.NaN;
        // the scroll pinch only counts while the index finger is clearly not pinching
        float scrollRatio = hand is null ? float.NaN : clickRatio > _click.OpenRatio || _scroll.IsPressed ? MiddlePinchRatio(hand) : 1f;
        var clickEvent = _scroll.IsPressed || _options.Click == ClickMode.Dwell ? PinchEvent.None : _click.Update(clickRatio, timeSeconds);
        var scrollEvent = _click.IsPressed ? PinchEvent.None : _scroll.Update(scrollRatio, timeSeconds);

        if (hand is null)
        {
            _u.Reset();
            _v.Reset();
            _history.Clear();
            _dwellStart = double.NaN;
            bool up = clickEvent == PinchEvent.Released || _dwellUpPending;
            _dwellUpPending = false;
            return new PointerFrame(false, _last.U, _last.V, up ? PointerAction.ButtonUp : PointerAction.None, 0);
        }

        var (ax, ay) = Anchor(hand);
        var (rawU, rawV) = Calibration.Map(ax, ay);
        double u = _u.Filter((float)rawU, timeSeconds);
        double v = _v.Filter((float)rawV, timeSeconds);
        _history.Enqueue((timeSeconds, u, v));
        while (_history.Count > 0 && _history.Peek().T < timeSeconds - 1)
        {
            _history.Dequeue();
        }

        var action = PointerAction.None;
        if (clickEvent == PinchEvent.Pressed)
        {
            action = PointerAction.ButtonDown;
            _frozenAt = PositionAt(timeSeconds - _options.PressLookBack.TotalSeconds, (u, v));
            _frozenUntil = timeSeconds + _options.ClickFreeze.TotalSeconds;
        }
        else if (clickEvent == PinchEvent.Released)
        {
            action = PointerAction.ButtonUp;
            _frozenAt = _last;
            _frozenUntil = timeSeconds + _options.ClickFreeze.TotalSeconds;
        }

        int notches = 0;
        if (scrollEvent == PinchEvent.Pressed)
        {
            _scrollAnchorV = v;
            _frozenAt = _last;
        }

        if (_scroll.IsPressed)
        {
            // pointer stays put while scrolling; hand up = wheel up
            notches = (int)((_scrollAnchorV - v) / _options.ScrollStep);
            _scrollAnchorV -= notches * _options.ScrollStep;
            _last = _frozenAt;
            return new PointerFrame(true, Clamp(_last.U), Clamp(_last.V), action, notches);
        }

        double dwell = 0;
        if (_options.Click != ClickMode.Pinch && action == PointerAction.None)
        {
            (action, dwell) = UpdateDwell(u, v, timeSeconds);
        }

        _last = timeSeconds < _frozenUntil ? _frozenAt : (u, v);
        return new PointerFrame(true, Clamp(_last.U), Clamp(_last.V), action, notches, dwell);
    }

    /// <summary>Dwell click: the pointer held within <see cref="HandPointerOptions.DwellRadius"/> for the dwell time gives a down + up.</summary>
    private (PointerAction Action, double Progress) UpdateDwell(double u, double v, double t)
    {
        if (_dwellUpPending)
        {
            _dwellUpPending = false;
            return (PointerAction.ButtonUp, 0);
        }

        if (_click.IsPressed)
        {
            _dwellStart = double.NaN;
            return (PointerAction.None, 0);
        }

        if (!_dwellArmed)
        {
            if (Distance((u, v), _dwellClickAt) < _options.DwellRearm)
            {
                return (PointerAction.None, 0);
            }

            _dwellArmed = true;
            _dwellStart = double.NaN;
        }

        if (double.IsNaN(_dwellStart) || Distance((u, v), _dwellCenter) > _options.DwellRadius)
        {
            _dwellCenter = (u, v);
            _dwellStart = t;
            return (PointerAction.None, 0);
        }

        double progress = (t - _dwellStart) / _options.DwellTime.TotalSeconds;
        if (progress < 1)
        {
            return (PointerAction.None, progress);
        }

        // click where the dwell started; the pointer stays there for the click
        _dwellUpPending = true;
        _dwellArmed = false;
        _dwellClickAt = _dwellCenter;
        _dwellStart = double.NaN;
        _frozenAt = _dwellCenter;
        _frozenUntil = t + _options.ClickFreeze.TotalSeconds;
        return (PointerAction.ButtonDown, 1);
    }

    private static double Distance((double U, double V) a, (double U, double V) b) =>
        Math.Sqrt((a.U - b.U) * (a.U - b.U) + (a.V - b.V) * (a.V - b.V));

    public void Reset()
    {
        _click.Reset();
        _scroll.Reset();
        _u.Reset();
        _v.Reset();
        _history.Clear();
        _frozenUntil = 0;
        _dwellStart = double.NaN;
        _dwellArmed = true;
        _dwellUpPending = false;
    }

    private (double U, double V) PositionAt(double t, (double U, double V) fallback)
    {
        foreach (var h in _history)
        {
            if (h.T >= t)
            {
                return (h.U, h.V);
            }
        }

        return fallback;
    }

    private static double Clamp(double x) => Math.Clamp(x, 0, 1);
}

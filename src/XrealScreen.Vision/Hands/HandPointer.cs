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
public readonly record struct PointerFrame(bool HasPointer, double U, double V, PointerAction Action, int ScrollNotches);

public sealed record HandPointerOptions
{
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
        var clickEvent = _scroll.IsPressed ? PinchEvent.None : _click.Update(clickRatio, timeSeconds);
        var scrollEvent = _click.IsPressed ? PinchEvent.None : _scroll.Update(scrollRatio, timeSeconds);

        if (hand is null)
        {
            _u.Reset();
            _v.Reset();
            _history.Clear();
            return new PointerFrame(false, _last.U, _last.V, clickEvent == PinchEvent.Released ? PointerAction.ButtonUp : PointerAction.None, 0);
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

        _last = timeSeconds < _frozenUntil ? _frozenAt : (u, v);
        return new PointerFrame(true, Clamp(_last.U), Clamp(_last.V), action, notches);
    }

    public void Reset()
    {
        _click.Reset();
        _scroll.Reset();
        _u.Reset();
        _v.Reset();
        _history.Clear();
        _frozenUntil = 0;
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

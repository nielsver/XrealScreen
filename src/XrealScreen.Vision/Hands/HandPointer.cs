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
/// <param name="ClickWait">Seconds until a new click is possible (<see cref="HandPointerOptions.RepeatGuard"/>); 0 = ready.</param>
/// <param name="DwellNeedsMove">The pointer must move before the next dwell click can start.</param>
public readonly record struct PointerFrame(bool HasPointer, double U, double V, PointerAction Action, int ScrollNotches, double DwellProgress = 0, double ClickWait = 0, bool DwellNeedsMove = false);

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

    /// <summary>
    /// How long the pointer must stay within <see cref="DwellRadius"/> for a dwell click. 1.2 s: with 0.8 s, slow aiming
    /// next to a target fired clicks the user did not mean (E3 test 2026-11-01).
    /// </summary>
    public TimeSpan DwellTime { get; init; } = TimeSpan.FromMilliseconds(1200);

    /// <summary>Allowed wobble during a dwell, in screen units (0.012 ≈ 23 px on 1920).</summary>
    public double DwellRadius { get; init; } = 0.012;

    /// <summary>After a dwell click the pointer must move this far (screen units) before the next dwell can start.</summary>
    public double DwellRearm { get; init; } = 0.03;

    /// <summary>
    /// No new click this long after the last release (E3 test 2026-11-01: 3 of 5 misses were a dwell click plus a pinch,
    /// or a bouncing pinch, right after a hit). Double-click is not supported while this is non-zero.
    /// </summary>
    public TimeSpan RepeatGuard { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Precision zone (like mouse acceleration): hand movement slower than <see cref="PrecisionSpeed"/> moves the pointer
    /// only this fraction. E3 test 2026-11-01: a "still" hand drifts ~0.04 screen/s (±26 px), more than a 40 px target allows.
    /// 1 = plain absolute mapping.
    /// </summary>
    public double PrecisionGain { get; init; } = 0.3;

    /// <summary>Below this hand speed (screen units/s) the full <see cref="PrecisionGain"/> applies.</summary>
    public double PrecisionSpeed { get; init; } = 0.15;

    /// <summary>At and above this speed the pointer moves 1:1 and catches up with the hand.</summary>
    public double FullSpeed { get; init; } = 0.6;

    /// <summary>Fraction of the pointer–hand offset removed per frame at <see cref="FullSpeed"/> (less when slower).</summary>
    public double CatchUp { get; init; } = 0.3;

    /// <summary>
    /// The pointer never lags the hand by more than this (screen units), so slow movements still reach the screen edges
    /// (E3 test 2026-11-01: the top could not be reached while the hand left the camera view).
    /// </summary>
    public double MaxOffset { get; init; } = 0.05;

    /// <summary>One Euro smoothing of the pointer (on top of the landmark smoothing), in screen units (0..1).</summary>
    public float MinCutoffHz { get; init; } = 0.5f;

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
    private double _lastUp = double.NegativeInfinity;
    private bool _ignoredPinch;
    private (double U, double V, double T)? _prevHand;
    private (double U, double V) _out;

    public HandPointer(HandCalibration calibration, HandPointerOptions? options = null)
    {
        Calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
        _options = options ?? new HandPointerOptions();
        Click = _options.Click;
        _u = new OneEuroFilter(_options.MinCutoffHz, _options.Beta);
        _v = new OneEuroFilter(_options.MinCutoffHz, _options.Beta);
    }

    public HandCalibration Calibration { get; set; }

    /// <summary>How clicks are made; can change at run time (calibration uses <see cref="ClickMode.Dwell"/>).</summary>
    public ClickMode Click { get; set; }

    public bool IsButtonDown => _click.IsPressed && !_ignoredPinch;

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
        clickEvent = FilterPinch(clickEvent, timeSeconds);
        var scrollEvent = _click.IsPressed ? PinchEvent.None : _scroll.Update(scrollRatio, timeSeconds);

        if (hand is null)
        {
            _u.Reset();
            _v.Reset();
            _history.Clear();
            _prevHand = null;
            _dwellStart = double.NaN;
            bool up = clickEvent == PinchEvent.Released || _dwellUpPending;
            _dwellUpPending = false;
            if (up)
            {
                _lastUp = timeSeconds;
            }

            return new PointerFrame(false, _last.U, _last.V, up ? PointerAction.ButtonUp : PointerAction.None, 0);
        }

        var (ax, ay) = Anchor(hand);
        var (rawU, rawV) = Calibration.Map(ax, ay);
        double handU = _u.Filter((float)rawU, timeSeconds);
        double handV = _v.Filter((float)rawV, timeSeconds);
        var (u, v) = ApplyGain(handU, handV, timeSeconds);
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
            _lastUp = timeSeconds;

            // holding still after a pinch click must not dwell-click the same spot again
            _dwellArmed = false;
            _dwellClickAt = _last;
        }

        int notches = 0;
        if (scrollEvent == PinchEvent.Pressed)
        {
            _scrollAnchorV = handV;
            _frozenAt = _last;
        }

        if (_scroll.IsPressed)
        {
            // pointer stays put while scrolling; hand up = wheel up
            notches = (int)((_scrollAnchorV - handV) / _options.ScrollStep);
            _scrollAnchorV -= notches * _options.ScrollStep;
            _last = _frozenAt;
            return new PointerFrame(true, Clamp(_last.U), Clamp(_last.V), action, notches);
        }

        double dwell = 0;
        if (Click != ClickMode.Pinch && action == PointerAction.None)
        {
            (action, dwell) = UpdateDwell(u, v, timeSeconds);
        }

        _last = timeSeconds < _frozenUntil ? _frozenAt : (u, v);
        double wait = Math.Max(0, _lastUp + _options.RepeatGuard.TotalSeconds - timeSeconds);
        return new PointerFrame(true, Clamp(_last.U), Clamp(_last.V), action, notches, dwell, wait, Click != ClickMode.Pinch && !_dwellArmed);
    }

    /// <summary>Hand position → pointer position with the precision zone (slow = fine control, fast = 1:1 and catch up).</summary>
    private (double U, double V) ApplyGain(double u, double v, double t)
    {
        if (_prevHand is not { } prev || t <= prev.T || _options.PrecisionGain >= 1)
        {
            _prevHand = (u, v, t);
            _out = (u, v);
            return _out;
        }

        double du = u - prev.U, dv = v - prev.V;
        double speed = Math.Sqrt(du * du + dv * dv) / (t - prev.T);
        double f = Math.Clamp((speed - _options.PrecisionSpeed) / (_options.FullSpeed - _options.PrecisionSpeed), 0, 1);
        double gain = _options.PrecisionGain + (1 - _options.PrecisionGain) * f;
        double pull = _options.CatchUp * f;
        double ou = _out.U + gain * du, ov = _out.V + gain * dv;
        ou += pull * (u - ou);
        ov += pull * (v - ov);
        double offset = Math.Sqrt((u - ou) * (u - ou) + (v - ov) * (v - ov));
        if (offset > _options.MaxOffset)
        {
            double k = 1 - _options.MaxOffset / offset;
            ou += k * (u - ou);
            ov += k * (v - ov);
        }

        // clamped like a mouse at the screen edge: moving back responds at once
        _out = (Clamp(ou), Clamp(ov));
        _prevHand = (u, v, t);
        return _out;
    }

    /// <summary>Dwell click: the pointer held within <see cref="HandPointerOptions.DwellRadius"/> for the dwell time gives a down + up.</summary>
    private (PointerAction Action, double Progress) UpdateDwell(double u, double v, double t)
    {
        if (_dwellUpPending)
        {
            _dwellUpPending = false;
            _lastUp = t;
            return (PointerAction.ButtonUp, 0);
        }

        if (IsButtonDown || t < _lastUp + _options.RepeatGuard.TotalSeconds)
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

    /// <summary>Drops pinches in dwell-only mode and pinches that start within the repeat guard (and their release).</summary>
    private PinchEvent FilterPinch(PinchEvent e, double t)
    {
        if (e == PinchEvent.Pressed && (Click == ClickMode.Dwell || t < _lastUp + _options.RepeatGuard.TotalSeconds))
        {
            _ignoredPinch = true;
            return PinchEvent.None;
        }

        if (e == PinchEvent.Released && _ignoredPinch)
        {
            _ignoredPinch = false;
            return PinchEvent.None;
        }

        return e;
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
        _prevHand = null;
        _frozenUntil = 0;
        _dwellStart = double.NaN;
        _dwellArmed = true;
        _dwellUpPending = false;
        _lastUp = double.NegativeInfinity;
        _ignoredPinch = false;
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

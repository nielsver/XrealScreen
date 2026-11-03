namespace XrealScreen.Vision.Hands;

public enum PinchEvent
{
    None,
    Pressed,
    Released,
}

/// <summary>
/// Turns the pinch ratio (thumb–index distance / hand size) into press and release events with hysteresis:
/// press below <see cref="CloseRatio"/> held for <see cref="MinHold"/>, release above <see cref="OpenRatio"/>.
/// A lost hand releases after <see cref="LostRelease"/> so a drag never sticks.
/// </summary>
public sealed class PinchDetector
{
    private bool _closingSeen;
    private double _closingSince;
    private bool _openingSeen;
    private double _openingSince;
    private double _lastHandSeconds;
    private float _closedMin;

    // Live test 2026-10-31 (1S + Eye, 15 fps): pinched 0.06–0.12, relaxed open hand 0.4–0.55, lowest idle value 0.29.
    // 0.25 / 0.45 found 11 of 11 pinches with 0 false clicks in an idle minute; 0.35 / 0.6 missed one and false-clicked once.
    public float CloseRatio { get; init; } = 0.25f;

    public float OpenRatio { get; init; } = 0.45f;

    /// <summary>How long the ratio must stay closed before a press. At 15 fps one frame is 67 ms, so 0 = the first closed frame.</summary>
    public TimeSpan MinHold { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// How long the ratio must stay open before a release. A closed pinch seen by the Eye jumps open for single
    /// frames (live test 2026-10-31: press–release–press within 0.1–0.3 s), so one open frame must not release.
    /// </summary>
    public TimeSpan MinRelease { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Also release when the ratio rose this much above its lowest value since the press (held for <see cref="MinRelease"/>),
    /// so small openings between clicks count when the hand pose keeps the ratio low (E3 test 2026-11-01).
    /// </summary>
    public float ReleaseRise { get; init; } = 0.2f;

    public TimeSpan LostRelease { get; init; } = TimeSpan.FromMilliseconds(300);

    public bool IsPressed { get; private set; }

    /// <summary>Feeds one frame. Pass <c>float.NaN</c> when no hand was found.</summary>
    public PinchEvent Update(float ratio, double timeSeconds)
    {
        if (float.IsNaN(ratio))
        {
            _closingSeen = false;
            if (IsPressed && timeSeconds - _lastHandSeconds >= LostRelease.TotalSeconds)
            {
                IsPressed = false;
                return PinchEvent.Released;
            }

            return PinchEvent.None;
        }

        _lastHandSeconds = timeSeconds;
        if (IsPressed)
        {
            _closedMin = Math.Min(_closedMin, ratio);
            if (ratio > Math.Min(OpenRatio, _closedMin + ReleaseRise))
            {
                if (!_openingSeen)
                {
                    _openingSeen = true;
                    _openingSince = timeSeconds;
                }

                if (timeSeconds - _openingSince >= MinRelease.TotalSeconds)
                {
                    IsPressed = false;
                    _openingSeen = false;
                    return PinchEvent.Released;
                }
            }
            else
            {
                _openingSeen = false;
            }

            return PinchEvent.None;
        }

        if (ratio < CloseRatio)
        {
            if (!_closingSeen)
            {
                _closingSeen = true;
                _closingSince = timeSeconds;
            }

            if (timeSeconds - _closingSince >= MinHold.TotalSeconds)
            {
                IsPressed = true;
                _closedMin = ratio;
                _closingSeen = false;
                return PinchEvent.Pressed;
            }
        }
        else
        {
            _closingSeen = false;
        }

        return PinchEvent.None;
    }

    public void Reset()
    {
        IsPressed = false;
        _closingSeen = false;
        _openingSeen = false;
    }
}

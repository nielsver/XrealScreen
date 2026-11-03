namespace XrealScreen.Core.Hud;

/// <summary>One look-target. Yaw and pitch are degrees from the last recenter. Dwell is 0 to 1.</summary>
public readonly record struct HudPin(int Id, string Label, float YawDegrees, float PitchDegrees, float Dwell);

/// <summary>
/// The four glance pins. They stay in the room (they counter-move with your head).
/// Look at one and hold still to use it. Up is the clipboard, left is a timer, right plays or pauses, down mutes.
/// </summary>
public static class HudGlance
{
    public const float AimRadius = 0.22f;

    public static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(700);

    public static HudPin[] Create(string up, string left, string right, string down) =>
    [
        new(0, up, 0f, 18f, 0f),
        new(1, left, 22f, 0f, 0f),
        new(2, right, -22f, 0f, 0f),
        new(3, down, 0f, -16f, 0f),
    ];
}

/// <summary>Fills a pin once when it stays aimed for <see cref="HudGlance.Hold"/>, then waits until you look away.</summary>
public sealed class HudAim
{
    private int _aimed = -1;
    private int _locked = -1;
    private TimeSpan _held;

    public int Advance(int aimed, TimeSpan step, TimeSpan hold)
    {
        if (aimed < 0)
        {
            _aimed = -1;
            _locked = -1;
            _held = TimeSpan.Zero;
            return -1;
        }

        if (aimed != _aimed)
        {
            _aimed = aimed;
            _held = TimeSpan.Zero;
        }

        if (aimed == _locked)
        {
            return -1;
        }

        _held += step;
        if (_held < hold)
        {
            return -1;
        }

        _locked = aimed;
        return aimed;
    }

    public float Dwell(int id, TimeSpan hold)
    {
        if (id != _aimed || id == _locked || hold <= TimeSpan.Zero)
        {
            return 0f;
        }

        return Math.Clamp((float)(_held.TotalSeconds / hold.TotalSeconds), 0f, 1f);
    }
}

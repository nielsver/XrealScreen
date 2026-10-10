using XrealScreen.Core.Tracking;

namespace XrealScreen.Core.Gaming;

/// <summary>How head rotation becomes mouse movement (ADR-0011).</summary>
public enum HeadMouseMode
{
    /// <summary>Head rotation turns the game camera 1:1 (mouse counts per degree of head rotation).</summary>
    Look,

    /// <summary>Joystick: the head's offset from center sets a continuous turn speed.</summary>
    Stick,
}

public sealed record HeadMouseSettings
{
    public HeadMouseMode Mode { get; init; } = HeadMouseMode.Look;

    /// <summary>Look: mouse counts per degree of head rotation (tune until a 90° head turn is 90° in game).</summary>
    public float CountsPerDegree { get; init; } = 20f;

    /// <summary>Multiplier for the vertical axis (both modes).</summary>
    public float VerticalScale { get; init; } = 1f;

    public bool InvertY { get; init; }

    /// <summary>
    /// Look: head rates below this (°/s) are ignored and the gain ramps to 1 at twice this rate.
    /// Removes sensor jitter and the slow fusion corrections. 0 disables.
    /// </summary>
    public float DeadbandDegreesPerSecond { get; init; } = 0.5f;

    /// <summary>Stick: offset from center (degrees) that does nothing.</summary>
    public float StickDeadZoneDegrees { get; init; } = 5f;

    /// <summary>Stick: offset (degrees) at which the turn speed reaches <see cref="StickMaxCountsPerSecond"/>.</summary>
    public float StickFullDegrees { get; init; } = 25f;

    /// <summary>Stick: mouse counts per second at full deflection.</summary>
    public float StickMaxCountsPerSecond { get; init; } = 1500f;
}

/// <summary>Relative mouse movement in counts (+X right, +Y down, like Win32 mouse input).</summary>
public readonly record struct MouseDelta(int X, int Y)
{
    public bool IsZero => X == 0 && Y == 0;
}

/// <summary>
/// Turns head poses into relative mouse movement for games (head look). Uses the absolute world
/// orientation, so the workspace auto-center does not leak into the game camera. Keeps sub-count
/// remainders so slow motion is not lost. Not thread-safe; feed it from the IMU thread.
/// </summary>
public sealed class HeadMouseMapper
{
    private const float MaxDtSeconds = 0.1f;
    private const float RadToDeg = 180f / MathF.PI;
    private bool _hasPrevious;
    private float _previousYaw;
    private float _previousPitch;
    private long _previousTimestampNs;
    private float _centerYaw;
    private float _centerPitch;
    private float _remainderX;
    private float _remainderY;

    public HeadMouseSettings Settings { get; set; } = new();

    /// <summary>Forget the previous pose; the next pose becomes the stick center and moves nothing.</summary>
    public void Reset() => _hasPrevious = false;

    public MouseDelta Update(in HeadPose pose)
    {
        if (!QuaternionMath.IsValidRotation(pose.World))
        {
            return default;
        }

        float yaw = QuaternionMath.Yaw(pose.World) * RadToDeg;
        float pitch = -QuaternionMath.Pitch(pose.World) * RadToDeg; // + = looking up
        if (!_hasPrevious)
        {
            _hasPrevious = true;
            _previousYaw = _centerYaw = yaw;
            _previousPitch = _centerPitch = pitch;
            _previousTimestampNs = pose.TimestampNs;
            _remainderX = _remainderY = 0f;
            return default;
        }

        float dt = Math.Clamp((pose.TimestampNs - _previousTimestampNs) / 1e9f, 0f, MaxDtSeconds);
        float moveX; // counts, + = right
        float moveY; // counts, + = up (flipped to screen Y below)
        var s = Settings;
        if (s.Mode == HeadMouseMode.Look)
        {
            float gain = LookGain(pose.AngularVelocity.Length() * RadToDeg, s.DeadbandDegreesPerSecond);
            moveX = -WrapDegrees(yaw - _previousYaw) * s.CountsPerDegree * gain; // yaw + = turn left
            moveY = (pitch - _previousPitch) * s.CountsPerDegree * gain;
        }
        else
        {
            moveX = -StickSpeed(WrapDegrees(yaw - _centerYaw), s) * dt;
            moveY = StickSpeed(pitch - _centerPitch, s) * dt;
        }

        _previousYaw = yaw;
        _previousPitch = pitch;
        _previousTimestampNs = pose.TimestampNs;

        _remainderX += moveX;
        _remainderY += moveY * s.VerticalScale * (s.InvertY ? 1f : -1f);
        int x = (int)MathF.Truncate(_remainderX);
        int y = (int)MathF.Truncate(_remainderY);
        _remainderX -= x;
        _remainderY -= y;
        return new MouseDelta(x, y);
    }

    private static float LookGain(float rateDegreesPerSecond, float deadband) =>
        deadband <= 0f ? 1f : Math.Clamp((rateDegreesPerSecond - deadband) / deadband, 0f, 1f);

    private static float StickSpeed(float offsetDegrees, HeadMouseSettings s)
    {
        float span = MathF.Max(s.StickFullDegrees - s.StickDeadZoneDegrees, 1e-3f);
        float t = Math.Clamp((MathF.Abs(offsetDegrees) - s.StickDeadZoneDegrees) / span, 0f, 1f);
        return MathF.CopySign(t * s.StickMaxCountsPerSecond, offsetDegrees);
    }

    private static float WrapDegrees(float degrees) => QuaternionMath.WrapAngle(degrees / RadToDeg) * RadToDeg;
}

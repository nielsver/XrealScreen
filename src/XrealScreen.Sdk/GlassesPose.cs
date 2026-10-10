using System.Numerics;
using XrealScreen.Core.Tracking;

namespace XrealScreen.Sdk;

/// <summary>
/// One head pose from the glasses, about 1000 per second.
/// </summary>
/// <remarks>
/// <para>Angles are in degrees, relative to the direction the head had at the last recenter
/// (<see cref="XrealGlasses.Recenter"/>; the first pose recenters automatically).</para>
/// <para>Conventions: <see cref="Yaw"/> + = turned left, <see cref="Pitch"/> + = looking up,
/// <see cref="Roll"/> + = tilted toward the right shoulder. The quaternions use a right-handed,
/// Z-up frame: X forward, Y left, Z up.</para>
/// </remarks>
public readonly record struct GlassesPose
{
    internal GlassesPose(HeadPose pose)
    {
        Tracker = pose;
        TimestampNs = pose.TimestampNs;
        Yaw = pose.YawDegrees;
        Pitch = pose.PitchDegrees;
        Roll = pose.RollDegrees;
        Orientation = pose.Relative;
        WorldOrientation = pose.World;
        AngularVelocity = pose.AngularVelocity * (180f / MathF.PI);
    }

    /// <summary>Glasses clock in nanoseconds (monotonic; starts when the glasses power on).</summary>
    public long TimestampNs { get; }

    /// <summary>Degrees turned left (+) or right (−) from the center.</summary>
    public float Yaw { get; }

    /// <summary>Degrees looking up (+) or down (−). Gravity-referenced, so it does not drift.</summary>
    public float Pitch { get; }

    /// <summary>Degrees tilted toward the right (+) or left (−) shoulder. Gravity-referenced.</summary>
    public float Roll { get; }

    /// <summary>Head orientation relative to the center (yaw re-referenced, pitch and roll absolute).</summary>
    public Quaternion Orientation { get; }

    /// <summary>Absolute head orientation; the yaw origin is arbitrary and is not changed by a recenter.</summary>
    public Quaternion WorldOrientation { get; }

    /// <summary>Gyro-bias-corrected rotation speed in degrees per second, in the head frame (X forward, Y left, Z up).</summary>
    public Vector3 AngularVelocity { get; }

    /// <summary>The tracker's pose, for <see cref="Core.Gaming.HeadMouseMapper"/> and other XrealScreen.Core APIs.</summary>
    public HeadPose Tracker { get; }
}

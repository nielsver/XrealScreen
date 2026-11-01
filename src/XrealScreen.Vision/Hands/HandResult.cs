namespace XrealScreen.Vision.Hands;

/// <summary>A hand joint in camera image pixels; <see cref="Z"/> is relative depth (pixels, wrist ≈ 0, smaller = nearer).</summary>
public readonly record struct HandPoint(float X, float Y, float Z);

/// <summary>Joint indices of the 21 MediaPipe hand landmarks used by this project.</summary>
public static class HandJoint
{
    public const int Wrist = 0;
    public const int ThumbTip = 4;
    public const int IndexBase = 5;
    public const int IndexTip = 8;
    public const int MiddleBase = 9;
    public const int MiddleTip = 12;
    public const int Count = 21;
}

/// <summary>One tracked hand in one camera frame.</summary>
/// <param name="Landmarks">21 joints in image pixels (filtered when the tracker smooths).</param>
/// <param name="Presence">Landmark model confidence that a hand is in the region, 0..1.</param>
/// <param name="Handedness">Model score for "right hand", 0..1 (as seen by the camera).</param>
/// <param name="Region">The image region the landmarks were found in.</param>
/// <param name="FromDetection">True when the palm detector had to find the hand in this frame (not tracked from the last one).</param>
public sealed record HandResult(HandPoint[] Landmarks, float Presence, float Handedness, RotatedRect Region, bool FromDetection, ulong TimestampNs)
{
    /// <summary>Wrist → middle-finger base distance in pixels: the hand's size in the image.</summary>
    public float Scale => Distance(Landmarks[HandJoint.Wrist], Landmarks[HandJoint.MiddleBase]);

    /// <summary>Thumb tip ↔ index tip distance divided by <see cref="Scale"/>: about 0.1–0.2 pinched, about 1 open.</summary>
    public float PinchRatio => Scale > 0 ? Distance(Landmarks[HandJoint.ThumbTip], Landmarks[HandJoint.IndexTip]) / Scale : float.NaN;

    /// <summary>Midpoint of thumb and index tips: the point that moves the pointer.</summary>
    public (float X, float Y) PinchPoint =>
        ((Landmarks[HandJoint.ThumbTip].X + Landmarks[HandJoint.IndexTip].X) / 2, (Landmarks[HandJoint.ThumbTip].Y + Landmarks[HandJoint.IndexTip].Y) / 2);

    private static float Distance(HandPoint a, HandPoint b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}

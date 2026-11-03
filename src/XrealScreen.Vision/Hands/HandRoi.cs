// Hand region rules follow MediaPipe's hand landmarker graphs (Apache-2.0, Google LLC): from a palm, rotate so
// wrist → middle-finger base points up, shift 0.5 box heights towards the fingers, square, scale 2.6; from
// landmarks, use wrist / index / middle / ring bases for the angle, the rotated bounding box of 12 joints,
// shift 0.1, square, scale 2.0. See THIRD-PARTY-NOTICES.md and docs/legal/SOURCES.md.

namespace XrealScreen.Vision.Hands;

internal static class HandRoi
{
    private static readonly int[] RoiJoints = [0, 1, 2, 3, 5, 6, 9, 10, 13, 14, 17, 18];

    /// <summary>The detector's square input: the whole image letterboxed (centred, no rotation).</summary>
    public static RotatedRect Letterbox(int width, int height) => new(width / 2f, height / 2f, Math.Max(width, height), 0f);

    /// <summary>Hand region in image pixels from a palm detection (coordinates of the letterbox crop).</summary>
    public static RotatedRect FromPalm(PalmDetection palm, RotatedRect letterbox)
    {
        var center = letterbox.ToImage(palm.CenterX, palm.CenterY);
        var wrist = letterbox.ToImage(palm.Keypoints[0].X, palm.Keypoints[0].Y);
        var middle = letterbox.ToImage(palm.Keypoints[2].X, palm.Keypoints[2].Y);
        float rotation = AngleUp(wrist, middle);
        return Transform(center.X, center.Y, palm.Width * letterbox.Size, palm.Height * letterbox.Size, rotation, scale: 2.6f, shiftY: -0.5f);
    }

    /// <summary>Hand region for the next frame from this frame's 21 landmarks (image pixels).</summary>
    public static RotatedRect FromLandmarks(ReadOnlySpan<HandPoint> landmarks)
    {
        var wrist = (landmarks[0].X, landmarks[0].Y);
        float mx = ((landmarks[5].X + landmarks[13].X) / 2 + landmarks[9].X) / 2;
        float my = ((landmarks[5].Y + landmarks[13].Y) / 2 + landmarks[9].Y) / 2;
        float rotation = AngleUp(wrist, (mx, my));

        float c = MathF.Cos(rotation);
        float s = MathF.Sin(rotation);
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (int j in RoiJoints)
        {
            // rotate by -rotation into the hand's frame
            float rx = landmarks[j].X * c + landmarks[j].Y * s;
            float ry = -landmarks[j].X * s + landmarks[j].Y * c;
            minX = Math.Min(minX, rx);
            maxX = Math.Max(maxX, rx);
            minY = Math.Min(minY, ry);
            maxY = Math.Max(maxY, ry);
        }

        float cx = (minX + maxX) / 2;
        float cy = (minY + maxY) / 2;
        return Transform(cx * c - cy * s, cx * s + cy * c, maxX - minX, maxY - minY, rotation, scale: 2.0f, shiftY: -0.1f);
    }

    /// <summary>Rotation that turns the direction <paramref name="from"/> → <paramref name="to"/> to "up" (image y down).</summary>
    internal static float AngleUp((float X, float Y) from, (float X, float Y) to) =>
        NormalizeAngle(MathF.PI / 2 - MathF.Atan2(-(to.Y - from.Y), to.X - from.X));

    internal static RotatedRect Transform(float cx, float cy, float width, float height, float rotation, float scale, float shiftY)
    {
        rotation = NormalizeAngle(rotation);
        cx += -height * shiftY * MathF.Sin(rotation);
        cy += height * shiftY * MathF.Cos(rotation);
        return new RotatedRect(cx, cy, Math.Max(width, height) * scale, rotation);
    }

    internal static float NormalizeAngle(float a) => a - 2 * MathF.PI * MathF.Floor((a + MathF.PI) / (2 * MathF.PI));
}

namespace XrealScreen.Core.Hud;

/// <summary>
/// Maps a point on the glasses picture back onto the desk monitor.
/// The picture is contained, so a click in a side bar lands on the desk edge.
/// </summary>
public static class DeskPointer
{
    public readonly record struct Rect(int X, int Y, int Width, int Height);

    public readonly record struct Point(int X, int Y);

    /// <summary>True when the point is inside the rectangle. An empty rectangle contains nothing.</summary>
    public static bool Over(Rect area, int x, int y)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return false;
        }

        return (uint)(x - area.X) < (uint)area.Width && (uint)(y - area.Y) < (uint)area.Height;
    }

    /// <summary>True when the point is on the glasses picture and not also on the desk.</summary>
    public static bool Redirect(Rect glasses, Rect desk, int x, int y) => Over(glasses, x, y) && !Over(desk, x, y);

    /// <summary>
    /// Desk pixel under a glasses-picture pixel. Letterbox and pillarbox clamp onto the picture.
    /// Returns null when either rectangle is empty.
    /// </summary>
    public static Point? Map(Rect glasses, Rect desk, int x, int y)
    {
        if (glasses.Width <= 0 || glasses.Height <= 0 || desk.Width <= 0 || desk.Height <= 0)
        {
            return null;
        }

        float ndcX = ((x - glasses.X) / (float)glasses.Width * 2f) - 1f;
        float ndcY = 1f - ((y - glasses.Y) / (float)glasses.Height * 2f);
        (float left, float right, float top, float bottom) = HudPicturePlacement.Contain(
            desk.Width, desk.Height, glasses.Width, glasses.Height);
        float spanX = right - left;
        float spanY = top - bottom;
        if (spanX <= 0f || spanY <= 0f)
        {
            return null;
        }

        float u = Math.Clamp((ndcX - left) / spanX, 0f, 0.999f);
        float v = Math.Clamp((top - ndcY) / spanY, 0f, 0.999f);
        return new Point(desk.X + (int)(u * desk.Width), desk.Y + (int)(v * desk.Height));
    }
}

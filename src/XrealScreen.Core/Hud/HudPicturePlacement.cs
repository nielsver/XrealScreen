namespace XrealScreen.Core.Hud;

/// <summary>Where a monitor frame sits in the glasses picture. The aspect stays intact.</summary>
public static class HudPicturePlacement
{
    /// <summary>
    /// NDC edges of a contained picture. Y is up. A matching aspect fills the frame.
    /// A wider frame keeps the picture full height and leaves the sides empty.
    /// </summary>
    public static (float Left, float Right, float Top, float Bottom) Contain(
        int pictureWidth, int pictureHeight, int frameWidth, int frameHeight)
    {
        if (pictureWidth <= 0 || pictureHeight <= 0 || frameWidth <= 0 || frameHeight <= 0)
        {
            return (0f, 0f, 0f, 0f);
        }

        float scale = MathF.Min(frameWidth / (float)pictureWidth, frameHeight / (float)pictureHeight);
        float halfW = pictureWidth * scale / frameWidth;
        float halfH = pictureHeight * scale / frameHeight;
        return (-halfW, halfW, halfH, -halfH);
    }

    /// <summary>
    /// NDC edges of a picture placed at <paramref name="centerX"/>, <paramref name="centerY"/>.
    /// <paramref name="size"/> is the full height in NDC. The width keeps the picture's aspect on this frame.
    /// </summary>
    public static (float Left, float Right, float Top, float Bottom) Place(
        int pictureWidth, int pictureHeight, int frameWidth, int frameHeight,
        float centerX, float centerY, float size)
    {
        if (pictureWidth <= 0 || pictureHeight <= 0 || frameWidth <= 0 || frameHeight <= 0 || size <= 0f)
        {
            return (0f, 0f, 0f, 0f);
        }

        float halfH = size * 0.5f;
        float halfW = halfH * (pictureWidth / (float)pictureHeight) * (frameHeight / (float)frameWidth);
        return (centerX - halfW, centerX + halfW, centerY + halfH, centerY - halfH);
    }
}

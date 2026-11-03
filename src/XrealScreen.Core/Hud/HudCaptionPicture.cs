namespace XrealScreen.Core.Hud;

/// <summary>BGRA top-down pixels. A monitor frame fills the glasses; a caption is a small card on top.</summary>
public sealed class HudCaptionPicture
{
    public HudCaptionPicture(int revision, int width, int height, byte[] bgra)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The card has no pixels.");
        }

        if (bgra is null || bgra.Length != width * height * 4)
        {
            throw new ArgumentException("BGRA length must be width × height × 4.", nameof(bgra));
        }

        Revision = revision;
        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Revision { get; }

    public int Width { get; }

    public int Height { get; }

    public byte[] Bgra { get; }
}

using System.Linq;

namespace XrealScreen.Core.Workspace;

/// <summary>A pixel rectangle inside the glasses' video signal.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>Shape of the screens in the glasses-anchor workspace.</summary>
public enum AnchorAspect
{
    /// <summary>16:9 screens (most apps and video).</summary>
    Ratio16x9,

    /// <summary>16:10 screens (a little taller).</summary>
    Ratio16x10,

    /// <summary>Use the full height/width of each slot (aspect follows the glasses signal).</summary>
    Fill,
}

/// <summary>
/// Glasses-anchor workspace (ADR-0010): the glasses world-lock their own ultrawide image (X1 chip,
/// OSD Anchor mode) and we split that image into virtual screens with 1:1 pixels.
/// </summary>
public static class AnchorSplit
{
    public const int MaxScreens = 3;

    /// <summary>
    /// True when the glasses send an ultrawide / tall signal (OSD UltraWide 21:9, 32:9 or 16:18).
    /// With UltraWide Off the 1S sends 1920×1200 ([verified-hw]).
    /// </summary>
    public static bool IsUltrawideSignal(Resolution glasses) =>
        glasses.Height > glasses.Width || (double)glasses.Width / glasses.Height >= 2.2;

    public static double? AspectValue(AnchorAspect aspect) => aspect switch
    {
        AnchorAspect.Ratio16x9 => 16.0 / 9.0,
        AnchorAspect.Ratio16x10 => 16.0 / 10.0,
        _ => null,
    };

    /// <summary>
    /// Splits the glasses signal into <paramref name="count"/> screens with <paramref name="gapPixels"/>
    /// between them: side by side for wide signals, stacked for tall ones (16:18). Each screen is as large
    /// as possible while keeping <paramref name="aspect"/>; the group is centered. Sizes are even pixels.
    /// </summary>
    public static IReadOnlyList<PixelRect> Split(Resolution glasses, int count, int gapPixels = 0, AnchorAspect aspect = AnchorAspect.Fill)
    {
        count = Math.Clamp(count, 1, MaxScreens);
        gapPixels = Math.Max(0, gapPixels);
        bool stacked = glasses.Height > glasses.Width;
        double? ratio = AspectValue(aspect);

        // Slot = the space one screen may use along the split axis; full extent across it.
        double slotMain = ((stacked ? glasses.Height : glasses.Width) - (double)gapPixels * (count - 1)) / count;
        double cross = stacked ? glasses.Width : glasses.Height;
        double w, h;
        if (stacked)
        {
            (w, h) = (cross, slotMain);
            if (ratio is { } r)
            {
                h = Math.Min(slotMain, cross / r);
                w = h * r;
            }
        }
        else
        {
            (w, h) = (slotMain, cross);
            if (ratio is { } r)
            {
                w = Math.Min(slotMain, cross * r);
                h = w / r;
            }
        }

        int width = Even(w), height = Even(h);
        var result = new List<PixelRect>(count);
        if (stacked)
        {
            int total = height * count + gapPixels * (count - 1);
            int y = (glasses.Height - total) / 2;
            int x = (glasses.Width - width) / 2;
            for (int i = 0; i < count; i++)
            {
                result.Add(new PixelRect(x, y + i * (height + gapPixels), width, height));
            }
        }
        else
        {
            int total = width * count + gapPixels * (count - 1);
            int x = (glasses.Width - total) / 2;
            int y = (glasses.Height - height) / 2;
            for (int i = 0; i < count; i++)
            {
                result.Add(new PixelRect(x + i * (width + gapPixels), y, width, height));
            }
        }

        return result;

        static int Even(double v) => Math.Max(2, (int)Math.Floor(v / 2) * 2);
    }

    /// <summary>
    /// Same split as <see cref="Split"/>, shifted so a frame of <paramref name="framePx"/> stays around the screens.
    /// The rectangles are in the full glasses image.
    /// </summary>
    public static IReadOnlyList<PixelRect> Place(Resolution signal, int count, int gapPixels, AnchorAspect aspect, int framePx)
    {
        framePx = Math.Max(0, framePx);
        bool framed = framePx > 0 && signal.Width > framePx * 2 + 32 && signal.Height > framePx * 2 + 32;
        var canvas = framed ? new Resolution(signal.Width - framePx * 2, signal.Height - framePx * 2) : signal;
        int ox = (signal.Width - canvas.Width) / 2;
        int oy = (signal.Height - canvas.Height) / 2;
        return Split(canvas, count, gapPixels, aspect)
            .Select(r => new PixelRect(r.X + ox, r.Y + oy, r.Width, r.Height))
            .ToArray();
    }

    /// <summary>
    /// Where one screen's colors stop and the next screen's colors start, as fractions of the image.
    /// The cut sits in the gap between the screens. The second split is 1 when there is no third screen.
    /// </summary>
    public static (float Split1, float Split2, bool Stacked) Bands(int width, int height, IReadOnlyList<PixelRect> rects)
    {
        bool stacked = height > width;
        float full = stacked ? height : width;
        float split1 = 1f;
        float split2 = 1f;
        if (rects.Count >= 2 && full > 0)
        {
            split1 = Midpoint(rects[0], rects[1], stacked) / full;
        }

        if (rects.Count >= 3 && full > 0)
        {
            split2 = Midpoint(rects[1], rects[2], stacked) / full;
        }

        return (split1, split2, stacked);

        static float Midpoint(PixelRect a, PixelRect b, bool stacked) => stacked
            ? (a.Y + a.Height + b.Y) / 2f
            : (a.X + a.Width + b.X) / 2f;
    }
}

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
    /// <summary>Side-by-side screens in one row (Arc, and a grid that has not filled a row yet).</summary>
    public const int MaxScreens = 3;

    /// <summary>Grid layout: rows of three, up to two rows, inside the same glasses image.</summary>
    public const int MaxGridScreens = 6;

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
    /// <paramref name="columns"/> above 1 places four or more screens in rows (a short last row is centered).
    /// </summary>
    public static IReadOnlyList<PixelRect> Split(Resolution glasses, int count, int gapPixels = 0, AnchorAspect aspect = AnchorAspect.Fill, int columns = 0)
    {
        int limit = columns > 1 ? MaxGridScreens : MaxScreens;
        count = Math.Clamp(count, 1, limit);
        gapPixels = Math.Max(0, gapPixels);
        bool stacked = glasses.Height > glasses.Width;
        double? ratio = AspectValue(aspect);
        if (columns > 1 && !stacked && count > columns)
        {
            return Grid(glasses, count, gapPixels, ratio, columns);
        }

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
    }

    /// <summary>Rows of <paramref name="columns"/> across a wide image. The last row is centered. Columns share one cell size.</summary>
    private static List<PixelRect> Grid(Resolution glasses, int count, int gapPixels, double? ratio, int columns)
    {
        int rows = (count + columns - 1) / columns;
        int cellH = Even((glasses.Height - (double)gapPixels * (rows - 1)) / rows);
        int cellW = Even((glasses.Width - (double)gapPixels * (columns - 1)) / columns);
        int screenW = cellW;
        int screenH = cellH;
        if (ratio is { } r && r > 0)
        {
            screenH = Even(screenW / r);
            if (screenH > cellH)
            {
                screenH = cellH;
                screenW = Even(screenH * r);
            }
        }

        int totalH = cellH * rows + gapPixels * (rows - 1);
        int y0 = Math.Max(0, (glasses.Height - totalH) / 2);
        int fullSpan = screenW * columns + gapPixels * (columns - 1);
        var result = new List<PixelRect>(count);
        for (int row = 0; row < rows; row++)
        {
            int n = Math.Min(columns, count - row * columns);
            int rowSpan = screenW * n + gapPixels * (n - 1);
            int x0 = n < columns
                ? Math.Max(0, (glasses.Width - rowSpan) / 2)
                : Math.Max(0, (glasses.Width - fullSpan) / 2);
            int y = y0 + row * (cellH + gapPixels) + (cellH - screenH) / 2;
            for (int col = 0; col < n; col++)
            {
                result.Add(new PixelRect(x0 + col * (screenW + gapPixels), y, screenW, screenH));
            }
        }

        return result;
    }

    private static int Even(double v) => Math.Max(2, (int)Math.Floor(v / 2) * 2);

    /// <summary>Fraction of the glasses width used when no width was saved. Half stays clear of the sides.</summary>
    public const double DefaultWidthScale = 0.5;

    public static double NormalizeWidthScale(double? saved) => Math.Clamp(saved ?? DefaultWidthScale, 0.35, 1);

    /// <summary>
    /// Same split as <see cref="Split"/>, shifted so a frame of <paramref name="framePx"/> stays around the screens.
    /// <paramref name="widthScale"/> pulls the screens in from the sides. 1 keeps the full width.
    /// <paramref name="columns"/> above 1 uses a grid once there are more screens than columns.
    /// The rectangles are in the full glasses image.
    /// </summary>
    public static IReadOnlyList<PixelRect> Place(Resolution signal, int count, int gapPixels, AnchorAspect aspect, int framePx, double widthScale = 1, int columns = 0)
    {
        framePx = Math.Max(0, framePx);
        bool framed = framePx > 0 && signal.Width > framePx * 2 + 32 && signal.Height > framePx * 2 + 32;
        var canvas = framed ? new Resolution(signal.Width - framePx * 2, signal.Height - framePx * 2) : signal;
        int ox = (signal.Width - canvas.Width) / 2;
        int oy = (signal.Height - canvas.Height) / 2;
        var placed = Split(canvas, count, gapPixels, aspect, columns)
            .Select(r => new PixelRect(r.X + ox, r.Y + oy, r.Width, r.Height))
            .ToArray();
        return ScaleWidth(placed, widthScale);
    }

    /// <summary>
    /// Pulls the screens toward the middle of the group. Height stays. 1 keeps the rectangles unchanged.
    /// </summary>
    public static IReadOnlyList<PixelRect> ScaleWidth(IReadOnlyList<PixelRect> rects, double widthScale)
    {
        ArgumentNullException.ThrowIfNull(rects);
        if (rects.Count == 0 || widthScale >= 0.999)
        {
            return rects;
        }

        widthScale = Math.Clamp(widthScale, 0.35, 1);
        int left = rects.Min(r => r.X);
        int right = rects.Max(r => r.X + r.Width);
        double center = (left + right) / 2.0;
        var scaled = new PixelRect[rects.Count];
        for (int i = 0; i < rects.Count; i++)
        {
            PixelRect r = rects[i];
            int width = Math.Max(2, (int)Math.Floor(r.Width * widthScale / 2.0) * 2);
            double nextCenter = center + (r.X + r.Width / 2.0 - center) * widthScale;
            int x = (int)Math.Round(nextCenter - width / 2.0);
            scaled[i] = new PixelRect(x, r.Y, width, r.Height);
        }

        return scaled;
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

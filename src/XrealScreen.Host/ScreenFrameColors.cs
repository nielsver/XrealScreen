using Vortice.Mathematics;
using XrealScreen.Core.Workspace;

namespace XrealScreen.Host;

/// <summary>Per-screen frame colors. Six entries: screen 1 first, screen 1 second, then screen 2, then screen 3.</summary>
public static class ScreenFrameColors
{
    public const int SlotCount = 6;

    public readonly struct Look
    {
        public bool Draw { get; init; }

        public int Style { get; init; }

        public int ScreenCount { get; init; }

        public float Axis { get; init; }

        public float Split1 { get; init; }

        public float Split2 { get; init; }

        public Color4 A0 { get; init; }

        public Color4 B0 { get; init; }

        public Color4 A1 { get; init; }

        public Color4 B1 { get; init; }

        public Color4 A2 { get; init; }

        public Color4 B2 { get; init; }
    }

    public static bool HasAny(IReadOnlyList<string?>? stored)
    {
        if (stored is null)
        {
            return false;
        }

        foreach (string? entry in stored)
        {
            if (RgbHex.TryParse(entry, out _, out _, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Fills six slots. A linear frame slices the shared pair so the screens still make one sweep.
    /// Every other style copies the shared pair onto each screen.
    /// </summary>
    public static void WriteDefaults(Span<(byte R, byte G, byte B)> slots, (byte R, byte G, byte B) first, (byte R, byte G, byte B) second, int screenCount, bool slice)
    {
        if (slots.Length < SlotCount)
        {
            throw new ArgumentException("Need six color slots.", nameof(slots));
        }

        screenCount = Math.Clamp(screenCount, 1, AnchorSplit.MaxScreens);
        for (int screen = 0; screen < AnchorSplit.MaxScreens; screen++)
        {
            (byte R, byte G, byte B) a;
            (byte R, byte G, byte B) b;
            if (slice)
            {
                a = Mix(first, second, screen / (float)screenCount);
                b = Mix(first, second, (screen + 1) / (float)screenCount);
            }
            else
            {
                a = first;
                b = second;
            }

            slots[screen * 2] = a;
            slots[screen * 2 + 1] = b;
        }
    }

    public static void Overlay(Span<(byte R, byte G, byte B)> slots, IReadOnlyList<string?>? stored)
    {
        if (stored is null)
        {
            return;
        }

        int count = Math.Min(slots.Length, stored.Count);
        for (int i = 0; i < count; i++)
        {
            if (RgbHex.TryParse(stored[i], out byte r, out byte g, out byte b))
            {
                slots[i] = (r, g, b);
            }
        }
    }

    public static Look Create(WorkspaceOptions options, IReadOnlyList<PixelRect> rects, int imageWidth, int imageHeight)
    {
        FrameStyle style = FrameStyles.Resolve(options.AnchorFrame, options.AnchorGradient);
        int count = Math.Clamp(options.ScreenCount, 1, AnchorSplit.MaxScreens);
        var first = RgbHex.ParseOrDefault(options.AnchorGradientLeft, RgbHex.DefaultLeft);
        var second = RgbHex.ParseOrDefault(options.AnchorGradientRight, RgbHex.DefaultRight);
        Span<(byte R, byte G, byte B)> slots = stackalloc (byte R, byte G, byte B)[SlotCount];
        bool custom = HasAny(options.AnchorScreenColors);
        WriteDefaults(slots, first, second, count, slice: style == FrameStyle.Linear && !custom);
        if (custom)
        {
            Overlay(slots, options.AnchorScreenColors);
        }

        var (split1, split2, stacked) = rects.Count >= 2
            ? AnchorSplit.Bands(imageWidth, imageHeight, rects)
            : (1f, 1f, imageHeight > imageWidth);
        if (rects.Count < 2 && count >= 2)
        {
            split1 = 1f / count;
            split2 = count >= 3 ? 2f / count : 1f;
        }

        return new Look
        {
            Draw = style != FrameStyle.SeeThrough,
            Style = FrameStyles.ShaderIndex(style),
            ScreenCount = count,
            Axis = stacked ? 1f : 0f,
            Split1 = split1,
            Split2 = split2,
            A0 = ToColor(slots[0]),
            B0 = ToColor(slots[1]),
            A1 = ToColor(slots[2]),
            B1 = ToColor(slots[3]),
            A2 = ToColor(slots[4]),
            B2 = ToColor(slots[5]),
        };
    }

    public static (byte R, byte G, byte B) Mix((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return (MixByte(a.R, b.R, t), MixByte(a.G, b.G, t), MixByte(a.B, b.B, t));
    }

    private static byte MixByte(byte a, byte b, float t) => (byte)Math.Round(a + (b - a) * t);

    private static Color4 ToColor((byte R, byte G, byte B) color) => new(color.R / 255f, color.G / 255f, color.B / 255f, 1f);
}

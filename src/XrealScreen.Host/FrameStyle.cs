namespace XrealScreen.Host;

/// <summary>
/// What is drawn in the border and the gaps around glasses-anchor screens.
/// See-through is black, which the glasses add as nothing.
/// </summary>
public enum FrameStyle
{
    SeeThrough = 0,
    Linear = 1,
    Vertical = 2,
    Solid = 3,
    Vignette = 4,
    Glow = 5,
}

/// <summary>Maps saved settings onto <see cref="FrameStyle"/>. A missing style keeps the old gradient switch.</summary>
public static class FrameStyles
{
    /// <summary>Pixels of color around glasses-anchor screens. See-through uses none.</summary>
    public const int FramePixels = 36;

    public static FrameStyle Resolve(FrameStyle? style, bool? gradient)
    {
        if (style is { } chosen)
        {
            return chosen;
        }

        return gradient ?? true ? FrameStyle.Linear : FrameStyle.SeeThrough;
    }

    /// <summary>Shader index. 0 linear, 1 vertical, 2 solid, 3 vignette, 4 edge glow.</summary>
    public static int ShaderIndex(FrameStyle style) => style switch
    {
        FrameStyle.Vertical => 1,
        FrameStyle.Solid => 2,
        FrameStyle.Vignette => 3,
        FrameStyle.Glow => 4,
        _ => 0,
    };
}

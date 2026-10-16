namespace XrealScreen.Core.Workspace;

/// <summary>Ultrawide modes offered in the UI. Values are persisted; do not renumber.</summary>
public enum UltrawideMode
{
    Off = 0,
    Wide21x9 = 1,
    Wide32x9 = 2,

    /// <summary>Two 16:9 screens stacked vertically (1920×2160). User decision 2026-09-26.</summary>
    Tall16x18 = 3,
}

public readonly record struct Resolution(int Width, int Height)
{
    public double AspectRatio => (double)Width / Height;

    public override string ToString() => $"{Width}×{Height}";
}

public static class UltrawideModes
{
    /// <summary>Base 16:9 screen used when ultrawide is off.</summary>
    public static readonly Resolution Base = new(1920, 1080);

    public static IReadOnlyList<UltrawideMode> All { get; } =
        [UltrawideMode.Off, UltrawideMode.Wide21x9, UltrawideMode.Wide32x9, UltrawideMode.Tall16x18];

    /// <summary>
    /// Virtual-monitor resolution for a mode. 21:9 uses the common 2560×1080 ("64:27")
    /// signal; actual glasses-native ultrawide signals are unverified (docs/findings/xreal-1s-hardware.md).
    /// </summary>
    public static Resolution GetResolution(UltrawideMode mode) => mode switch
    {
        UltrawideMode.Off => Base,
        UltrawideMode.Wide21x9 => new Resolution(2560, 1080),
        UltrawideMode.Wide32x9 => new Resolution(3840, 1080),
        UltrawideMode.Tall16x18 => new Resolution(1920, 2160),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static string GetDisplayName(UltrawideMode mode) => mode switch
    {
        UltrawideMode.Off => "Full picture",
        UltrawideMode.Wide21x9 => "21:9",
        UltrawideMode.Wide32x9 => "32:9",
        UltrawideMode.Tall16x18 => "16:18 (stacked)",
        _ => mode.ToString(),
    };
}

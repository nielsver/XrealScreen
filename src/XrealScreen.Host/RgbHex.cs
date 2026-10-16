using System.Globalization;

namespace XrealScreen.Host;

/// <summary>RGB color stored as <c>#RRGGBB</c> in workspace settings.</summary>
public static class RgbHex
{
    public const string DefaultLeft = "#1F0A6B";

    public const string DefaultRight = "#008CB8";

    public static string Format(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>Reads <c>#RRGGBB</c>. Returns false when <paramref name="text"/> is missing or not six hex digits.</summary>
    public static bool TryParse(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hex = text.Trim();
        if (hex[0] == '#')
        {
            hex = hex[1..];
        }

        return hex.Length == 6
            && byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }

    /// <summary>Parsed color, or <paramref name="fallbackHex"/> when <paramref name="text"/> is not a color.</summary>
    public static (byte R, byte G, byte B) ParseOrDefault(string? text, string fallbackHex)
    {
        if (TryParse(text, out byte r, out byte g, out byte b) || TryParse(fallbackHex, out r, out g, out b))
        {
            return (r, g, b);
        }

        return (0, 0, 0);
    }
}

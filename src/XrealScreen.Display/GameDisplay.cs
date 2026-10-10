using System.Drawing;
using XrealScreen.Core.Abstractions;

namespace XrealScreen.Display;

/// <summary>
/// Puts games on the glasses (ADR-0011): most games, and every exclusive-fullscreen game, open on
/// the Windows primary monitor, which is the monitor at (0,0).
/// </summary>
public static class GameDisplay
{
    /// <summary>The current layout shifted so the glasses are at (0,0) and therefore primary; relative positions are kept.</summary>
    public static DisplayLayout GlassesPrimary(IReadOnlyList<DisplayMonitor> monitors, string glassesGdiName)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        var glasses = monitors.FirstOrDefault(m => m.GdiDeviceName == glassesGdiName)
            ?? throw new ArgumentException($"{glassesGdiName} is not an active monitor", nameof(glassesGdiName));
        return new DisplayLayout(DateTimeOffset.Now, [.. monitors.Select(m => new MonitorPlacement(
            m.DevicePath, m.EdidId, m.FriendlyName, m.X - glasses.X, m.Y - glasses.Y,
            m.Resolution.Width, m.Resolution.Height, m.RefreshHz, m.GdiDeviceName == glassesGdiName))]);
    }

    /// <summary>
    /// Where a game window goes on <paramref name="to"/>: a window covering its whole monitor
    /// (borderless fullscreen) fills <paramref name="to"/>; any other window is centered, shrunk to fit.
    /// </summary>
    public static Rectangle FitOnto(Rectangle window, Rectangle fromMonitor, Rectangle to) =>
        window.Contains(fromMonitor) ? to : CenterOnto(window, to);

    /// <summary><paramref name="window"/> centered on <paramref name="to"/>, shrunk to fit.</summary>
    public static Rectangle CenterOnto(Rectangle window, Rectangle to)
    {
        int width = Math.Min(window.Width, to.Width);
        int height = Math.Min(window.Height, to.Height);
        return new Rectangle(to.X + (to.Width - width) / 2, to.Y + (to.Height - height) / 2, width, height);
    }
}

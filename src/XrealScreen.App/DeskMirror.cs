using System.Drawing;
using XrealScreen.Core.Abstractions;
using XrealScreen.Display;

namespace XrealScreen.App;

/// <summary>Live frames of the main monitor, never the glasses. One <see cref="LiveScreenCopy"/> stays on the calling thread.</summary>
internal static class DeskMirror
{
    private static readonly object Gate = new();
    private static DisplayMonitor? _desk;
    private static DisplayMonitor? _glasses;
    private static long _chosenAt;

    public readonly record struct Frame(
        byte[] Bgra,
        int Width,
        int Height,
        int DeskX,
        int DeskY,
        int DeskWidth,
        int DeskHeight,
        int GlassesX,
        int GlassesY,
        int GlassesWidth,
        int GlassesHeight);

    public static Frame? Capture(LiveScreenCopy copy, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(copy);
        (DisplayMonitor? desk, DisplayMonitor? glasses) = Choose();
        if (desk is null)
        {
            return null;
        }

        var bounds = new Rectangle(desk.X, desk.Y, desk.Resolution.Width, desk.Resolution.Height);
        LiveScreenCopy.Frame? pixels = copy.Copy(bounds, maxWidth);
        if (pixels is not { } frame)
        {
            return null;
        }

        return new Frame(
            frame.Pixels,
            frame.Width,
            frame.Height,
            desk.X,
            desk.Y,
            desk.Resolution.Width,
            desk.Resolution.Height,
            glasses?.X ?? 0,
            glasses?.Y ?? 0,
            glasses?.Resolution.Width ?? 0,
            glasses?.Resolution.Height ?? 0);
    }

    private static (DisplayMonitor? Desk, DisplayMonitor? Glasses) Choose()
    {
        long now = Environment.TickCount64;
        lock (Gate)
        {
            if (_chosenAt != 0 && now - _chosenAt < 2000)
            {
                return (_desk, _glasses);
            }
        }

        IReadOnlyList<DisplayMonitor> monitors = new CcdDisplayTopology().GetActiveMonitors();
        DisplayMonitor? desk = ReadingMonitor.Choose(monitors);
        DisplayMonitor? glasses = GlassesDisplayLocator.FindGlasses(monitors);
        lock (Gate)
        {
            _desk = desk;
            _glasses = glasses;
            _chosenAt = Environment.TickCount64;
        }

        return (desk, glasses);
    }
}

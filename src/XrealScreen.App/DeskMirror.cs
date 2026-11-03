using System.Drawing;
using XrealScreen.Core.Abstractions;
using XrealScreen.Display;

namespace XrealScreen.App;

/// <summary>One frame of the main monitor, never the glasses.</summary>
internal static class DeskMirror
{
    private static readonly object Gate = new();
    private static DisplayMonitor? _monitor;
    private static long _chosenAt;

    public readonly record struct Pixels(byte[] Bgra, int Width, int Height);

    public static async Task<Pixels?> CaptureAsync(int maxWidth, CancellationToken cancellationToken)
    {
        DisplayMonitor? monitor = await Task.Run(Choose, cancellationToken).ConfigureAwait(false);
        if (monitor is null)
        {
            return null;
        }

        var bounds = new Rectangle(monitor.X, monitor.Y, monitor.Resolution.Width, monitor.Resolution.Height);
        byte[] pixels = await Task.Run(() => ScreenCapture.CaptureBgra(bounds), cancellationToken).ConfigureAwait(false);
        (byte[] shrunk, int width, int height) = ScreenCapture.Shrink(pixels, bounds.Width, bounds.Height, maxWidth);
        return new Pixels(shrunk, width, height);
    }

    private static DisplayMonitor? Choose()
    {
        long now = Environment.TickCount64;
        lock (Gate)
        {
            if (_monitor is not null && now - _chosenAt < 2000)
            {
                return _monitor;
            }
        }

        DisplayMonitor? chosen = ReadingMonitor.Choose(new CcdDisplayTopology().GetActiveMonitors());
        lock (Gate)
        {
            _monitor = chosen;
            _chosenAt = Environment.TickCount64;
        }

        return chosen;
    }
}

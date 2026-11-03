using XrealScreen.Core.Abstractions;

namespace XrealScreen.Display;

/// <summary>Which monitor a glance reads. Never the glasses, and never a virtual display.</summary>
public static class ReadingMonitor
{
    public static DisplayMonitor? Choose(IReadOnlyList<DisplayMonitor> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        DisplayMonitor? primary = null;
        DisplayMonitor? largest = null;
        long largestArea = -1;
        foreach (DisplayMonitor monitor in monitors)
        {
            if (GlassesDisplayLocator.Identify(monitor) is not null || monitor.EdidManufacturer == "CHY")
            {
                continue;
            }

            if (monitor.IsPrimary)
            {
                primary = monitor;
            }

            long area = (long)monitor.Resolution.Width * monitor.Resolution.Height;
            if (area > largestArea)
            {
                largestArea = area;
                largest = monitor;
            }
        }

        return primary ?? largest;
    }
}

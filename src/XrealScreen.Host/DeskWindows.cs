using System.Drawing;
using XrealScreen.Core.Abstractions;
using XrealScreen.Display;

namespace XrealScreen.Host;

/// <summary>Puts windows back on the physical monitor when a virtual screen goes away or hides them.</summary>
internal static class DeskWindows
{
    /// <summary>Moves app windows off each virtual screen onto the physical desk, before those screens are removed.</summary>
    public static int ReturnVirtualWindowsHome(IReadOnlyList<DisplayMonitor> monitors, IReadOnlyList<Rectangle> virtualBounds)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(virtualBounds);
        if (virtualBounds.Count == 0)
        {
            return 0;
        }

        Rectangle? desk = Desk(monitors);
        if (desk is null)
        {
            return 0;
        }

        int moved = 0;
        foreach (Rectangle from in virtualBounds)
        {
            try
            {
                moved += WindowMover.MoveWindows(from, desk.Value);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // One window that cannot be enumerated must not skip the rest of the restore.
            }
        }

        return moved;
    }

    /// <summary>
    /// Parks windows whose center is not on a monitor the user can see.
    /// While the workspace picture covers the glasses, a window left there counts as lost.
    /// </summary>
    public static int RescueStrays(IReadOnlyList<DisplayMonitor> monitors, bool glassesAreCovered)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        Rectangle? desk = Desk(monitors);
        if (desk is null)
        {
            return 0;
        }

        var safe = new List<Rectangle>(monitors.Count);
        foreach (DisplayMonitor monitor in monitors)
        {
            if (glassesAreCovered && GlassesDisplayLocator.Identify(monitor) is not null)
            {
                continue;
            }

            safe.Add(Bounds(monitor));
        }

        try
        {
            return WindowMover.RescueOffScreen(desk.Value, safe);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return 0;
        }
    }

    private static Rectangle? Desk(IReadOnlyList<DisplayMonitor> monitors)
    {
        var candidates = new DeskCandidate[monitors.Count];
        for (int i = 0; i < monitors.Count; i++)
        {
            DisplayMonitor monitor = monitors[i];
            bool skip = monitor.EdidManufacturer == "CHY" || GlassesDisplayLocator.Identify(monitor) is not null;
            candidates[i] = new DeskCandidate(monitor.IsPrimary, skip, (long)monitor.Resolution.Width * monitor.Resolution.Height);
        }

        int index = AppOpenPlacement.PreferredDesk(candidates);
        return index < 0 ? null : Bounds(monitors[index]);
    }

    private static Rectangle Bounds(DisplayMonitor monitor) =>
        new(monitor.X, monitor.Y, monitor.Resolution.Width, monitor.Resolution.Height);
}

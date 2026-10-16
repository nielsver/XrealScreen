using System.Drawing;

namespace XrealScreen.Display;

/// <summary>Decides where an app window goes when it is opened from a virtual screen.</summary>
public static class AppOpenPlacement
{
    /// <summary>The virtual screen under <paramref name="cursor"/>, or null when the pointer is on the desk.</summary>
    public static Rectangle? ScreenAt(Point cursor, IReadOnlyList<Rectangle> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        foreach (Rectangle screen in screens)
        {
            if (screen.Contains(cursor))
            {
                return screen;
            }
        }

        return null;
    }

    /// <summary>
    /// Where to put <paramref name="window"/> so it opens on <paramref name="screen"/>.
    /// Null when the window is already on that screen.
    /// A window wider than the screen is shrunk to fit and centered. A smaller window keeps its size and is centered.
    /// </summary>
    public static Rectangle? MoveTarget(Rectangle window, Rectangle screen)
    {
        if (screen.Width < 1 || screen.Height < 1 || window.Width < 1 || window.Height < 1)
        {
            return null;
        }

        int cx = window.X + window.Width / 2;
        int cy = window.Y + window.Height / 2;
        if (screen.Contains(cx, cy))
        {
            return null;
        }

        int width = Math.Min(window.Width, screen.Width);
        int height = Math.Min(window.Height, screen.Height);
        int x = screen.X + (screen.Width - width) / 2;
        int y = screen.Y + (screen.Height - height) / 2;
        return new Rectangle(x, y, width, height);
    }

    /// <summary>
    /// True when the window was created after the workspace started, the mouse button is up,
    /// and the pointer is on a virtual screen. A window that was already open stays where it is.
    /// </summary>
    public static bool ShouldMoveNewWindow(bool createdAfterStart, bool buttonDown, Rectangle? screenUnderCursor) =>
        createdAfterStart && !buttonDown && screenUnderCursor is not null;

    /// <summary>True when the center of <paramref name="window"/> lies on one of <paramref name="areas"/>.</summary>
    public static bool CenterIsInside(Rectangle window, IReadOnlyList<Rectangle> areas)
    {
        ArgumentNullException.ThrowIfNull(areas);
        if (window.Width < 1 || window.Height < 1)
        {
            return false;
        }

        int cx = window.X + (window.Width / 2);
        int cy = window.Y + (window.Height / 2);
        foreach (Rectangle area in areas)
        {
            if (area.Contains(cx, cy))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parks a lost window on <paramref name="desk"/>, centered. A window larger than the desk is shrunk to fit.
    /// A smaller window keeps its size.
    /// </summary>
    public static Rectangle ParkOnDesk(Rectangle window, Rectangle desk)
    {
        int width = Math.Min(Math.Max(window.Width, 1), Math.Max(desk.Width, 1));
        int height = Math.Min(Math.Max(window.Height, 1), Math.Max(desk.Height, 1));
        int x = desk.X + Math.Max(0, (desk.Width - width) / 2);
        int y = desk.Y + Math.Max(0, (desk.Height - height) / 2);
        return new Rectangle(x, y, width, height);
    }

    /// <summary>
    /// Index of the physical monitor lost windows return to.
    /// Virtual screens and the glasses are skipped. The real primary wins; otherwise the largest real monitor.
    /// Returns -1 when every monitor is skipped.
    /// </summary>
    public static int PreferredDesk(IReadOnlyList<DeskCandidate> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        int best = -1;
        long bestArea = -1;
        for (int i = 0; i < monitors.Count; i++)
        {
            DeskCandidate monitor = monitors[i];
            if (monitor.Skip)
            {
                continue;
            }

            if (monitor.IsPrimary)
            {
                return i;
            }

            if (monitor.Area > bestArea)
            {
                bestArea = monitor.Area;
                best = i;
            }
        }

        return best;
    }
}

/// <summary>One monitor considered when choosing where a lost window returns.</summary>
/// <param name="Skip">Virtual screens and the glasses. Those are not the home monitor.</param>
public readonly record struct DeskCandidate(bool IsPrimary, bool Skip, long Area);

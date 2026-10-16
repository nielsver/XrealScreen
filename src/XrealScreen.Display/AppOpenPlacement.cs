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
}

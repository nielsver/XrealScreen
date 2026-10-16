using System.Drawing;
using System.Runtime.InteropServices;

namespace XrealScreen.Display;

/// <summary>Puts the cursor on a virtual screen. Sending a window uses <see cref="WindowMover"/>.</summary>
public static partial class ScreenDesk
{
    /// <summary>
    /// Index of the virtual screen that should receive the cursor.
    /// <paramref name="primaryScreen"/> is 0 when the physical monitor stays primary, otherwise the 1-based virtual screen.
    /// </summary>
    public static int CursorScreen(int primaryScreen, int screenCount)
    {
        screenCount = Math.Max(1, screenCount);
        if (primaryScreen >= 1 && primaryScreen <= screenCount)
        {
            return primaryScreen - 1;
        }

        return 0;
    }

    /// <summary>Center of a monitor rectangle, in desktop pixels.</summary>
    public static Point Center(Rectangle screen) => new(screen.X + screen.Width / 2, screen.Y + screen.Height / 2);

    /// <summary>Moves the pointer to the center of <paramref name="screen"/>.</summary>
    public static bool TryPlaceCursor(Rectangle screen)
    {
        Point point = Center(screen);
        return SetCursorPos(point.X, point.Y);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);
}

using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace XrealScreen.Display;

/// <summary>
/// Moves app windows off a monitor (the glasses output, which the workspace covers with its own
/// full-screen image) onto another monitor. Keeps each window's offset, size (clamped) and
/// normal / maximized / minimized state.
/// </summary>
public static partial class WindowMover
{
    /// <summary>Moves all normal top-level windows on the monitor covering <paramref name="from"/> into <paramref name="to"/>.</summary>
    /// <returns>Number of windows moved.</returns>
    public static int MoveWindows(Rectangle from, Rectangle to)
    {
        nint fromMonitor = MonitorFromPoint(new POINT(from.X + 1, from.Y + 1), MONITOR_DEFAULTTONULL);
        if (fromMonitor == 0 || from.Contains(to.X + 1, to.Y + 1))
        {
            return 0;
        }

        int moved = 0;
        foreach (nint hwnd in TopLevelAppWindows())
        {
            if (MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST) != fromMonitor)
            {
                continue;
            }

            var placement = new WINDOWPLACEMENT { Length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(hwnd, ref placement))
            {
                continue;
            }

            var r = placement.NormalPosition;
            var target = Relocate(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), from, to);
            placement.NormalPosition = new RECT(target.Left, target.Top, target.Right, target.Bottom);
            placement.ShowCmd = placement.ShowCmd switch
            {
                SW_SHOWMAXIMIZED => SW_SHOWMAXIMIZED,
                SW_SHOWMINIMIZED or SW_MINIMIZE or SW_SHOWMINNOACTIVE => SW_SHOWMINNOACTIVE,
                _ => SW_SHOWNOACTIVATE,
            };

            // Fails for windows of elevated apps (UIPI); those stay where they are.
            if (SetWindowPlacement(hwnd, ref placement))
            {
                moved++;
            }
        }

        return moved;
    }

    /// <summary>Moves the foreground window onto <paramref name="to"/> (see <see cref="GameDisplay.FitOnto"/>); maximized windows stay maximized.</summary>
    /// <returns>The moved window's title, or null when there is no movable foreground window.</returns>
    public static unsafe string? MoveForegroundWindow(Rectangle to)
    {
        nint hwnd = GetForegroundWindow();
        if (hwnd == 0 || !IsAppWindow(hwnd))
        {
            return null;
        }

        var placement = new WINDOWPLACEMENT { Length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
        var info = new MONITORINFO { Size = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetWindowPlacement(hwnd, ref placement) || !GetWindowRect(hwnd, out var rect)
            || !GetMonitorInfoW(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info))
        {
            return null;
        }

        var window = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var monitor = Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
        bool moved;
        if (placement.ShowCmd == SW_SHOWMAXIMIZED)
        {
            // Placement coordinates are workspace coordinates; equal to screen coordinates with the taskbar at the bottom.
            var r = placement.NormalPosition;
            var target = GameDisplay.CenterOnto(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), to);
            placement.NormalPosition = new RECT(target.Left, target.Top, target.Right, target.Bottom);
            moved = SetWindowPlacement(hwnd, ref placement);
        }
        else
        {
            var target = GameDisplay.FitOnto(window, monitor, to);
            moved = SetWindowPos(hwnd, 0, target.X, target.Y, target.Width, target.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        }

        if (!moved)
        {
            return null; // elevated window (UIPI)
        }

        int length = GetWindowTextLengthW(hwnd);
        char* title = stackalloc char[Math.Min(length + 1, 256)];
        int copied = GetWindowTextW(hwnd, title, Math.Min(length + 1, 256));
        return new string(title, 0, Math.Max(0, copied));
    }

    /// <summary>Same offset inside <paramref name="to"/> as inside <paramref name="from"/>, shrunk and shifted to fit.</summary>
    public static Rectangle Relocate(Rectangle window, Rectangle from, Rectangle to)
    {
        int width = Math.Min(window.Width, to.Width);
        int height = Math.Min(window.Height, to.Height);
        int x = Math.Clamp(to.X + (window.X - from.X), to.X, to.Right - width);
        int y = Math.Clamp(to.Y + (window.Y - from.Y), to.Y, to.Bottom - height);
        return new Rectangle(x, y, width, height);
    }

    /// <summary>Visible, uncloaked, unowned, titled windows — what Alt+Tab shows, minus the shell.</summary>
    private static unsafe List<nint> TopLevelAppWindows()
    {
        var all = new List<nint>();
        var handle = GCHandle.Alloc(all);
        try
        {
            if (EnumWindows(&Collect, GCHandle.ToIntPtr(handle)) == 0)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            handle.Free();
        }

        return all.Where(IsAppWindow).ToList();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Collect(nint hwnd, nint state)
    {
        ((List<nint>)GCHandle.FromIntPtr(state).Target!).Add(hwnd);
        return 1;
    }

    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    private static unsafe bool IsAppWindow(nint hwnd)
    {
        if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != 0 || GetWindowTextLengthW(hwnd) == 0)
        {
            return false;
        }

        if ((GetWindowLongPtrW(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        int cloaked = 0;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        char* buffer = stackalloc char[64];
        int length = GetClassNameW(hwnd, buffer, 64);
        string className = new(buffer, 0, Math.Max(0, length));
        return !ShellClasses.Contains(className);
    }

    private const uint MONITOR_DEFAULTTONULL = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const uint DWMWA_CLOAKED = 14;
    private const uint SW_SHOWMINIMIZED = 2;
    private const uint SW_SHOWMAXIMIZED = 3;
    private const uint SW_SHOWNOACTIVATE = 4;
    private const uint SW_MINIMIZE = 6;
    private const uint SW_SHOWMINNOACTIVE = 7;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct POINT(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct RECT(int Left, int Top, int Right, int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public uint Length;
        public uint Flags;
        public uint ShowCmd;
        public POINT MinPosition;
        public POINT MaxPosition;
        public RECT NormalPosition;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint state);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint hwnd, uint cmd);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextLengthW(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial long GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    private static unsafe partial int GetClassNameW(nint hwnd, char* className, int maxCount);

    [LibraryImport("dwmapi.dll")]
    private static unsafe partial int DwmGetWindowAttribute(nint hwnd, uint attribute, void* value, int size);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(POINT point, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);

    [LibraryImport("user32.dll")]
    private static unsafe partial int GetWindowTextW(nint hwnd, char* text, int maxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
}

using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using XrealScreen.Display;

namespace XrealScreen.App;

/// <summary>
/// While a workspace is running, an app opened from a virtual screen opens on that screen.
/// The pointer is the signal: a click on a virtual screen leaves the cursor there.
/// </summary>
internal sealed unsafe partial class AppOpenHook : IDisposable
{
    private const uint EventForeground = 0x0003;
    private const uint EventShow = 0x8002;
    private const uint SkipOwnProcess = 0x0002;
    private const int VkLeftButton = 0x01;

    private static AppOpenHook? _current;

    private readonly DispatcherQueue _dispatcher;
    private readonly Func<IReadOnlyList<Rectangle>> _screens;
    private readonly HashSet<nint> _pending = [];
    private nint _foreground;
    private nint _show;

    public AppOpenHook(DispatcherQueue dispatcher, Func<IReadOnlyList<Rectangle>> screens)
    {
        _dispatcher = dispatcher;
        _screens = screens;
    }

    public unsafe void Start()
    {
        if (_foreground != 0)
        {
            return;
        }

        _current = this;
        _foreground = SetWinEventHook(EventForeground, EventForeground, 0, &OnEvent, 0, 0, SkipOwnProcess);
        _show = SetWinEventHook(EventShow, EventShow, 0, &OnEvent, 0, 0, SkipOwnProcess);
    }

    public void Stop()
    {
        if (_current == this)
        {
            _current = null;
        }

        if (_foreground != 0)
        {
            UnhookWinEvent(_foreground);
            _foreground = 0;
        }

        if (_show != 0)
        {
            UnhookWinEvent(_show);
            _show = 0;
        }
    }

    public void Dispose() => Stop();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (hwnd == 0 || idObject != 0 || idChild != 0)
        {
            return;
        }

        _current?.Schedule(hwnd);
    }

    private void Schedule(nint hwnd)
    {
        if (!_pending.Add(hwnd))
        {
            return;
        }

        var timer = _dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(160);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            _pending.Remove(hwnd);
            Place(hwnd);
        };
        timer.Start();
    }

    private void Place(nint hwnd)
    {
        if ((GetAsyncKeyState(VkLeftButton) & 0x8000) != 0 || !GetCursorPos(out POINT point))
        {
            return;
        }

        Rectangle? screen = AppOpenPlacement.ScreenAt(new Point(point.X, point.Y), _screens());
        if (screen is { } target)
        {
            WindowMover.TryMoveOnto(hwnd, target);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    private static partial nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint module,
        delegate* unmanaged[Stdcall]<nint, uint, nint, int, int, uint, uint, void> proc,
        uint processId,
        uint threadId,
        uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);
}

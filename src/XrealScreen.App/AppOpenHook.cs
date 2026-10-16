using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using XrealScreen.Display;

namespace XrealScreen.App;

/// <summary>
/// While a workspace is running, an app opened from a virtual screen opens on that screen.
/// Only windows created after the workspace started are moved. A window already on the desk stays there.
/// </summary>
internal sealed unsafe partial class AppOpenHook : IDisposable
{
    private const uint EventForeground = 0x0003;
    private const uint EventCreate = 0x8000;
    private const uint EventShow = 0x8002;
    private const uint SkipOwnProcess = 0x0002;
    private const int VkLeftButton = 0x01;
    private const long WatchFor = 10_000;

    private static AppOpenHook? _current;

    private readonly object _gate = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<IReadOnlyList<Rectangle>> _screens;
    private readonly Dictionary<nint, long> _born = [];
    private readonly HashSet<nint> _pending = [];
    private nint _foreground;
    private nint _create;
    private nint _show;

    public AppOpenHook(DispatcherQueue dispatcher, Func<IReadOnlyList<Rectangle>> screens)
    {
        _dispatcher = dispatcher;
        _screens = screens;
    }

    public void Start()
    {
        if (_foreground != 0)
        {
            return;
        }

        _current = this;
        _foreground = SetWinEventHook(EventForeground, EventForeground, 0, &OnEvent, 0, 0, SkipOwnProcess);
        _create = SetWinEventHook(EventCreate, EventCreate, 0, &OnEvent, 0, 0, SkipOwnProcess);
        _show = SetWinEventHook(EventShow, EventShow, 0, &OnEvent, 0, 0, SkipOwnProcess);
    }

    public void Stop()
    {
        if (_current == this)
        {
            _current = null;
        }

        lock (_gate)
        {
            _born.Clear();
            _pending.Clear();
        }

        Unhook(ref _foreground);
        Unhook(ref _create);
        Unhook(ref _show);
    }

    public void Dispose() => Stop();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (hwnd == 0 || idObject != 0 || idChild != 0)
        {
            return;
        }

        if (eventType == EventCreate)
        {
            _current?.NoteCreated(hwnd);
            return;
        }

        _current?.Schedule(hwnd);
    }

    private void NoteCreated(nint hwnd)
    {
        lock (_gate)
        {
            _born[hwnd] = Environment.TickCount64;
        }
    }

    private void Schedule(nint hwnd)
    {
        lock (_gate)
        {
            if (!_born.ContainsKey(hwnd) || !_pending.Add(hwnd))
            {
                return;
            }
        }

        var timer = _dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(160);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Place(hwnd);
        timer.Start();
    }

    private void Place(nint hwnd)
    {
        if (!StillWatching(hwnd))
        {
            return;
        }

        if ((GetAsyncKeyState(VkLeftButton) & 0x8000) != 0)
        {
            return;
        }

        Rectangle? screen = null;
        if (GetCursorPos(out POINT point))
        {
            screen = AppOpenPlacement.ScreenAt(new Point(point.X, point.Y), _screens());
        }

        if (AppOpenPlacement.ShouldMoveNewWindow(createdAfterStart: true, buttonDown: false, screen))
        {
            if (WindowMover.TryPlaceOpened(hwnd, screen!.Value) != WindowOpenMove.NotReady)
            {
                Forget(hwnd);
            }

            return;
        }

        if (WindowMover.IsSettledAppWindow(hwnd))
        {
            Forget(hwnd);
        }
    }

    private bool StillWatching(nint hwnd)
    {
        lock (_gate)
        {
            _pending.Remove(hwnd);
            if (!_born.TryGetValue(hwnd, out long bornAt))
            {
                return false;
            }

            if (Environment.TickCount64 - bornAt > WatchFor)
            {
                _born.Remove(hwnd);
                return false;
            }

            return true;
        }
    }

    private void Forget(nint hwnd)
    {
        lock (_gate)
        {
            _born.Remove(hwnd);
        }
    }

    private static void Unhook(ref nint hook)
    {
        if (hook == 0)
        {
            return;
        }

        UnhookWinEvent(hook);
        hook = 0;
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

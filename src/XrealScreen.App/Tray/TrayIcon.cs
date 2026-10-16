using System.Runtime.InteropServices;

namespace XrealScreen.App.Tray;

/// <summary>Commands offered by the tray menu and the global hotkey.</summary>
public enum TrayCommand
{
    Open = 1,
    StartWorkspace = 2,
    StopWorkspace = 3,
    Recenter = 4,
    Exit = 5,

    /// <summary>Ctrl+Alt+W: start when stopped, stop when running.</summary>
    ToggleWorkspace = 6,

    /// <summary>Ctrl+Alt+P: screenshot of what the glasses show.</summary>
    Screenshot = 7,

    /// <summary>Ctrl+Alt+1, 2, or 3: send the window you are using to that virtual screen.</summary>
    SendToScreen1 = 8,
    SendToScreen2 = 9,
    SendToScreen3 = 10,

    /// <summary>Ctrl+Alt+0: put the cursor on the main virtual screen.</summary>
    FindCursor = 11,
}

/// <summary>
/// Notification-area (tray) icon with a context menu and the global Ctrl+Alt+W hotkey (M6).
/// Uses Shell_NotifyIcon on a message-only window created on the UI thread, so callbacks run on the
/// UI thread (WinUI pumps Win32 messages for that thread).
/// </summary>
public sealed unsafe partial class TrayIcon : IDisposable
{
    private const uint WM_APP_TRAY = 0x8000 + 1; // WM_APP + 1
    private const int HotkeyToggle = 1;
    private const int HotkeyScreenshot = 2;
    private const int HotkeyScreen1 = 3;
    private const int HotkeyScreen2 = 4;
    private const int HotkeyScreen3 = 5;
    private const int HotkeyFindCursor = 6;
    private static TrayIcon? s_current;

    private readonly uint _taskbarCreated;
    private IntPtr _hwnd;
    private IntPtr _icon;
    private bool _added;

    public TrayIcon(string iconPath, string tooltip)
    {
        s_current = this;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        fixed (char* className = "XrealScreenTray")
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = GetModuleHandle(IntPtr.Zero),
                lpszClassName = className,
            };
            RegisterClassEx(&wc);
        }

        _hwnd = CreateWindowEx(0, "XrealScreenTray", "XrealScreen tray", 0, 0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, GetModuleHandle(IntPtr.Zero), IntPtr.Zero);
        _icon = LoadImage(IntPtr.Zero, iconPath, 1 /* IMAGE_ICON */, 0, 0, 0x10 | 0x40 /* LR_LOADFROMFILE | LR_DEFAULTSIZE */);
        HotkeyAvailable = RegisterHotKey(_hwnd, HotkeyToggle, 0x0002 | 0x0001 | 0x4000 /* CTRL | ALT | NOREPEAT */, 0x57 /* W */);
        ScreenshotHotkeyAvailable = RegisterHotKey(_hwnd, HotkeyScreenshot, 0x0002 | 0x0001 | 0x4000, 0x50 /* P */);
        RegisterHotKey(_hwnd, HotkeyScreen1, 0x0002 | 0x0001 | 0x4000, 0x31 /* 1 */);
        RegisterHotKey(_hwnd, HotkeyScreen2, 0x0002 | 0x0001 | 0x4000, 0x32 /* 2 */);
        RegisterHotKey(_hwnd, HotkeyScreen3, 0x0002 | 0x0001 | 0x4000, 0x33 /* 3 */);
        RegisterHotKey(_hwnd, HotkeyFindCursor, 0x0002 | 0x0001 | 0x4000, 0x30 /* 0 */);
        Tooltip = tooltip;
        Add();
    }

    /// <summary>Raised on the UI thread for menu choices, clicks and the hotkey.</summary>
    public event Action<TrayCommand>? CommandInvoked;

    /// <summary>False when Ctrl+Alt+W is already taken by another program.</summary>
    public bool HotkeyAvailable { get; }

    /// <summary>False when Ctrl+Alt+P is already taken by another program.</summary>
    public bool ScreenshotHotkeyAvailable { get; }

    /// <summary>Workspace state for the menu (Start vs Stop enabled).</summary>
    public bool WorkspaceRunning { get; set; }

    public string Tooltip { get; private set; }

    public void SetTooltip(string text)
    {
        Tooltip = text;
        var data = Data(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIcon(NIM_MODIFY, &data);
    }

    /// <summary>Shows a notification balloon from the tray icon.</summary>
    public void ShowNotification(string title, string text)
    {
        var data = Data(NIF_INFO);
        Copy(title, data.szInfoTitle, 64);
        Copy(text, data.szInfo, 256);
        data.dwInfoFlags = 0x1; // NIIF_INFO
        Shell_NotifyIcon(NIM_MODIFY, &data);
    }

    private void Add()
    {
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIcon(NIM_ADD, &data);
    }

    private NOTIFYICONDATAW Data(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = WM_APP_TRAY,
            hIcon = _icon,
        };
        Copy(Tooltip, data.szTip, 128);
        return data;
    }

    private static void Copy(string text, char* destination, int capacity)
    {
        int n = Math.Min(text.Length, capacity - 1);
        for (int i = 0; i < n; i++)
        {
            destination[i] = text[i];
        }

        destination[n] = '\0';
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_SEPARATOR = 0x800, MF_DEFAULT = 0x1000;
        AppendMenu(menu, MF_STRING | MF_DEFAULT, (IntPtr)TrayCommand.Open, "Open XrealScreen");
        AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
        AppendMenu(menu, WorkspaceRunning ? MF_GRAYED : MF_STRING, (IntPtr)TrayCommand.StartWorkspace, "Start workspace\tCtrl+Alt+W");
        AppendMenu(menu, WorkspaceRunning ? MF_STRING : MF_GRAYED, (IntPtr)TrayCommand.StopWorkspace, "Stop workspace\tCtrl+Alt+W");
        AppendMenu(menu, WorkspaceRunning ? MF_STRING : MF_GRAYED, (IntPtr)TrayCommand.Recenter, "Recenter");
        AppendMenu(menu, WorkspaceRunning ? MF_STRING : MF_GRAYED, (IntPtr)TrayCommand.Screenshot, "Screenshot of the glasses\tCtrl+Alt+P");
        AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
        AppendMenu(menu, MF_STRING, (IntPtr)TrayCommand.Exit, "Exit");

        POINT pt;
        GetCursorPos(&pt);
        SetForegroundWindow(_hwnd); // required so the menu closes when clicking elsewhere
        int cmd = TrackPopupMenu(menu, 0x0100 | 0x0002 /* TPM_RETURNCMD | TPM_RIGHTBUTTON */, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        PostMessage(_hwnd, 0 /* WM_NULL */, 0, 0);
        DestroyMenu(menu);
        if (cmd != 0)
        {
            CommandInvoked?.Invoke((TrayCommand)cmd);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var self = s_current;
        if (self is not null)
        {
            if (msg == WM_APP_TRAY)
            {
                switch ((uint)lParam & 0xFFFF)
                {
                    case 0x0202: // WM_LBUTTONUP
                        self.CommandInvoked?.Invoke(TrayCommand.Open);
                        break;
                    case 0x0205: // WM_RBUTTONUP
                        self.ShowMenu();
                        break;
                }

                return 0;
            }

            if (msg == 0x0312 /* WM_HOTKEY */ && wParam == HotkeyToggle)
            {
                self.CommandInvoked?.Invoke(TrayCommand.ToggleWorkspace);
                return 0;
            }

            if (msg == 0x0312 /* WM_HOTKEY */ && wParam == HotkeyScreenshot)
            {
                self.CommandInvoked?.Invoke(TrayCommand.Screenshot);
                return 0;
            }

            if (msg == 0x0312 /* WM_HOTKEY */ && wParam == HotkeyScreen1)
            {
                self.CommandInvoked?.Invoke(TrayCommand.SendToScreen1);
                return 0;
            }

            if (msg == 0x0312 /* WM_HOTKEY */ && wParam == HotkeyScreen2)
            {
                self.CommandInvoked?.Invoke(TrayCommand.SendToScreen2);
                return 0;
            }

            if (msg == 0x0312 /* WM_HOTKEY */ && wParam == HotkeyScreen3)
            {
                self.CommandInvoked?.Invoke(TrayCommand.SendToScreen3);
                return 0;
            }

            if (msg == 0x0312 /* WM_HOTKEY */ && wParam == HotkeyFindCursor)
            {
                self.CommandInvoked?.Invoke(TrayCommand.FindCursor);
                return 0;
            }

            if (msg == self._taskbarCreated)
            {
                self.Add(); // Explorer restarted: put the icon back
                return 0;
            }
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data(0);
            Shell_NotifyIcon(NIM_DELETE, &data);
            _added = false;
        }

        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HotkeyToggle);
            UnregisterHotKey(_hwnd, HotkeyScreenshot);
            UnregisterHotKey(_hwnd, HotkeyScreen1);
            UnregisterHotKey(_hwnd, HotkeyScreen2);
            UnregisterHotKey(_hwnd, HotkeyScreen3);
            UnregisterHotKey(_hwnd, HotkeyFindCursor);
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }

        s_current = null;
    }

    // ---- interop -------------------------------------------------------------------------------

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public IntPtr hIconSm;
    }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIcon(uint message, NOTIFYICONDATAW* data);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")]
    private static partial ushort RegisterClassEx(WNDCLASSEXW* wc);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hwnd);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessage(string name);

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr LoadImage(IntPtr instance, string name, uint type, int cx, int cy, uint load);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr icon);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenu(IntPtr menu, uint flags, IntPtr id, string? text);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(IntPtr menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(POINT* point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hwnd, int id);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    private static partial IntPtr GetModuleHandle(IntPtr name);
}

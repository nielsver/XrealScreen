using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using XrealScreen.Core.Hud;

namespace XrealScreen.App;

/// <summary>
/// While the HUD runs, a pointer that moves onto the glasses is sent back to the mirrored monitor.
/// Installed and removed on the UI thread. Does not use <c>SendInputPointer</c>, so hand control still
/// treats this movement as the user's own mouse. A drag that slides onto the glasses keeps its button.
/// </summary>
internal static unsafe partial class DeskMouseRelay
{
    internal readonly record struct Target(
        int GlassesX, int GlassesY, int GlassesWidth, int GlassesHeight,
        int DeskX, int DeskY, int DeskWidth, int DeskHeight);

    private const int MouseHook = 14;
    private const uint Move = 0x0001;
    private const uint Absolute = 0x8000;
    private const uint VirtualDesk = 0x4000;
    private const uint Injected = 0x3;

    private static Aim? _aim;
    private static DispatcherQueue? _queue;
    private static nint _hook;

    /// <summary>Installs the hook. Call on the thread that pumps window messages.</summary>
    internal static void Start()
    {
        if (_hook != 0)
        {
            return;
        }

        DispatcherQueue? queue = DispatcherQueue.GetForCurrentThread();
        if (queue is null)
        {
            return;
        }

        _queue = queue;
        _hook = SetWindowsHookEx(MouseHook, &OnMouse, GetModuleHandle(0), 0);
    }

    /// <summary>Removes the hook. Call on the same thread as <see cref="Start"/>.</summary>
    internal static void Stop()
    {
        nint hook = _hook;
        _hook = 0;
        _queue = null;
        if (hook != 0)
        {
            UnhookWindowsHookEx(hook);
        }

        Volatile.Write(ref _aim, null);
    }

    internal static void SetTarget(Target? target)
    {
        if (target is not { } value || value.GlassesWidth <= 0 || value.GlassesHeight <= 0 || value.DeskWidth <= 0 || value.DeskHeight <= 0)
        {
            Volatile.Write(ref _aim, null);
            return;
        }

        Volatile.Write(ref _aim, new Aim
        {
            Glasses = new DeskPointer.Rect(value.GlassesX, value.GlassesY, value.GlassesWidth, value.GlassesHeight),
            Desk = new DeskPointer.Rect(value.DeskX, value.DeskY, value.DeskWidth, value.DeskHeight),
        });
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint OnMouse(int code, nint wParam, nint lParam)
    {
        nint hook = _hook;
        if (code < 0 || lParam == 0 || hook == 0)
        {
            return CallNextHookEx(hook, code, wParam, lParam);
        }

        try
        {
            var info = (HookInfo*)lParam;
            if ((info->Flags & Injected) != 0)
            {
                return CallNextHookEx(hook, code, wParam, lParam);
            }

            Aim? aim = Volatile.Read(ref _aim);
            if (aim is null || !DeskPointer.Redirect(aim.Glasses, aim.Desk, info->X, info->Y))
            {
                return CallNextHookEx(hook, code, wParam, lParam);
            }

            uint message = (uint)wParam;
            if (!Known(message))
            {
                return CallNextHookEx(hook, code, wParam, lParam);
            }

            DeskPointer.Point? mapped = DeskPointer.Map(aim.Glasses, aim.Desk, info->X, info->Y);
            DispatcherQueue? queue = _queue;
            if (mapped is not { } point || queue is null)
            {
                return CallNextHookEx(hook, code, wParam, lParam);
            }

            // SendInput from inside this hook can stall the pointer. Replay after the hook returns.
            int x = point.X;
            int y = point.Y;
            uint data = info->MouseData;
            if (!queue.TryEnqueue(() => Replay(x, y, message, data)))
            {
                return CallNextHookEx(hook, code, wParam, lParam);
            }

            return 1;
        }
        catch (Exception)
        {
            return CallNextHookEx(hook, code, wParam, lParam);
        }
    }

    private static bool Known(uint message) => message is 0x0200 or 0x0201 or 0x0202 or 0x0204 or 0x0205 or 0x0207 or 0x0208 or 0x020A or 0x020B or 0x020C or 0x020E;

    private static bool Replay(int x, int y, uint message, uint mouseData)
    {
        uint flags = Move | Absolute | VirtualDesk;
        uint data = 0;
        switch (message)
        {
            case 0x0200:
                break;
            case 0x0201:
                flags |= 0x0002;
                break;
            case 0x0202:
                flags |= 0x0004;
                break;
            case 0x0204:
                flags |= 0x0008;
                break;
            case 0x0205:
                flags |= 0x0010;
                break;
            case 0x0207:
                flags |= 0x0020;
                break;
            case 0x0208:
                flags |= 0x0040;
                break;
            case 0x020A:
                flags |= 0x0800;
                data = (uint)(short)(mouseData >> 16);
                break;
            case 0x020E:
                flags |= 0x1000;
                data = (uint)(short)(mouseData >> 16);
                break;
            case 0x020B:
                flags |= 0x0080;
                data = mouseData >> 16;
                break;
            case 0x020C:
                flags |= 0x0100;
                data = mouseData >> 16;
                break;
            default:
                return false;
        }

        int vx = GetSystemMetrics(76);
        int vy = GetSystemMetrics(77);
        int vw = GetSystemMetrics(78);
        int vh = GetSystemMetrics(79);
        if (vw <= 1 || vh <= 1)
        {
            return false;
        }

        int nx = (int)((x - vx + 0.5) * 65535.0 / vw);
        int ny = (int)((y - vy + 0.5) * 65535.0 / vh);
        var input = new Input
        {
            Type = 0,
            Mouse = new MouseInput { Dx = nx, Dy = ny, MouseData = data, Flags = flags },
        };
        return SendInput(1, &input, sizeof(Input)) == 1;
    }

    private sealed class Aim
    {
        public DeskPointer.Rect Glasses;
        public DeskPointer.Rect Desk;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HookInfo
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    private static partial nint GetModuleHandle(nint module);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static partial nint SetWindowsHookEx(int idHook, delegate* unmanaged[Stdcall]<int, nint, nint, nint> proc, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial uint SendInput(uint count, Input* inputs, int size);
}

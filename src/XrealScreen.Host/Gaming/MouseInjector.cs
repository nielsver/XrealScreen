using System.Runtime.InteropServices;
using XrealScreen.Core.Gaming;

namespace XrealScreen.Host.Gaming;

/// <summary>
/// Sends relative mouse movement with Win32 <c>SendInput</c> (ADR-0011). Games that read the mouse
/// through raw input or DirectInput (e.g. Skyrim) see it like a real mouse; no driver needed.
/// Windows blocks injection into elevated windows unless this process is elevated too (UIPI).
/// </summary>
public static partial class MouseInjector
{
    private const uint InputMouse = 0;
    private const uint MouseEventFMove = 0x0001;

    /// <summary>Sends <paramref name="delta"/>; returns false when Windows rejected it (e.g. UIPI, secure desktop).</summary>
    public static bool Move(MouseDelta delta)
    {
        if (delta.IsZero)
        {
            return true;
        }

        var input = new[] { new Input { Type = InputMouse, Mouse = new MouseInput { Dx = delta.X, Dy = delta.Y, Flags = MouseEventFMove } } };
        return SendInput(1, input, Marshal.SizeOf<Input>()) == 1;
    }

    /// <summary>True while the virtual key is held down (works when another app has focus).</summary>
    public static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

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

    // MOUSEINPUT is the largest member of the INPUT union, so no explicit padding is needed.
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, [In] Input[] inputs, int size);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);
}

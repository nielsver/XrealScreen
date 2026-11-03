using System.Runtime.InteropServices;

namespace XrealScreen.Host.Hands;

/// <summary>Where <see cref="HandControlEngine"/> sends pointer input. Implement it to test or redirect.</summary>
public interface IPointerOutput
{
    /// <summary>Moves the pointer to a virtual-desktop pixel.</summary>
    bool MoveTo(int x, int y);

    bool LeftButton(bool down);

    /// <summary>Mouse wheel; positive = away from the user (scroll up).</summary>
    bool Wheel(int notches);
}

/// <summary>
/// Absolute pointer moves, left button and wheel through Win32 <c>SendInput</c> (like head look, ADR-0011/0016).
/// Windows blocks input into elevated windows unless this process is elevated (UIPI); the calls then return false.
/// </summary>
public sealed partial class SendInputPointer : IPointerOutput
{
    private const uint InputMouse = 0;
    private const uint Move = 0x0001;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint WheelFlag = 0x0800;
    private const uint Absolute = 0x8000;
    private const uint VirtualDesk = 0x4000;
    private const int WheelDelta = 120;

    private SendInputPointer()
    {
    }

    public static SendInputPointer Instance { get; } = new();

    /// <summary><see cref="Environment.TickCount64"/> of the last input this process sent (for <see cref="RealInputMonitor"/>).</summary>
    public static long LastSentTick { get; private set; }

    public bool MoveTo(int x, int y)
    {
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        if (vw <= 1 || vh <= 1)
        {
            return false;
        }

        int nx = (int)((x - vx + 0.5) * 65535.0 / vw);
        int ny = (int)((y - vy + 0.5) * 65535.0 / vh);
        return Send(new MouseInput { Dx = nx, Dy = ny, Flags = Move | Absolute | VirtualDesk });
    }

    public bool LeftButton(bool down) => Send(new MouseInput { Flags = down ? LeftDown : LeftUp });

    public bool Wheel(int notches) => notches == 0 || Send(new MouseInput { MouseData = (uint)(notches * WheelDelta), Flags = WheelFlag });

    private static bool Send(MouseInput mouse)
    {
        var input = new[] { new Input { Type = InputMouse, Mouse = mouse } };
        bool ok = SendInput(1, input, Marshal.SizeOf<Input>()) == 1;
        LastSentTick = Environment.TickCount64;
        return ok;
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

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, [In] Input[] inputs, int size);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);
}

/// <summary>
/// Detects real mouse/keyboard use: Windows' last-input time moving past the last input this process sent.
/// Hand control pauses while the user uses the real mouse or keyboard (ADR-0016).
/// </summary>
public static partial class RealInputMonitor
{
    /// <summary>Tolerance for the 10–16 ms tick resolution.</summary>
    private const long ToleranceMs = 40;

    /// <summary>Milliseconds since the last real input, or null when the last input was ours.</summary>
    public static long? MillisecondsSinceRealInput()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return null;
        }

        // GetLastInputInfo uses the 32-bit tick count; compare in that domain
        uint now = (uint)Environment.TickCount;
        uint ours = (uint)SendInputPointer.LastSentTick;
        long sinceInput = unchecked((int)(now - info.Time));
        long sinceOurs = unchecked((int)(now - ours));
        return SendInputPointer.LastSentTick != 0 && sinceInput + ToleranceMs >= sinceOurs ? null : sinceInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);
}

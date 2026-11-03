using System.Drawing;
using System.Runtime.InteropServices;

namespace XrealScreen.Display;

/// <summary>
/// Live copy of one monitor into a reused bitmap, with the system cursor drawn on top.
/// One instance stays on the thread that created it. Screenshots still use <see cref="ScreenCapture"/>,
/// which does not include the pointer.
/// </summary>
public sealed unsafe partial class LiveScreenCopy : IDisposable
{
    public readonly record struct Frame(byte[] Pixels, int Width, int Height);

    private const int ColorOnColor = 3;
    private const int CursorShowing = 1;
    private const int DiNormal = 3;
    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;
    private static readonly nint PerMonitorV2 = -4;

    private nint _previousDpi;
    private bool _restoreDpi;
    private nint _screenDc;
    private nint _memoryDc;
    private nint _dib;
    private nint _restoreBitmap;
    private byte* _bits;
    private int _width;
    private int _height;
    private bool _disposed;

    public LiveScreenCopy()
    {
        _previousDpi = SetThreadDpiAwarenessContext(PerMonitorV2);
        _restoreDpi = _previousDpi != 0;
    }

    /// <summary>Top-down BGRA of <paramref name="source"/>, width at most <paramref name="maxWidth"/>. Null when the copy fails.</summary>
    public Frame? Copy(Rectangle source, int maxWidth)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (source.Width <= 0 || source.Height <= 0 || maxWidth <= 0)
        {
            return null;
        }

        int destWidth = Math.Min(source.Width, maxWidth);
        int destHeight = Math.Max(1, (int)((long)source.Height * destWidth / source.Width));
        Ensure(destWidth, destHeight);
        uint rop = SrcCopy | CaptureBlt;
        if (!StretchBlt(_memoryDc, 0, 0, destWidth, destHeight, _screenDc, source.X, source.Y, source.Width, source.Height, rop)
            && !StretchBlt(_memoryDc, 0, 0, destWidth, destHeight, _screenDc, source.X, source.Y, source.Width, source.Height, SrcCopy))
        {
            return null;
        }

        DrawCursor(source, destWidth, destHeight);
        int bytes = destWidth * destHeight * 4;
        byte[] pixels = new byte[bytes];
        fixed (byte* destination = pixels)
        {
            Buffer.MemoryCopy(_bits, destination, bytes, bytes);
        }

        for (int i = 3; i < bytes; i += 4)
        {
            pixels[i] = 255;
        }

        return new Frame(pixels, destWidth, destHeight);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_memoryDc != 0 && _restoreBitmap != 0)
        {
            SelectObject(_memoryDc, _restoreBitmap);
        }

        if (_dib != 0)
        {
            DeleteObject(_dib);
            _dib = 0;
        }

        if (_memoryDc != 0)
        {
            DeleteDC(_memoryDc);
            _memoryDc = 0;
        }

        if (_screenDc != 0)
        {
            _ = ReleaseDC(0, _screenDc);
            _screenDc = 0;
        }

        if (_restoreDpi)
        {
            SetThreadDpiAwarenessContext(_previousDpi);
            _restoreDpi = false;
        }
    }

    private void Ensure(int width, int height)
    {
        if (_screenDc == 0)
        {
            _screenDc = GetDC(0);
            _memoryDc = CreateCompatibleDC(_screenDc);
            if (_screenDc == 0 || _memoryDc == 0)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "Could not open a screen DC.");
            }

            _ = SetStretchBltMode(_memoryDc, ColorOnColor);
        }

        if (_dib != 0 && _width == width && _height == height)
        {
            return;
        }

        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
        };
        byte* bits;
        nint dib = CreateDIBSection(_memoryDc, &header, 0, &bits, 0, 0);
        if (dib == 0 || bits == null)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "CreateDIBSection failed.");
        }

        nint previous = SelectObject(_memoryDc, dib);
        if (_restoreBitmap == 0)
        {
            _restoreBitmap = previous;
        }
        else if (_dib != 0)
        {
            DeleteObject(_dib);
        }

        _dib = dib;
        _bits = bits;
        _width = width;
        _height = height;
    }

    private void DrawCursor(Rectangle source, int destWidth, int destHeight)
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info) || info.Flags != CursorShowing || info.Cursor == 0)
        {
            return;
        }

        if (!GetIconInfo(info.Cursor, out IconInfo icon))
        {
            return;
        }

        try
        {
            int x = (int)(((long)info.X - source.X) * destWidth / source.Width) - (int)icon.HotspotX;
            int y = (int)(((long)info.Y - source.Y) * destHeight / source.Height) - (int)icon.HotspotY;
            _ = DrawIconEx(_memoryDc, x, y, info.Cursor, 0, 0, 0, 0, DiNormal);
        }
        finally
        {
            if (icon.Mask != 0)
            {
                DeleteObject(icon.Mask);
            }

            if (icon.Color != 0)
            {
                DeleteObject(icon.Color);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size;
        public int Flags;
        public nint Cursor;
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int Icon;
        public uint HotspotX;
        public uint HotspotY;
        public nint Mask;
        public nint Color;
    }

    [LibraryImport("user32.dll")]
    private static partial nint SetThreadDpiAwarenessContext(nint context);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorInfo(ref CursorInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(nint cursor, out IconInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial int SetStretchBltMode(nint dc, int mode);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint obj);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool StretchBlt(
        nint dest, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, uint rop);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static unsafe partial nint CreateDIBSection(nint dc, BitmapInfoHeader* info, uint usage, byte** bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
}

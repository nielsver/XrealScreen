using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace XrealScreen.Display;

/// <summary>
/// Saves what a monitor shows (after DWM composition, without the mouse pointer) as a PNG.
/// Used for screenshots of the glasses output. Coordinates are physical desktop pixels, so the
/// calling process must be per-monitor DPI aware (the app and the engine are).
/// </summary>
public static partial class ScreenCapture
{
    public static async Task SavePngAsync(Rectangle bounds, string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        byte[] pixels = CaptureBgra(bounds);

        using var memory = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memory).AsTask(cancellationToken).ConfigureAwait(false);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)bounds.Width, (uint)bounds.Height, 96, 96, pixels);
        await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        memory.Seek(0);
        await using var file = File.Create(path);
        await memory.AsStreamForRead().CopyToAsync(file, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Top-down BGRA pixels of the screen area, fully opaque. Coordinates are physical pixels.</summary>
    public static unsafe byte[] CaptureBgra(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), bounds, "Empty capture area.");
        }

        // Physical pixels whatever the process DPI awareness is (the CLI is not DPI aware).
        nint previousDpi = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        nint screen = GetDC(0);
        nint memoryDc = CreateCompatibleDC(screen);
        nint bitmap = CreateCompatibleBitmap(screen, bounds.Width, bounds.Height);
        nint previous = SelectObject(memoryDc, bitmap);
        try
        {
            if (!BitBlt(memoryDc, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, SRCCOPY | CAPTUREBLT))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "BitBlt failed");
            }

            var header = new BITMAPINFOHEADER
            {
                Size = (uint)sizeof(BITMAPINFOHEADER),
                Width = bounds.Width,
                Height = -bounds.Height, // top-down
                Planes = 1,
                BitCount = 32,
            };
            byte[] pixels = new byte[bounds.Width * bounds.Height * 4];
            fixed (byte* p = pixels)
            {
                SelectObject(memoryDc, previous); // GetDIBits needs the bitmap deselected
                previous = 0;
                if (GetDIBits(memoryDc, bitmap, 0, (uint)bounds.Height, p, &header, DIB_RGB_COLORS) == 0)
                {
                    throw new System.ComponentModel.Win32Exception("GetDIBits failed");
                }
            }

            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255; // GDI leaves alpha at 0
            }

            return pixels;
        }
        finally
        {
            if (previous != 0)
            {
                SelectObject(memoryDc, previous);
            }

            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            _ = ReleaseDC(0, screen);
            if (previousDpi != 0)
            {
                SetThreadDpiAwarenessContext(previousDpi);
            }
        }
    }

    private static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;
    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
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

    [LibraryImport("user32.dll")]
    private static partial nint SetThreadDpiAwarenessContext(nint context);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleBitmap(nint dc, int width, int height);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint obj);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint rop);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, void* bits, BITMAPINFOHEADER* info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
}

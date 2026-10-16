using Vortice.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace XrealScreen.Render.Capture;

/// <summary>A captured frame. <see cref="Texture"/> is only valid inside the callback — copy it.</summary>
public readonly record struct CapturedFrame(ID3D11Texture2D Texture, int Width, int Height, TimeSpan SystemRelativeTime);

/// <summary>
/// Captures one monitor with Windows.Graphics.Capture into D3D11 textures on the given device.
/// Frames arrive on a worker thread (free-threaded pool). Frames are never written to disk.
/// </summary>
public sealed class MonitorCapture : IDisposable
{
    private readonly IDirect3DDevice _winRtDevice;
    private readonly GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private bool _disposed;

    public MonitorCapture(ID3D11Device device, IntPtr hmonitor, bool borderless = true, bool captureCursor = true)
    {
        ArgumentNullException.ThrowIfNull(device);
        _winRtDevice = CaptureInterop.CreateWinRtDevice(device);
        _item = CaptureInterop.CreateItemForMonitor(hmonitor);
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winRtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _item.Size);
        _session = _pool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = captureCursor;
        if (borderless)
        {
            // Requires the borderless capture permission (see RequestBorderlessAsync); ignored otherwise.
            try
            {
                _session.IsBorderRequired = false;
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        _pool.FrameArrived += OnFrameArrived;
    }

    public event Action<CapturedFrame>? FrameArrived;

    public int Width => _item.Size.Width;

    public int Height => _item.Size.Height;

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    /// <summary>HMONITOR at a desktop point (e.g. a monitor's top-left from CCD), or zero.</summary>
    public static IntPtr MonitorAt(int x, int y) => CaptureInterop.MonitorAt(x, y);

    /// <summary>HMONITOR for a GDI device name such as <c>\\.\DISPLAY9</c>, or zero.</summary>
    public static IntPtr MonitorForDevice(string deviceName) => CaptureInterop.MonitorForDevice(deviceName);

    /// <summary>GDI device name of an HMONITOR, or empty.</summary>
    public static string DeviceName(IntPtr hmonitor) => CaptureInterop.DeviceName(hmonitor);

    /// <summary>Asks Windows for borderless capture (no yellow frame). Returns true when allowed.</summary>
    public static async Task<bool> RequestBorderlessAsync()
    {
        var status = await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
        return status == Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus.Allowed;
    }

    public void Start() => _session.StartCapture();

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame is null || _disposed)
        {
            return;
        }

        using var texture = CaptureInterop.GetTexture(frame.Surface);
        FrameArrived?.Invoke(new CapturedFrame(texture, frame.ContentSize.Width, frame.ContentSize.Height, frame.SystemRelativeTime));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pool.FrameArrived -= OnFrameArrived;
        _session.Dispose();
        _pool.Dispose();
        _winRtDevice.Dispose();
    }
}

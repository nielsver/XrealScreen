using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using XrealScreen.Render.Scene;

namespace XrealScreen.Render.Presenter;

/// <summary>Frame statistics of a presenter run.</summary>
/// <param name="LatchToScanoutMsP50">Pose latch → the vblank at which DXGI reports the frame on screen (median; NaN if unknown).</param>
/// <param name="PresentPath">How DXGI reports the frames reached the glasses (composition mode counts).</param>
public sealed record PresenterStats(long Frames, double Seconds, double FrameMsP50, double FrameMsP99, double LatchToPresentMsAvg, long MissedFrames, string Output, string PresentPath,
    double LatchToScanoutMsP50 = double.NaN, double LatchToScanoutMsP99 = double.NaN)
{
    public double Fps => Seconds > 0 ? Frames / Seconds : 0;

    public override string ToString() =>
        $"frames={Frames} fps={Fps:F1} frame p50={FrameMsP50:F2} ms p99={FrameMsP99:F2} ms latch→present={LatchToPresentMsAvg:F2} ms latch→scanout p50={LatchToScanoutMsP50:F2} ms p99={LatchToScanoutMsP99:F2} ms missed(>1.5×)={MissedFrames} output={Output} path={PresentPath}";
}

/// <summary>
/// Borderless fullscreen window on the glasses monitor with a flip-model swapchain.
/// A dedicated render thread waits for the glasses' vblank, late-latches the head pose, renders and presents.
/// Global hotkeys: Ctrl+Alt+R = recenter, Ctrl+Alt+Q = stop, Ctrl+Alt+Plus/Minus = screens closer/farther
/// (Ctrl+Alt+arrows are usually taken by the graphics driver for screen rotation, [verified-hw]).
/// When the window has focus also Esc / R.
/// </summary>
public sealed unsafe class GlassesPresenter : IDisposable
{
    private static GlassesPresenter? s_current;

    private readonly GraphicsDevice _gd;
    private readonly IGlassesScene _scene;
    private readonly GlassesOptics _optics;
    private readonly Func<HeadView> _latchPose;
    private readonly int _x, _y, _width, _height;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<double> _frameMs = new(1 << 16);
    private readonly List<double> _scanoutMs = new(1 << 16);

    private Thread? _windowThread;
    private Thread? _renderThread;
    private IntPtr _hwnd;
    private volatile bool _stop;
    private double _latchSumMs;
    private long _missed;
    private long _frames;
    private string _output = "?";
    private string _presentPath = "?";
    private double _seconds;
    private Exception? _renderError;

    public GlassesPresenter(GraphicsDevice graphics, IGlassesScene scene, GlassesOptics optics, Func<HeadView> latchPose, int x, int y, int width, int height)
    {
        _gd = graphics;
        _scene = scene;
        _optics = optics;
        _latchPose = latchPose;
        (_x, _y, _width, _height) = (x, y, width, height);
    }

    /// <summary>Flat mode: screens are drawn pixel-exact without head tracking (glasses-anchor workspace).</summary>
    public bool Flat { get; init; }

    /// <summary>
    /// Late latch: read the pose and render this many ms BEFORE the next glasses vblank instead of right
    /// after the previous one (0 = right after vblank). Only helps when the swapchain is flipped directly;
    /// while DWM composes (always, on the test PC) it did not lower latch→scan-out (ADR-0009, 2026-09-27).
    /// </summary>
    public double LatchLeadMs { get; init; }

    /// <summary>Raised on the window thread when the user presses R.</summary>
    public event Action? RecenterRequested;

    /// <summary>Raised for Ctrl+Alt+Plus (-1 = closer) / Ctrl+Alt+Minus (+1 = farther).</summary>
    public event Action<int>? DistanceStepRequested;

    /// <summary>Hotkeys that could not be registered (already taken by another program).</summary>
    public IReadOnlyList<string> UnavailableHotkeys => _unavailableHotkeys;

    private readonly List<string> _unavailableHotkeys = [];


    /// <summary>Completes when the window closes (Esc, <see cref="Stop"/> or error).</summary>
    public Task Completion => _stopped.Task;

    /// <summary>Call before querying monitor coordinates so they are physical pixels.</summary>
    public static void EnablePerMonitorDpi() => Win32.EnablePerMonitorDpi();

    public void Start()
    {
        if (Interlocked.CompareExchange(ref s_current, this, null) is not null)
        {
            throw new InvalidOperationException("Only one GlassesPresenter can run at a time.");
        }

        Win32.EnablePerMonitorDpi();
        var ready = new ManualResetEventSlim();
        _windowThread = new Thread(() => WindowThread(ready)) { IsBackground = true, Name = "XrealScreen window" };
        _windowThread.Start();
        ready.Wait();
        if (_hwnd == IntPtr.Zero)
        {
            s_current = null;
            throw new InvalidOperationException("Could not create the presenter window.");
        }

        _renderThread = new Thread(RenderThread) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "XrealScreen render" };
        _renderThread.Start();
    }

    public void Stop()
    {
        _stop = true;
        if (_hwnd != IntPtr.Zero)
        {
            Win32.PostMessage(_hwnd, Win32.WM_CLOSE, 0, 0);
        }
    }

    public PresenterStats GetStats()
    {
        double[] sorted;
        lock (_frameMs)
        {
            sorted = [.. _frameMs.Order()];
        }

        double[] scanout;
        lock (_frameMs)
        {
            scanout = [.. _scanoutMs.Order()];
        }

        double P(double q) => sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
        double S(double q) => scanout.Length == 0 ? double.NaN : scanout[Math.Min(scanout.Length - 1, (int)(q * scanout.Length))];
        long frames = Interlocked.Read(ref _frames);
        return new PresenterStats(frames, _seconds, P(0.5), P(0.99), frames > 0 ? _latchSumMs / frames : 0, Interlocked.Read(ref _missed), _output, _presentPath, S(0.5), S(0.99));
    }

    private void WindowThread(ManualResetEventSlim ready)
    {
        fixed (char* className = "XrealScreenPresenter")
        {
            var wc = new Win32.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Win32.WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = Win32.GetModuleHandle(IntPtr.Zero),
                lpszClassName = className,
            };
            Win32.RegisterClassEx(&wc); // returns 0 if already registered — fine
        }

        _hwnd = Win32.CreateWindowEx(Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST, "XrealScreenPresenter", "XrealScreen glasses output", Win32.WS_POPUP | Win32.WS_VISIBLE,
            _x, _y, _width, _height, IntPtr.Zero, IntPtr.Zero, Win32.GetModuleHandle(IntPtr.Zero), IntPtr.Zero);
        ready.Set();
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        // Global hotkeys: the glasses window usually does not have focus while the user works.
        const uint ctrlAlt = Win32.MOD_CONTROL | Win32.MOD_ALT;
        Register(Win32.HotkeyRecenter, ctrlAlt | Win32.MOD_NOREPEAT, Win32.VK_R, "Ctrl+Alt+R");
        Register(Win32.HotkeyStop, ctrlAlt | Win32.MOD_NOREPEAT, Win32.VK_Q, "Ctrl+Alt+Q");
        Register(Win32.HotkeyCloser, ctrlAlt, Win32.VK_OEM_PLUS, "Ctrl+Alt+Plus");
        Register(Win32.HotkeyFarther, ctrlAlt, Win32.VK_OEM_MINUS, "Ctrl+Alt+Minus");
        Register(Win32.HotkeyCloserNumpad, ctrlAlt, Win32.VK_ADD, "Ctrl+Alt+Numpad Plus");
        Register(Win32.HotkeyFartherNumpad, ctrlAlt, Win32.VK_SUBTRACT, "Ctrl+Alt+Numpad Minus");

        Win32.MSG msg;
        while (Win32.GetMessage(&msg, IntPtr.Zero, 0, 0) > 0)
        {
            _ = Win32.TranslateMessage(&msg);
            Win32.DispatchMessage(&msg);
        }

        _stop = true;
        _renderThread?.Join();
        s_current = null;
        if (_renderError is not null)
        {
            _stopped.TrySetException(_renderError);
        }
        else
        {
            _stopped.TrySetResult();
        }
    }

    private void Register(int id, uint modifiers, int vk, string name)
    {
        if (!Win32.RegisterHotKey(_hwnd, id, modifiers, (uint)vk))
        {
            _unavailableHotkeys.Add(name);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var self = s_current;
        switch (msg)
        {
            case Win32.WM_KEYDOWN when wParam == Win32.VK_ESCAPE:
                Win32.PostMessage(hwnd, Win32.WM_CLOSE, 0, 0);
                return 0;
            case Win32.WM_KEYDOWN when wParam == Win32.VK_R:
            case Win32.WM_HOTKEY when wParam == Win32.HotkeyRecenter:
                self?.RecenterRequested?.Invoke();
                return 0;
            case Win32.WM_HOTKEY when wParam == Win32.HotkeyCloser || wParam == Win32.HotkeyCloserNumpad:
                self?.DistanceStepRequested?.Invoke(-1);
                return 0;
            case Win32.WM_HOTKEY when wParam == Win32.HotkeyFarther || wParam == Win32.HotkeyFartherNumpad:
                self?.DistanceStepRequested?.Invoke(+1);
                return 0;
            case Win32.WM_HOTKEY when wParam == Win32.HotkeyStop:
                Win32.PostMessage(hwnd, Win32.WM_CLOSE, 0, 0);
                return 0;
            case Win32.WM_SETCURSOR when (lParam & 0xFFFF) == Win32.HTCLIENT:
                // A pointer over the glasses output would be head-locked; hide it (the pointer is shown,
                // world-locked, inside the captured virtual monitors instead).
                Win32.SetCursor(IntPtr.Zero);
                return 1;
            case Win32.WM_CLOSE:
                if (self is not null)
                {
                    self._stop = true;
                    self._renderThread?.Join(); // release the swapchain before the window goes away
                }

                Win32.UnregisterHotKey(hwnd, Win32.HotkeyRecenter);
                Win32.UnregisterHotKey(hwnd, Win32.HotkeyStop);
                for (int id = Win32.HotkeyRecenter; id <= Win32.HotkeyFartherNumpad; id++)
                {
                    Win32.UnregisterHotKey(hwnd, id);
                }
                Win32.DestroyWindow(hwnd);
                return 0;
            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                return 0;
            default:
                return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }

    private void RenderThread()
    {
        try
        {
            using var dxgiDevice = _gd.Device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var factory = adapter.GetParent<IDXGIFactory2>();
            var desc = new SwapChainDescription1
            {
                Width = (uint)_width,
                Height = (uint)_height,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SwapEffect = SwapEffect.FlipDiscard,
                SampleDescription = new SampleDescription(1, 0),
                Scaling = Scaling.None,
                AlphaMode = AlphaMode.Ignore,
            };
            using var swapChain1 = factory.CreateSwapChainForHwnd(_gd.Device, _hwnd, desc);
            factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAltEnter);
            using var swapChain = swapChain1.QueryInterface<IDXGISwapChain2>();
            using var device1 = _gd.Device.QueryInterface<IDXGIDevice1>();
            device1.MaximumFrameLatency = 1;

            // Pacing ([verified-hw] 2026-09-26, ADR-0009): the window is composed by DWM, whose clock
            // follows the primary monitor (144 Hz here), so Present(1)/waitable pacing ran at 144 fps.
            // Waiting on the glasses output's own vblank gives exactly one frame per glasses refresh.
            using var glassesOutput = swapChain.GetContainingOutput();
            _output = glassesOutput.Description.DeviceName;

            using var backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
            using var rtv = _gd.Device.CreateRenderTargetView(backBuffer);

            var total = Stopwatch.StartNew();
            long last = Stopwatch.GetTimestamp();
            double expectedMs = 0;
            IntPtr timer = LatchLeadMs > 0 ? Win32.CreateHighResolutionTimer() : IntPtr.Zero;

            // Latency measurement: DXGI frame statistics tell at which vblank (QPC) a present count reached the
            // screen, and whether DWM composed it or the swapchain was flipped/overlaid directly.
            using var media = swapChain.QueryInterfaceOrNull<IDXGISwapChainMedia>();
            var latchByPresent = new long[256];
            uint lastDisplayed = 0;
            var modes = new Dictionary<FramePresentationMode, long>();
            while (!_stop)
            {
                glassesOutput.WaitForVBlank();
                long vblank = Stopwatch.GetTimestamp();
                if (LatchLeadMs > 0 && expectedMs > 0)
                {
                    // High-resolution timer to ~0.7 ms before the latch point, then spin the rest.
                    double targetMs = expectedMs - LatchLeadMs;
                    double sleepMs = targetMs - 0.7;
                    if (sleepMs > 0)
                    {
                        Win32.SleepPrecise(timer, sleepMs);
                    }

                    while (Stopwatch.GetElapsedTime(vblank).TotalMilliseconds < targetMs)
                    {
                        Thread.SpinWait(50);
                    }
                }

                long latch = Stopwatch.GetTimestamp();
                HeadView head = _latchPose(); // late latch
                if (_scene is IHudPoseSink sink)
                {
                    sink.SetPose(head);
                }

                var viewProjection = Flat ? Matrix4x4.Identity : ViewMath.ViewProjection(head, _optics);
                _scene.Render(rtv, _width, _height, viewProjection);
                swapChain.Present(0, PresentFlags.None);
                long now = Stopwatch.GetTimestamp();
                latchByPresent[swapChain.LastPresentCount & 255] = latch;
                if (swapChain.GetFrameStatistics(out var fs).Success && fs.PresentCount != lastDisplayed && fs.PresentCount != 0)
                {
                    lastDisplayed = fs.PresentCount;
                    long latched = latchByPresent[fs.PresentCount & 255];
                    if (latched != 0 && fs.SyncQPCTime > latched)
                    {
                        lock (_frameMs)
                        {
                            _scanoutMs.Add((fs.SyncQPCTime - latched) * 1000.0 / Stopwatch.Frequency);
                        }
                    }
                }

                if (media is not null && (_frames & 31) == 0)
                {
                    try
                    {
                        var mode = media.FrameStatisticsMedia.CompositionMode;
                        modes[mode] = modes.GetValueOrDefault(mode) + 1;
                    }
                    catch (SharpGen.Runtime.SharpGenException)
                    {
                        // DXGI_ERROR_FRAME_STATISTICS_DISJOINT etc. — skip this sample.
                    }
                }

                double frameMs = Stopwatch.GetElapsedTime(last, now).TotalMilliseconds;
                last = now;
                _latchSumMs += Stopwatch.GetElapsedTime(latch, now).TotalMilliseconds;
                long frame = Interlocked.Increment(ref _frames);
                if (frame > 10)
                {
                    lock (_frameMs)
                    {
                        _frameMs.Add(frameMs);
                    }

                    expectedMs = expectedMs == 0 ? frameMs : expectedMs * 0.99 + frameMs * 0.01;
                    if (frameMs > expectedMs * 1.5)
                    {
                        Interlocked.Increment(ref _missed);
                    }
                }
            }

            _seconds = total.Elapsed.TotalSeconds;
            if (timer != IntPtr.Zero)
            {
                Win32.CloseHandle(timer);
            }
            // DXGI_FRAME_PRESENTATION_MODE: Composed = DWM, Overlay = hardware overlay (MPO), None = independent flip.
            _presentPath = modes.Count == 0 ? "unknown (no media stats)" : string.Join(" ", modes.Select(m => $"{m.Key}×{m.Value}"));

            _gd.Context.ClearState();
            _gd.Context.Flush();
        }
        catch (Exception ex)
        {
            // Any failure (device removed, display re-enumerated, ...) ends the session instead of the process.
            _renderError = ex;
            Win32.PostMessage(_hwnd, Win32.WM_CLOSE, 0, 0);
        }
    }

    public void Dispose()
    {
        Stop();
        _windowThread?.Join(TimeSpan.FromSeconds(5));
    }
}

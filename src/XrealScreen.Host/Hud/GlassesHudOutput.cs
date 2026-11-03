using XrealScreen.Core.Hud;
using XrealScreen.Display;
using XrealScreen.Render;
using XrealScreen.Render.Capture;
using XrealScreen.Render.Presenter;
using XrealScreen.Render.Scene;

namespace XrealScreen.Host.Hud;

/// <summary>Fullscreen clear HUD on the glasses monitor. Does not create virtual monitors or capture the desktop.</summary>
public sealed class GlassesHudOutput : IHudOutput
{
    private readonly Func<HudStyle> _style;
    private GraphicsDevice? _device;
    private HudScene? _scene;
    private GlassesPresenter? _presenter;

    public GlassesHudOutput(Func<HudStyle> style) => _style = style ?? throw new ArgumentNullException(nameof(style));

    public Task Completion => _presenter?.Completion ?? Task.CompletedTask;

    public event Action? RecenterRequested;

    public event Action<int>? PinSelected;

    public void Start(Func<HeadView> latchPose)
    {
        ArgumentNullException.ThrowIfNull(latchPose);
        var glasses = GlassesDisplayLocator.FindGlasses(new CcdDisplayTopology().GetActiveMonitors())
            ?? throw new InvalidOperationException("Glasses display not found. Plug in the XREAL glasses and set UltraWide to Off.");

        _device = GraphicsDevice.CreateForMonitor(MonitorCapture.MonitorAt(glasses.X, glasses.Y));
        _scene = new HudScene(_device, _style);
        _scene.PinSelected += OnPin;
        _presenter = new GlassesPresenter(_device, _scene, GlassesOptics.Xreal1S, latchPose, glasses.X, glasses.Y, glasses.Resolution.Width, glasses.Resolution.Height)
        {
            LatchLeadMs = 20,
        };
        _presenter.RecenterRequested += OnRecenter;
        _presenter.Start();
    }

    public void RequestStop() => _presenter?.Stop();

    public void Dispose()
    {
        if (_scene is not null)
        {
            _scene.PinSelected -= OnPin;
        }

        if (_presenter is not null)
        {
            _presenter.RecenterRequested -= OnRecenter;
            _presenter.Stop();
            _presenter.Dispose();
            _presenter = null;
        }

        _scene?.Dispose();
        _scene = null;
        _device?.Dispose();
        _device = null;
    }

    private void OnRecenter() => RecenterRequested?.Invoke();

    private void OnPin(int id) => PinSelected?.Invoke(id);
}

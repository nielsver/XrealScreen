using XrealScreen.Render.Scene;

namespace XrealScreen.Host.Hud;

/// <summary>Where the clear HUD is shown. The real one is the glasses; tests supply a stand-in that does not touch the display.</summary>
public interface IHudOutput : IDisposable
{
    void Start(Func<HeadView> latchPose);

    void RequestStop();

    Task Completion { get; }

    event Action? RecenterRequested;
}

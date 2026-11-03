using System.Numerics;
using Vortice.Direct3D11;

namespace XrealScreen.Render.Scene;

/// <summary>Something the glasses presenter draws into the swapchain each vblank.</summary>
public interface IGlassesScene
{
    void Render(ID3D11RenderTargetView target, int width, int height, Matrix4x4 viewProjection);
}

/// <summary>A scene that wants the pose the presenter just latched, in addition to the view matrix.</summary>
public interface IHudPoseSink
{
    void SetPose(HeadView pose);
}

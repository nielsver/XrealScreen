using System.Numerics;
using Vortice.Direct3D11;
using Vortice.DXGI;
using XrealScreen.Core.Workspace;
using XrealScreen.Render.Scene;

namespace XrealScreen.Render.Tests;

/// <summary>Offscreen rendering on WARP (no GPU or glasses needed).</summary>
public sealed class WorkspaceSceneTests : IDisposable
{
    private const int W = 160, H = 100;
    private static readonly uint Red = 0xFFFF0000, Green = 0xFF00FF00, Black = 0xFF000000;

    private readonly GraphicsDevice _gd = GraphicsDevice.Create(warp: true);
    private readonly WorkspaceScene _scene;
    private readonly ID3D11Texture2D _target;
    private readonly ID3D11RenderTargetView _rtv;
    private readonly ID3D11Texture2D _staging;

    public WorkspaceSceneTests()
    {
        _scene = new WorkspaceScene(_gd);
        _target = _gd.Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, W, H, 1, 1, BindFlags.RenderTarget));
        _rtv = _gd.Device.CreateRenderTargetView(_target);
        _staging = _gd.Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, W, H, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
    }

    private static Quaternion Yaw(float deg) => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, deg * MathF.PI / 180f);

    // Nose-up is a negative rotation about +Y (left) in the FLU body frame.
    private static Quaternion LookUp(float deg) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, -deg * MathF.PI / 180f);

    private ScreenSurface AddScreen(float yaw, float pitch, Func<int, int, uint> pixel, int tw = 64, int th = 36)
    {
        var s = _scene.AddScreen(new ScreenPlacement(1, yaw, pitch, 1.2f, 0.675f, 1.5f), tw, th);
        var data = new uint[tw * th];
        for (int y = 0; y < th; y++)
        {
            for (int x = 0; x < tw; x++)
            {
                data[y * tw + x] = pixel(x, y);
            }
        }

        _gd.Context.UpdateSubresource(data, s.Texture, 0, (uint)(tw * 4), 0);
        return s;
    }

    private uint[] Render(Quaternion head)
    {
        _scene.Render(_rtv, W, H, ViewMath.ViewProjection(head, GlassesOptics.Xreal1S));
        _gd.Context.CopyResource(_staging, _target);
        var map = _gd.Context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var pixels = new uint[W * H];
            unsafe
            {
                for (int y = 0; y < H; y++)
                {
                    new ReadOnlySpan<uint>((byte*)map.DataPointer + y * map.RowPitch, W).CopyTo(pixels.AsSpan(y * W, W));
                }
            }

            return pixels;
        }
        finally
        {
            _gd.Context.Unmap(_staging, 0);
        }
    }

    private static uint At(uint[] p, float fx, float fy) => p[(int)(fy * (H - 1)) * W + (int)(fx * (W - 1))];

    [Fact]
    public void ScreenAhead_FillsCenter()
    {
        AddScreen(0, 0, (_, _) => Red);
        Assert.Equal(Red, At(Render(Quaternion.Identity), 0.5f, 0.5f));
    }

    [Fact]
    public void TurningHeadLeft_MovesScreenOutOfView()
    {
        AddScreen(0, 0, (_, _) => Red);
        Assert.Equal(Black, At(Render(Yaw(90)), 0.5f, 0.5f));
    }

    [Fact]
    public void ScreenOnTheLeft_IsSeenWhenLookingLeft()
    {
        AddScreen(60, 0, (_, _) => Red);
        Assert.Equal(Black, At(Render(Quaternion.Identity), 0.5f, 0.5f));
        Assert.Equal(Red, At(Render(Yaw(60)), 0.5f, 0.5f));
    }

    [Fact]
    public void ScreenAbove_IsSeenWhenLookingUp()
    {
        AddScreen(0, 25, (_, _) => Red);
        Assert.Equal(Black, At(Render(Quaternion.Identity), 0.5f, 0.5f));
        Assert.Equal(Red, At(Render(LookUp(25)), 0.5f, 0.5f));
    }

    [Fact]
    public void Texture_IsNotMirrored()
    {
        // Left half red, right half green; top rows red, bottom rows green on the second screen check.
        AddScreen(0, 0, (x, _) => x < 32 ? Red : Green);
        var p = Render(Quaternion.Identity);
        Assert.Equal(Red, At(p, 0.4f, 0.5f));
        Assert.Equal(Green, At(p, 0.6f, 0.5f));
    }

    [Fact]
    public void Texture_IsNotUpsideDown()
    {
        AddScreen(0, 0, (_, y) => y < 18 ? Red : Green);
        var p = Render(Quaternion.Identity);
        Assert.Equal(Red, At(p, 0.5f, 0.4f));
        Assert.Equal(Green, At(p, 0.5f, 0.6f));
    }

    [Fact]
    public void GradientBackdrop_IsALeftToRightGradientAroundTheScreen()
    {
        _scene.GradientBackdrop = true;
        var rect = new PixelRect(40, 20, 80, 60);
        var screen = _scene.AddFlatScreen(rect, W, H, rect.Width, rect.Height);
        _gd.Context.UpdateSubresource(Enumerable.Repeat(Red, rect.Width * rect.Height).ToArray(), screen.Texture, 0, (uint)(rect.Width * 4), 0);

        _scene.Render(_rtv, W, H, Matrix4x4.Identity);
        _gd.Context.CopyResource(_staging, _target);
        var map = _gd.Context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            unsafe
            {
                uint Px(int x, int y) => ((uint*)((byte*)map.DataPointer + y * map.RowPitch))[x];
                uint left = Px(1, H / 2);
                uint right = Px(W - 2, H / 2);
                Assert.Equal(Red, Px(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
                Assert.NotEqual(left, right);
                Assert.True((left & 0xFF) < (right & 0xFF), "the right side of the gradient is more blue");
            }
        }
        finally
        {
            _gd.Context.Unmap(_staging, 0);
        }
    }

    [Fact]
    public void FlatScreens_FillTheirPixelRects()
    {
        var rects = XrealScreen.Core.Workspace.AnchorSplit.Split(new XrealScreen.Core.Workspace.Resolution(W, H), 2);
        uint[] colors = [Red, Green];
        for (int i = 0; i < 2; i++)
        {
            var s = _scene.AddFlatScreen(rects[i], W, H, rects[i].Width, rects[i].Height);
            _gd.Context.UpdateSubresource(Enumerable.Repeat(colors[i], rects[i].Width * rects[i].Height).ToArray(), s.Texture, 0, (uint)(rects[i].Width * 4), 0);
        }

        _scene.Render(_rtv, W, H, Matrix4x4.Identity);
        _gd.Context.CopyResource(_staging, _target);
        var map = _gd.Context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            unsafe
            {
                uint Px(int x, int y) => ((uint*)((byte*)map.DataPointer + y * map.RowPitch))[x];
                Assert.Equal(Red, Px(0, 0));
                Assert.Equal(Red, Px(W / 2 - 1, H - 1));
                Assert.Equal(Green, Px(W / 2, 0));
                Assert.Equal(Green, Px(W - 1, H - 1));
            }
        }
        finally
        {
            _gd.Context.Unmap(_staging, 0);
        }
    }

    public void Dispose()
    {
        _staging.Dispose();
        _rtv.Dispose();
        _target.Dispose();
        _scene.Dispose();
        _gd.Dispose();
    }
}

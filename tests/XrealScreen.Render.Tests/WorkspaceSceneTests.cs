using System.Numerics;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
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
    public void VerticalBackdrop_ChangesFromTopToBottom()
    {
        _scene.GradientBackdrop = true;
        _scene.BackdropStyle = 1;
        var pixels = Pixels();
        uint top = pixels[2 + 2 * W];
        uint bottom = pixels[2 + (H - 3) * W];
        Assert.True((top & 0xFF) < (bottom & 0xFF), "the bottom of a vertical gradient is more blue");
    }

    [Fact]
    public void SolidBackdrop_UsesTheLeftColorOnBothSides()
    {
        _scene.GradientBackdrop = true;
        _scene.BackdropStyle = 2;
        var pixels = Pixels();
        Assert.Equal(pixels[1 + (H / 2) * W], pixels[(W - 2) + (H / 2) * W]);
    }

    [Fact]
    public void GlowBackdrop_IsStrongerAtTheEdgeThanNearTheMiddle()
    {
        _scene.GradientBackdrop = true;
        _scene.BackdropStyle = 4;
        var pixels = Pixels();
        uint edge = pixels[1 + (H / 2) * W];
        uint inner = pixels[30 + (H / 2) * W];
        Assert.True((edge & 0xFF) > (inner & 0xFF), "the rim is brighter than a point closer to the middle");
    }

    [Fact]
    public void EachScreen_KeepsItsOwnColor()
    {
        _scene.GradientBackdrop = true;
        _scene.BackdropStyle = 2;
        _scene.BackdropScreenCount = 2;
        _scene.BackdropSplit1 = 0.5f;
        _scene.GradientLeft = new Color4(1f, 0f, 0f, 1f);
        _scene.GradientRight = new Color4(1f, 0f, 0f, 1f);
        _scene.BackdropColorA1 = new Color4(0f, 0f, 1f, 1f);
        _scene.BackdropColorB1 = new Color4(0f, 0f, 1f, 1f);
        var pixels = Pixels();
        uint left = pixels[4 + (H / 2) * W];
        uint right = pixels[(W - 5) + (H / 2) * W];
        Assert.True(((left >> 16) & 0xFF) > (left & 0xFF), "screen 1 is red");
        Assert.True((right & 0xFF) > ((right >> 16) & 0xFF), "screen 2 is blue");
    }

    [Fact]
    public void FlatScreen_FloatsWithRoundedCornersAndAShadow()
    {
        _scene.GradientBackdrop = true;
        _scene.BackdropStyle = 2;
        _scene.GradientLeft = new Color4(1f, 1f, 1f, 1f);
        _scene.GradientRight = new Color4(1f, 1f, 1f, 1f);
        var rect = new PixelRect(40, 20, 80, 60);
        var screen = _scene.AddFlatScreen(rect, W, H, rect.Width, rect.Height);
        _gd.Context.UpdateSubresource(Enumerable.Repeat(Red, rect.Width * rect.Height).ToArray(), screen.Texture, 0, (uint)(rect.Width * 4), 0);

        var pixels = Pixels();
        uint Px(int x, int y) => pixels[x + y * W];
        Assert.Equal(Red, Px(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
        Assert.NotEqual(Red, Px(rect.X, rect.Y));
        uint outside = Px(2, 2);
        uint beside = Px(rect.X - 8, rect.Y + rect.Height / 2);
        int Lum(uint p) => (int)((p >> 16) & 0xFF) + (int)((p >> 8) & 0xFF) + (int)(p & 0xFF);
        Assert.True(Lum(beside) < Lum(outside), "the shadow beside the screen is darker than the open frame");
    }

    private uint[] Pixels()
    {
        _scene.Render(_rtv, W, H, Matrix4x4.Identity);
        _gd.Context.CopyResource(_staging, _target);
        var map = _gd.Context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var pixels = new uint[W * H];
            unsafe
            {
                for (int y = 0; y < H; y++)
                {
                    var row = new Span<uint>((byte*)map.DataPointer + y * map.RowPitch, W);
                    row.CopyTo(pixels.AsSpan(y * W, W));
                }
            }

            return pixels;
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
                Assert.Equal(Red, Px(rects[0].X + rects[0].Width / 2, H / 2));
                Assert.Equal(Green, Px(rects[1].X + rects[1].Width / 2, H / 2));
                Assert.Equal(Red, Px(rects[0].X, H / 2));
                Assert.NotEqual(Red, Px(rects[0].X, rects[0].Y));
                Assert.NotEqual(Green, Px(rects[1].X + rects[1].Width - 1, rects[1].Y));
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

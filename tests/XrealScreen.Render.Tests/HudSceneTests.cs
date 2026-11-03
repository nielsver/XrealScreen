using System.Numerics;
using Vortice.Direct3D11;
using Vortice.DXGI;
using XrealScreen.Core.Hud;
using XrealScreen.Render.Scene;

namespace XrealScreen.Render.Tests;

/// <summary>The clear HUD draws strokes on black and leaves the rest of the frame empty.</summary>
public sealed class HudSceneTests : IDisposable
{
    private const int W = 160;
    private const int H = 100;

    private readonly GraphicsDevice _gd = GraphicsDevice.Create(warp: true);
    private readonly HudScene _scene;
    private HudStyle _style = new() { Brightness = 1f, ShowClock = false };
    private readonly ID3D11Texture2D _target;
    private readonly ID3D11RenderTargetView _rtv;
    private readonly ID3D11Texture2D _staging;

    public HudSceneTests()
    {
        _scene = new HudScene(_gd, () => _style);
        _target = _gd.Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, W, H, 1, 1, BindFlags.RenderTarget));
        _rtv = _gd.Device.CreateRenderTargetView(_target);
        _staging = _gd.Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, W, H, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
    }

    [Fact]
    public void LevelPose_PaintsTheReticleAndLeavesTheCornerBlack()
    {
        _scene.SetPose(new HeadView(Quaternion.Identity, Vector3.Zero));
        uint[] pixels = Render();

        Assert.Equal(0xFF000000, pixels[1 * W + 1]);
        Assert.True(LitNear(pixels, NdcToX(-0.04f), NdcToY(0f)));
    }

    [Fact]
    public void Caption_PaintsACardUnderTheReticleAndLeavesTheCornerBlack()
    {
        var bgra = new byte[40 * 20 * 4];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = 16;
            bgra[i + 1] = 200;
            bgra[i + 2] = 220;
            bgra[i + 3] = 255;
        }

        _style = _style with { Caption = new HudCaptionPicture(1, 40, 20, bgra) };
        _scene.SetPose(new HeadView(Quaternion.Identity, Vector3.Zero));
        uint[] pixels = Render();

        Assert.Equal(0xFF000000, pixels[1 * W + 1]);
        uint card = pixels[(NdcToY(-0.42f) * W) + NdcToX(0f)];
        Assert.True((byte)(card >> 8) > 100, "The card should be visible under the reticle.");
    }

    [Fact]
    public void Screen_FillsAMatchingFrameAndTheCaptionStaysOnTop()
    {
        var screen = new byte[W * H * 4];
        for (int i = 0; i < screen.Length; i += 4)
        {
            screen[i] = 10;
            screen[i + 1] = 40;
            screen[i + 2] = 200;
            screen[i + 3] = 255;
        }

        var card = new byte[40 * 20 * 4];
        for (int i = 0; i < card.Length; i += 4)
        {
            card[i] = 16;
            card[i + 1] = 200;
            card[i + 2] = 220;
            card[i + 3] = 255;
        }

        _style = _style with
        {
            Screen = new HudCaptionPicture(1, W, H, screen),
            Caption = new HudCaptionPicture(1, 40, 20, card),
        };
        _scene.SetPose(new HeadView(Quaternion.Identity, Vector3.Zero));
        uint[] pixels = Render();

        uint corner = pixels[1 * W + 1];
        Assert.True((byte)(corner >> 16) > 150, "The monitor frame should reach the corner.");
        Assert.True((byte)(corner >> 8) < 80, "The corner should be the monitor, not the card.");
        uint note = pixels[(NdcToY(-0.42f) * W) + NdcToX(0f)];
        Assert.True((byte)(note >> 8) > 100, "The suggestion card should stay on top of the monitor.");
    }

    [Fact]
    public void PlacedImage_SitsWhereItWasAskedAndLeavesTheCornerBlack()
    {
        var bgra = new byte[20 * 20 * 4];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = 220;
            bgra[i + 1] = 30;
            bgra[i + 2] = 30;
            bgra[i + 3] = 255;
        }

        _style = _style with
        {
            Pictures =
            [
                new HudPlacedPicture(1, new HudCaptionPicture(1, 20, 20, bgra), 0.5f, 0f, 0.4f),
            ],
        };
        _scene.SetPose(new HeadView(Quaternion.Identity, Vector3.Zero));
        uint[] pixels = Render();

        Assert.Equal(0xFF000000, pixels[1 * W + 1]);
        uint placed = pixels[(NdcToY(0f) * W) + NdcToX(0.5f)];
        Assert.True((byte)placed > 150, "The image should sit to the right of center.");
    }

    public void Dispose()
    {
        _rtv.Dispose();
        _target.Dispose();
        _staging.Dispose();
        _scene.Dispose();
        _gd.Dispose();
    }

    private uint[] Render()
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
                    new ReadOnlySpan<uint>((byte*)map.DataPointer + (y * map.RowPitch), W).CopyTo(pixels.AsSpan(y * W, W));
                }
            }

            return pixels;
        }
        finally
        {
            _gd.Context.Unmap(_staging, 0);
        }
    }

    private static int NdcToX(float ndcX) => (int)((ndcX * 0.5f + 0.5f) * W);

    private static int NdcToY(float ndcY) => (int)((0.5f - (ndcY * 0.5f)) * H);

    private static bool LitNear(uint[] pixels, int x, int y)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int px = x + dx;
                int py = y + dy;
                if ((uint)px >= W || (uint)py >= H)
                {
                    continue;
                }

                uint pixel = pixels[(py * W) + px];
                byte b = (byte)pixel;
                byte g = (byte)(pixel >> 8);
                if (g > 80 && b > 80)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

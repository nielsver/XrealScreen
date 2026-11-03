using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using XrealScreen.Core.Hud;
using XrealScreen.Core.Tracking;

namespace XrealScreen.Render.Scene;

/// <summary>
/// Clears the glasses to black (optically clear on the micro-OLED) and strokes a <see cref="HudLayout"/> frame.
/// Head-locked: the pose is baked into the layout, so the view matrix is ignored.
/// </summary>
public sealed class HudScene : IDisposable, IGlassesScene, IHudPoseSink
{
    private const int MaxVertices = 16384;

    private const string Shader = """
        struct VsIn { float2 pos : POSITION; float4 col : COLOR0; };
        struct PsIn { float4 pos : SV_Position; float4 col : COLOR0; };
        PsIn VS(VsIn i) { PsIn o; o.pos = float4(i.pos, 0, 1); o.col = i.col; return o; }
        float4 PS(PsIn i) : SV_Target { return i.col; }
        """;

    private readonly GraphicsDevice _gd;
    private readonly Func<HudStyle> _style;
    private readonly Func<string?> _clock;
    private readonly ID3D11VertexShader _vs;
    private readonly ID3D11PixelShader _ps;
    private readonly ID3D11InputLayout _layout;
    private readonly ID3D11Buffer _vertices;
    private readonly ID3D11RasterizerState _rasterizer;
    private readonly HudVertex[] _cpu = new HudVertex[MaxVertices];
    private readonly HudAim _aim = new();
    private float _yaw;
    private float _pitch;
    private float _roll;
    private long _lastTicks;

    /// <summary>Raised on the render thread when a glance pin has been looked at long enough.</summary>
    public event Action<int>? PinSelected;

    public HudScene(GraphicsDevice graphics, Func<HudStyle>? style = null, Func<string?>? clock = null)
    {
        _gd = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _style = style ?? (() => new HudStyle());
        _clock = clock ?? (() => DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        ReadOnlyMemory<byte> vsCode = Compiler.Compile(Shader, "VS", "hud.hlsl", "vs_5_0");
        ReadOnlyMemory<byte> psCode = Compiler.Compile(Shader, "PS", "hud.hlsl", "ps_5_0");
        _vs = graphics.Device.CreateVertexShader(vsCode.Span);
        _ps = graphics.Device.CreatePixelShader(psCode.Span);
        _layout = graphics.Device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 8, 0),
        ], vsCode.Span);
        _vertices = graphics.Device.CreateBuffer(new BufferDescription((uint)(MaxVertices * Marshal.SizeOf<HudVertex>()), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        _rasterizer = graphics.Device.CreateRasterizerState(RasterizerDescription.CullNone);
    }

    public void SetPose(HeadView pose)
    {
        Quaternion q = QuaternionMath.IsValidRotation(pose.Orientation) ? pose.Orientation : Quaternion.Identity;
        _yaw = QuaternionMath.Yaw(q) * 180f / MathF.PI;
        _pitch = -QuaternionMath.Pitch(q) * 180f / MathF.PI;
        _roll = QuaternionMath.Roll(q) * 180f / MathF.PI;
    }

    public void Render(ID3D11RenderTargetView target, int width, int height, Matrix4x4 viewProjection)
    {
        ArgumentNullException.ThrowIfNull(target);
        _ = viewProjection;
        HudStyle style = _style();
        float brightness = Math.Clamp(style.Brightness, 0f, 1f);
        HudFrame frame = HudLayout.Build(_yaw, _pitch, _roll, _clock(), style.ToLayout(), GlancePins(style));
        int count = Fill(frame, width, height, brightness);

        var ctx = _gd.Context;
        ctx.OMSetRenderTargets(target);
        ctx.ClearRenderTargetView(target, new Color4(0f, 0f, 0f, 1f));
        ctx.RSSetViewport(new Viewport(width, height));
        ctx.RSSetState(_rasterizer);
        if (count == 0)
        {
            return;
        }

        var mapped = ctx.Map(_vertices, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        try
        {
            unsafe
            {
                _cpu.AsSpan(0, count).CopyTo(new Span<HudVertex>(mapped.DataPointer.ToPointer(), count));
            }
        }
        finally
        {
            ctx.Unmap(_vertices, 0);
        }

        ctx.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
        ctx.IASetInputLayout(_layout);
        ctx.IASetVertexBuffer(0, _vertices, (uint)Marshal.SizeOf<HudVertex>());
        ctx.VSSetShader(_vs);
        ctx.PSSetShader(_ps);
        ctx.Draw((uint)count, 0);
    }

    public void Dispose()
    {
        _rasterizer.Dispose();
        _vertices.Dispose();
        _layout.Dispose();
        _ps.Dispose();
        _vs.Dispose();
    }

    private HudPin[]? GlancePins(HudStyle style)
    {
        if (!style.ShowGlance)
        {
            _lastTicks = 0;
            return null;
        }

        HudPin[] pins = HudGlance.Create(style.GlanceUp, style.GlanceLeft, style.GlanceRight, style.GlanceDown);
        float scale = style.ToLayout().DegreesToNdc;
        int aimed = HudLayout.NearestPin(_yaw, _pitch, _roll, pins, scale, HudGlance.AimRadius);
        long now = Stopwatch.GetTimestamp();
        TimeSpan step = _lastTicks == 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((now - _lastTicks) / (double)Stopwatch.Frequency);
        _lastTicks = now;
        int fired = _aim.Advance(aimed, step, HudGlance.Hold);
        if (fired >= 0)
        {
            PinSelected?.Invoke(fired);
        }

        for (int i = 0; i < pins.Length; i++)
        {
            pins[i] = pins[i] with { Dwell = _aim.Dwell(pins[i].Id, HudGlance.Hold) };
        }

        return pins;
    }

    private int Fill(HudFrame frame, int width, int height, float brightness)
    {
        var color = new Color4(0f, 0.86f * brightness, brightness, 1f);
        int count = 0;
        float pixelX = 2f / Math.Max(width, 1);
        float pixelY = 2f / Math.Max(height, 1);

        foreach (HudSegment line in frame.Lines)
        {
            count = AddStroke(count, line.X0, line.Y0, line.X1, line.Y1, pixelX, pixelY, color);
        }

        const float glyphHeight = 0.05f;
        float cellY = glyphHeight / HudFont.Rows;
        float cellX = cellY * height / Math.Max(width, 1);
        float advance = cellX * (HudFont.Columns + 1);
        foreach (HudText label in frame.Labels)
        {
            float runWidth = label.Text.Length * advance;
            float left = label.X - (runWidth / 2f);
            float bottom = label.Y - (glyphHeight / 2f);
            for (int i = 0; i < label.Text.Length; i++)
            {
                for (int row = 0; row < HudFont.Rows; row++)
                {
                    for (int column = 0; column < HudFont.Columns; column++)
                    {
                        if (!HudFont.Ink(label.Text[i], column, row))
                        {
                            continue;
                        }

                        float x = left + (i * advance) + (column * cellX);
                        float y = bottom + ((HudFont.Rows - 1 - row) * cellY);
                        count = AddQuad(count, x, y, x + cellX, y, x + cellX, y + cellY, x, y + cellY, color);
                    }
                }
            }
        }

        return count;
    }

    private int AddStroke(int count, float x0, float y0, float x1, float y1, float pixelX, float pixelY, Color4 color)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-6f)
        {
            return count;
        }

        float nx = -dy / length * pixelX * 1.1f;
        float ny = dx / length * pixelY * 1.1f;
        return AddQuad(count, x0 + nx, y0 + ny, x0 - nx, y0 - ny, x1 - nx, y1 - ny, x1 + nx, y1 + ny, color);
    }

    private int AddQuad(int count, float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, Color4 color)
    {
        if (count + 6 > MaxVertices)
        {
            return count;
        }

        _cpu[count++] = Vert(x0, y0, color);
        _cpu[count++] = Vert(x1, y1, color);
        _cpu[count++] = Vert(x2, y2, color);
        _cpu[count++] = Vert(x0, y0, color);
        _cpu[count++] = Vert(x2, y2, color);
        _cpu[count++] = Vert(x3, y3, color);
        return count;
    }

    private static HudVertex Vert(float x, float y, Color4 color) => new()
    {
        X = x,
        Y = y,
        R = color.R,
        G = color.G,
        B = color.B,
        A = color.A,
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct HudVertex
    {
        public float X;
        public float Y;
        public float R;
        public float G;
        public float B;
        public float A;
    }
}

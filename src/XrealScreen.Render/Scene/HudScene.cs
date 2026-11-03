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
/// Draws the main monitor, then a head-locked <see cref="HudLayout"/> frame and an optional note.
/// Black pixels stay optically clear. Head-locked: the pose is baked into the layout, so the view matrix is ignored.
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

    private const string CaptionShader = """
        struct VsIn { float2 pos : POSITION; float2 uv : TEXCOORD0; };
        struct PsIn { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
        PsIn VS(VsIn i) { PsIn o; o.pos = float4(i.pos, 0, 1); o.uv = i.uv; return o; }
        Texture2D Card : register(t0);
        SamplerState Samp : register(s0);
        float4 PS(PsIn i) : SV_Target { return Card.Sample(Samp, i.uv); }
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
    private readonly ID3D11VertexShader _captionVs;
    private readonly ID3D11PixelShader _captionPs;
    private readonly ID3D11InputLayout _captionLayout;
    private readonly ID3D11Buffer _captionVertices;
    private readonly ID3D11SamplerState _captionSampler;
    private readonly PictureSlot _screenPicture = new();
    private readonly PictureSlot _captionPicture = new();
    private readonly Dictionary<int, PictureSlot> _placed = [];
    private readonly HashSet<int> _placedIds = [];
    private readonly List<int> _stalePlaced = [];
    private readonly ID3D11BlendState _imageBlend;
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
        var blend = new BlendDescription();
        blend.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.SourceAlpha,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.InverseSourceAlpha,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        _imageBlend = graphics.Device.CreateBlendState(blend);
        ReadOnlyMemory<byte> captionVs = Compiler.Compile(CaptionShader, "VS", "hud-card.hlsl", "vs_5_0");
        ReadOnlyMemory<byte> captionPs = Compiler.Compile(CaptionShader, "PS", "hud-card.hlsl", "ps_5_0");
        _captionVs = graphics.Device.CreateVertexShader(captionVs.Span);
        _captionPs = graphics.Device.CreatePixelShader(captionPs.Span);
        _captionLayout = graphics.Device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
        ], captionVs.Span);
        _captionVertices = graphics.Device.CreateBuffer(new BufferDescription(6 * 16, BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        _captionSampler = graphics.Device.CreateSamplerState(SamplerDescription.LinearClamp);
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
        if (style.Screen is { } screen)
        {
            DrawScreen(ctx, screen, width, height);
        }

        if (count > 0)
        {
            DrawLines(ctx, count);
        }

        DrawPlaced(ctx, style.Pictures, width, height);

        if (style.Caption is { } caption)
        {
            DrawCaption(ctx, caption, width, height);
        }
    }

    private void DrawLines(ID3D11DeviceContext ctx, int count)
    {

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

    private void DrawScreen(ID3D11DeviceContext ctx, HudCaptionPicture picture, int frameWidth, int frameHeight)
    {
        (float left, float right, float top, float bottom) = HudPicturePlacement.Contain(
            picture.Width, picture.Height, frameWidth, frameHeight);
        Span<float> vertices = stackalloc float[24];
        WriteQuad(vertices, left, right, top, bottom);
        DrawTextured(ctx, picture, _screenPicture, vertices);
    }

    private void DrawPlaced(ID3D11DeviceContext ctx, IReadOnlyList<HudPlacedPicture>? pictures, int frameWidth, int frameHeight)
    {
        _placedIds.Clear();
        if (pictures is { Count: > 0 })
        {
            ctx.OMSetBlendState(_imageBlend);
            Span<float> vertices = stackalloc float[24];
            int drawn = 0;
            foreach (HudPlacedPicture picture in pictures)
            {
                if (drawn == 12)
                {
                    break;
                }

                (float left, float right, float top, float bottom) = HudPicturePlacement.Place(
                    picture.Image.Width, picture.Image.Height, frameWidth, frameHeight, picture.CenterX, picture.CenterY, picture.Size);
                if (right <= left || top <= bottom)
                {
                    continue;
                }

                if (!_placed.TryGetValue(picture.Id, out PictureSlot? slot))
                {
                    slot = new PictureSlot();
                    _placed[picture.Id] = slot;
                }

                WriteQuad(vertices, left, right, top, bottom);
                DrawTextured(ctx, picture.Image, slot, vertices);
                _placedIds.Add(picture.Id);
                drawn++;
            }

            ctx.OMSetBlendState(null);
        }

        _stalePlaced.Clear();
        foreach (int id in _placed.Keys)
        {
            if (!_placedIds.Contains(id))
            {
                _stalePlaced.Add(id);
            }
        }

        foreach (int id in _stalePlaced)
        {
            _placed[id].Dispose();
            _placed.Remove(id);
        }
    }

    private void DrawCaption(ID3D11DeviceContext ctx, HudCaptionPicture picture, int frameWidth, int frameHeight)
    {
        float halfW = picture.Width / (float)Math.Max(frameWidth, 1);
        float halfH = picture.Height / (float)Math.Max(frameHeight, 1);
        float centerY = -0.42f;
        if (centerY - halfH < -0.92f)
        {
            centerY = -0.92f + halfH;
        }

        Span<float> vertices = stackalloc float[24];
        WriteQuad(vertices, -halfW, halfW, centerY + halfH, centerY - halfH);
        DrawTextured(ctx, picture, _captionPicture, vertices);
    }

    private void DrawTextured(ID3D11DeviceContext ctx, HudCaptionPicture picture, PictureSlot slot, ReadOnlySpan<float> vertices)
    {
        if (picture.Revision != slot.Revision || slot.Texture is null || slot.Width != picture.Width || slot.Height != picture.Height)
        {
            Upload(slot, picture);
        }

        if (slot.View is not { } view)
        {
            return;
        }

        var mapped = ctx.Map(_captionVertices, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        try
        {
            unsafe
            {
                vertices.CopyTo(new Span<float>(mapped.DataPointer.ToPointer(), 24));
            }
        }
        finally
        {
            ctx.Unmap(_captionVertices, 0);
        }

        ctx.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
        ctx.IASetInputLayout(_captionLayout);
        ctx.IASetVertexBuffer(0, _captionVertices, 16);
        ctx.VSSetShader(_captionVs);
        ctx.PSSetShader(_captionPs);
        ctx.PSSetShaderResource(0, view);
        ctx.PSSetSampler(0, _captionSampler);
        ctx.Draw(6, 0);
    }

    private void Upload(PictureSlot slot, HudCaptionPicture picture)
    {
        if (slot.Texture is null || slot.Width != picture.Width || slot.Height != picture.Height)
        {
            slot.View?.Dispose();
            slot.Texture?.Dispose();
            slot.Texture = _gd.Device.CreateTexture2D(new Texture2DDescription(
                Format.B8G8R8A8_UNorm,
                (uint)picture.Width,
                (uint)picture.Height,
                1,
                1,
                BindFlags.ShaderResource,
                ResourceUsage.Dynamic,
                CpuAccessFlags.Write));
            slot.View = _gd.Device.CreateShaderResourceView(slot.Texture);
            slot.Width = picture.Width;
            slot.Height = picture.Height;
        }

        if (slot.Texture is not { } texture)
        {
            return;
        }

        var mapped = _gd.Context.Map(texture, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        try
        {
            unsafe
            {
                byte* destination = (byte*)mapped.DataPointer;
                int rowBytes = picture.Width * 4;
                int pitch = checked((int)mapped.RowPitch);
                for (int y = 0; y < picture.Height; y++)
                {
                    Marshal.Copy(picture.Bgra, y * rowBytes, (nint)(destination + (y * pitch)), rowBytes);
                }
            }
        }
        finally
        {
            _gd.Context.Unmap(texture, 0);
        }

        slot.Revision = picture.Revision;
    }

    private static void WriteQuad(Span<float> vertices, float left, float right, float top, float bottom)
    {
        WriteCaptionVertex(vertices, 0, left, top, 0f, 0f);
        WriteCaptionVertex(vertices, 4, right, top, 1f, 0f);
        WriteCaptionVertex(vertices, 8, right, bottom, 1f, 1f);
        WriteCaptionVertex(vertices, 12, left, top, 0f, 0f);
        WriteCaptionVertex(vertices, 16, right, bottom, 1f, 1f);
        WriteCaptionVertex(vertices, 20, left, bottom, 0f, 1f);
    }

    private static void WriteCaptionVertex(Span<float> vertices, int index, float x, float y, float u, float v)
    {
        vertices[index] = x;
        vertices[index + 1] = y;
        vertices[index + 2] = u;
        vertices[index + 3] = v;
    }

    public void Dispose()
    {
        _screenPicture.Dispose();
        _captionPicture.Dispose();
        foreach (PictureSlot slot in _placed.Values)
        {
            slot.Dispose();
        }

        _placed.Clear();
        _imageBlend.Dispose();
        _captionSampler.Dispose();
        _captionVertices.Dispose();
        _captionLayout.Dispose();
        _captionPs.Dispose();
        _captionVs.Dispose();
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

        HudPin[] pins = HudGlance.Create(style.GlanceUp, style.GlanceLeft, style.GlanceRight, style.GlanceDown, style.GlanceRead);
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

    private sealed class PictureSlot : IDisposable
    {
        public ID3D11Texture2D? Texture;
        public ID3D11ShaderResourceView? View;
        public int Revision = -1;
        public int Width;
        public int Height;

        public void Dispose()
        {
            View?.Dispose();
            Texture?.Dispose();
            View = null;
            Texture = null;
        }
    }

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

using System.Numerics;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using XrealScreen.Core.Workspace;

namespace XrealScreen.Render.Scene;

/// <summary>
/// Draws the workspace (one curved textured panel per virtual monitor) into any render target.
/// Window-independent so it can be tested offscreen on WARP.
/// </summary>
public sealed class WorkspaceScene : IDisposable
{
    private const int VertexStride = 5 * sizeof(float);

    private const string Shader = """
        cbuffer Frame : register(b0) { row_major float4x4 ViewProj; };
        struct VsIn { float3 pos : POSITION; float2 uv : TEXCOORD0; };
        struct PsIn { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
        PsIn VS(VsIn i) { PsIn o; o.pos = mul(float4(i.pos, 1), ViewProj); o.uv = i.uv; return o; }
        Texture2D Tex : register(t0);
        SamplerState Samp : register(s0);
        cbuffer Panel : register(b2) { float2 Size; float Radius; float Round; };
        float sdRoundBox(float2 p, float2 b, float r)
        {
            float2 q = abs(p) - b + r;
            return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
        }
        float4 PS(PsIn i) : SV_Target
        {
            if (Round > 0.5)
            {
                float sd = sdRoundBox(i.uv * Size - Size * 0.5, Size * 0.5, Radius);
                if (sd > 0) discard;
            }
            return float4(Tex.Sample(Samp, i.uv).rgb, 1);
        }
        cbuffer Shade : register(b3) { float4 Rect; float Spread; float Drop; float RadiusS; float Strength; };
        float4 PSShadow(PsIn i) : SV_Target
        {
            float2 center = float2(Rect.x + Rect.z * 0.5, Rect.y + Rect.w * 0.5);
            float2 halfSize = float2(Rect.z, Rect.w) * 0.5;
            float panel = sdRoundBox(i.pos.xy - center, halfSize, RadiusS);
            float shade = sdRoundBox(i.pos.xy - (center + float2(0, Drop)), halfSize, RadiusS);
            if (panel <= 0) discard;
            float shadow = saturate(1 - shade / max(Spread, 1));
            shadow = shadow * shadow * Strength;
            float rim = saturate(1 - panel / 3.0);
            float alpha = max(shadow, rim * 0.9);
            if (alpha < 0.02) discard;
            float3 rgb = lerp(float3(0, 0, 0), float3(0.86, 0.92, 1), rim);
            return float4(rgb, alpha);
        }
        cbuffer Backdrop : register(b1) {
            float3 A0; float Style;
            float3 B0; float Count;
            float3 A1; float Split1;
            float3 B1; float Split2;
            float3 A2; float Axis;
            float3 B2; float Pad;
        };
        PsIn VSBackdrop(VsIn i) { PsIn o; o.pos = float4(i.pos.xy, 0, 1); o.uv = i.uv; return o; }
        float4 PSBackdrop(PsIn i) : SV_Target {
            float x = saturate(i.uv.x);
            float y = saturate(i.uv.y);
            float t = Axis > 0.5 ? y : x;
            float other = Axis > 0.5 ? x : y;
            float t0 = 0;
            float t1 = Split1;
            float3 a = A0;
            float3 b = B0;
            if (Count > 1.5 && t >= Split1) { t0 = Split1; t1 = Count > 2.5 ? Split2 : 1; a = A1; b = B1; }
            if (Count > 2.5 && t >= Split2) { t0 = Split2; t1 = 1; a = A2; b = B2; }
            float span = max(t1 - t0, 0.0001);
            float along = saturate((t - t0) / span);
            float3 side = lerp(a, b, along);
            float3 down = lerp(a, b, other);
            float2 p = float2(along * 2 - 1, other * 2 - 1);
            float edge = saturate(dot(p, p));
            float3 vignette = lerp(b, a, edge);
            float3 rim = a * edge;
            float3 color = side;
            if (Style > 0.5) color = down;
            if (Style > 1.5) color = a;
            if (Style > 2.5) color = vignette;
            if (Style > 3.5) color = rim;
            return float4(color, 1);
        }
        """;

    private readonly GraphicsDevice _gd;
    private readonly ID3D11VertexShader _vs;
    private readonly ID3D11PixelShader _ps;
    private readonly ID3D11PixelShader _shadowPs;
    private readonly ID3D11VertexShader _backdropVs;
    private readonly ID3D11PixelShader _backdropPs;
    private readonly ID3D11InputLayout _layout;
    private readonly ID3D11Buffer _frameBuffer;
    private readonly ID3D11Buffer _backdrop;
    private readonly ID3D11Buffer _backdropColors;
    private readonly ID3D11Buffer _panel;
    private readonly ID3D11Buffer _shade;
    private readonly ID3D11BlendState _blend;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11RasterizerState _rasterizer;
    private readonly List<ScreenSurface> _screens = [];

    public WorkspaceScene(GraphicsDevice graphics)
    {
        _gd = graphics ?? throw new ArgumentNullException(nameof(graphics));
        var device = graphics.Device;
        ReadOnlyMemory<byte> vsCode = Compiler.Compile(Shader, "VS", "workspace.hlsl", "vs_5_0");
        ReadOnlyMemory<byte> psCode = Compiler.Compile(Shader, "PS", "workspace.hlsl", "ps_5_0");
        _vs = device.CreateVertexShader(vsCode.Span);
        _ps = device.CreatePixelShader(psCode.Span);
        ReadOnlyMemory<byte> shadowPsCode = Compiler.Compile(Shader, "PSShadow", "workspace.hlsl", "ps_5_0");
        _shadowPs = device.CreatePixelShader(shadowPsCode.Span);
        _panel = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer));
        _shade = device.CreateBuffer(new BufferDescription(32, BindFlags.ConstantBuffer));
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
        _blend = device.CreateBlendState(blend);
        ReadOnlyMemory<byte> backdropVsCode = Compiler.Compile(Shader, "VSBackdrop", "workspace.hlsl", "vs_5_0");
        ReadOnlyMemory<byte> backdropPsCode = Compiler.Compile(Shader, "PSBackdrop", "workspace.hlsl", "ps_5_0");
        _backdropVs = device.CreateVertexShader(backdropVsCode.Span);
        _backdropPs = device.CreatePixelShader(backdropPsCode.Span);
        _backdrop = device.CreateBuffer(BackdropQuad(), BindFlags.VertexBuffer);
        _backdropColors = device.CreateBuffer(new BufferDescription(96, BindFlags.ConstantBuffer));
        _layout = device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 12, 0),
        ], vsCode.Span);
        _frameBuffer = device.CreateBuffer(new BufferDescription(64, BindFlags.ConstantBuffer));
        _sampler = device.CreateSamplerState(SamplerDescription.LinearClamp);
        _rasterizer = device.CreateRasterizerState(RasterizerDescription.CullNone);
    }

    public Color4 ClearColor { get; set; } = new(0f, 0f, 0f, 1f);

    /// <summary>Draws a colored frame behind the screens (glasses-anchor border and seams).</summary>
    public bool GradientBackdrop { get; set; }

    /// <summary>0 linear, 1 vertical, 2 solid, 3 vignette, 4 edge glow.</summary>
    public int BackdropStyle { get; set; }

    /// <summary>How many virtual screens own a color pair. 1 paints the whole image with the first pair.</summary>
    public int BackdropScreenCount { get; set; } = 1;

    /// <summary>0 = screens sit side by side. 1 = screens are stacked.</summary>
    public float BackdropAxis { get; set; }

    /// <summary>Fraction of the image where screen 1's colors stop.</summary>
    public float BackdropSplit1 { get; set; } = 1f;

    /// <summary>Fraction of the image where screen 2's colors stop. 1 when there is no third screen.</summary>
    public float BackdropSplit2 { get; set; } = 1f;

    /// <summary>First frame color, RGB in 0–1. Screen 1's first color. Linear start, solid fill, vignette edge, glow.</summary>
    public Color4 GradientLeft { get; set; } = new(0.12f, 0.04f, 0.42f, 1f);

    /// <summary>Screen 1's second color, RGB in 0–1. Linear end, vignette center.</summary>
    public Color4 GradientRight { get; set; } = new(0f, 0.55f, 0.72f, 1f);

    /// <summary>Screen 2's first color.</summary>
    public Color4 BackdropColorA1 { get; set; } = new(0.12f, 0.04f, 0.42f, 1f);

    /// <summary>Screen 2's second color.</summary>
    public Color4 BackdropColorB1 { get; set; } = new(0f, 0.55f, 0.72f, 1f);

    /// <summary>Screen 3's first color.</summary>
    public Color4 BackdropColorA2 { get; set; } = new(0.12f, 0.04f, 0.42f, 1f);

    /// <summary>Screen 3's second color.</summary>
    public Color4 BackdropColorB2 { get; set; } = new(0f, 0.55f, 0.72f, 1f);

    /// <summary>When set, each frame reads this instead of the properties above.</summary>
    public IBackdropSource? Live { get; set; }

    public IReadOnlyList<ScreenSurface> Screens => _screens;

    /// <summary>Adds a panel at <paramref name="placement"/> showing a texture of the given size.</summary>
    public ScreenSurface AddScreen(ScreenPlacement placement, int textureWidth, int textureHeight)
    {
        var surface = new ScreenSurface(_gd, placement, textureWidth, textureHeight);
        _screens.Add(surface);
        return surface;
    }

    /// <summary>
    /// Adds a flat screen covering a pixel rectangle of the render target (glasses-anchor mode).
    /// Render it with <see cref="System.Numerics.Matrix4x4.Identity"/> as view-projection.
    /// </summary>
    public ScreenSurface AddFlatScreen(PixelRect rect, int targetWidth, int targetHeight, int textureWidth, int textureHeight)
    {
        var surface = new ScreenSurface(_gd, ViewMath.BuildFlatQuad(rect, targetWidth, targetHeight), textureWidth, textureHeight);
        const int spread = 28;
        const int drop = 12;
        int margin = spread + drop;
        int x0 = Math.Max(0, rect.X - margin);
        int y0 = Math.Max(0, rect.Y - margin);
        int x1 = Math.Min(targetWidth, rect.X + rect.Width + margin);
        int y1 = Math.Min(targetHeight, rect.Y + rect.Height + margin);
        var shadow = new PixelRect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
        surface.SetFloat(ViewMath.BuildFlatQuad(shadow, targetWidth, targetHeight), rect, spread, drop, radius: 22);
        _screens.Add(surface);
        return surface;
    }

    private IReadOnlyList<ScreenPlacement>? _pendingPlacements;

    /// <summary>
    /// Moves the panels (e.g. new viewing distance). Thread-safe; applied at the start of the next
    /// <see cref="Render"/> call on the render thread. Placements match screens by index.
    /// </summary>
    public void SetPlacements(IReadOnlyList<ScreenPlacement> placements) => Volatile.Write(ref _pendingPlacements, placements);

    public void Render(ID3D11RenderTargetView target, int width, int height, Matrix4x4 viewProjection)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Interlocked.Exchange(ref _pendingPlacements, null) is { } placements)
        {
            for (int i = 0; i < Math.Min(placements.Count, _screens.Count); i++)
            {
                _screens[i].SetPlacement(placements[i]);
            }
        }

        var ctx = _gd.Context;
        ctx.UpdateSubresource(in viewProjection, _frameBuffer);
        ctx.OMSetRenderTargets(target);
        ctx.ClearRenderTargetView(target, ClearColor);
        ctx.RSSetViewport(new Viewport(width, height));
        ctx.RSSetState(_rasterizer);
        ctx.OMSetBlendState(_blend);
        ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        ctx.IASetInputLayout(_layout);
        ctx.VSSetShader(_vs);
        ctx.VSSetConstantBuffer(0, _frameBuffer);
        ctx.PSSetShader(_ps);
        ctx.PSSetSampler(0, _sampler);

        bool draw = GradientBackdrop;
        int style = BackdropStyle;
        int count = Math.Clamp(BackdropScreenCount, 1, 3);
        float axis = BackdropAxis;
        float split1 = BackdropSplit1;
        float split2 = BackdropSplit2;
        Color4 a0 = GradientLeft;
        Color4 b0 = GradientRight;
        Color4 a1 = BackdropColorA1;
        Color4 b1 = BackdropColorB1;
        Color4 a2 = BackdropColorA2;
        Color4 b2 = BackdropColorB2;
        if (Live is not null)
        {
            Live.Read(out BackdropLook look);
            draw = look.Draw;
            style = look.Style;
            count = Math.Clamp(look.ScreenCount, 1, 3);
            axis = look.Axis;
            split1 = look.Split1;
            split2 = look.Split2;
            a0 = look.A0;
            b0 = look.B0;
            a1 = look.A1;
            b1 = look.B1;
            a2 = look.A2;
            b2 = look.B2;
        }

        if (draw)
        {
            var colors = new BackdropColors(a0, b0, a1, b1, a2, b2, style, count, axis, split1, split2);
            ctx.UpdateSubresource(in colors, _backdropColors);
            ctx.VSSetShader(_backdropVs);
            ctx.PSSetShader(_backdropPs);
            ctx.PSSetConstantBuffer(1, _backdropColors);
            ctx.IASetVertexBuffer(0, _backdrop, VertexStride);
            ctx.Draw(6, 0);
            ctx.VSSetShader(_vs);
            ctx.PSSetShader(_ps);
        }

        ctx.PSSetShader(_shadowPs);
        ctx.PSSetConstantBuffer(3, _shade);
        foreach (var screen in _screens)
        {
            if (screen.ShadowVertices is null)
            {
                continue;
            }

            var shade = new ShadowStyle(screen.PanelRect, screen.ShadowSpread, screen.ShadowDrop, screen.CornerRadius, 0.72f);
            ctx.UpdateSubresource(in shade, _shade);
            ctx.IASetVertexBuffer(0, screen.ShadowVertices, VertexStride);
            ctx.Draw(screen.ShadowVertexCount, 0);
        }

        ctx.PSSetShader(_ps);
        ctx.PSSetConstantBuffer(2, _panel);
        foreach (var screen in _screens)
        {
            var panel = new PanelStyle(screen.PanelRect.Width, screen.PanelRect.Height, screen.CornerRadius, screen.Floats ? 1f : 0f);
            ctx.UpdateSubresource(in panel, _panel);
            ctx.IASetVertexBuffer(0, screen.Vertices, VertexStride);
            ctx.PSSetShaderResource(0, screen.ShaderView);
            ctx.Draw(screen.VertexCount, 0);
        }
    }

    public void Dispose()
    {
        foreach (var s in _screens)
        {
            s.Dispose();
        }

        _rasterizer.Dispose();
        _sampler.Dispose();
        _blend.Dispose();
        _shade.Dispose();
        _panel.Dispose();
        _shadowPs.Dispose();
        _backdropColors.Dispose();
        _backdrop.Dispose();
        _frameBuffer.Dispose();
        _layout.Dispose();
        _backdropPs.Dispose();
        _backdropVs.Dispose();
        _ps.Dispose();
        _vs.Dispose();
    }

    /// <summary>Matches the HLSL backdrop constant buffer: each float3 occupies 16 bytes. 96 bytes total.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private readonly struct BackdropColors
    {
        public BackdropColors(Color4 a0, Color4 b0, Color4 a1, Color4 b1, Color4 a2, Color4 b2, float style, int count, float axis, float split1, float split2)
        {
            A0R = a0.R;
            A0G = a0.G;
            A0B = a0.B;
            Style = style;
            B0R = b0.R;
            B0G = b0.G;
            B0B = b0.B;
            Count = count;
            A1R = a1.R;
            A1G = a1.G;
            A1B = a1.B;
            Split1 = split1;
            B1R = b1.R;
            B1G = b1.G;
            B1B = b1.B;
            Split2 = split2;
            A2R = a2.R;
            A2G = a2.G;
            A2B = a2.B;
            Axis = axis;
            B2R = b2.R;
            B2G = b2.G;
            B2B = b2.B;
            Pad = 0;
        }

        public readonly float A0R;
        public readonly float A0G;
        public readonly float A0B;
        public readonly float Style;
        public readonly float B0R;
        public readonly float B0G;
        public readonly float B0B;
        public readonly float Count;
        public readonly float A1R;
        public readonly float A1G;
        public readonly float A1B;
        public readonly float Split1;
        public readonly float B1R;
        public readonly float B1G;
        public readonly float B1B;
        public readonly float Split2;
        public readonly float A2R;
        public readonly float A2G;
        public readonly float A2B;
        public readonly float Axis;
        public readonly float B2R;
        public readonly float B2G;
        public readonly float B2B;
        public readonly float Pad;
    }

    /// <summary>16 bytes. Rounds a flat screen. Curved screens leave <see cref="Round"/> at 0.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private readonly struct PanelStyle
    {
        public PanelStyle(float width, float height, float radius, float round)
        {
            SizeX = width;
            SizeY = height;
            Radius = radius;
            Round = round;
        }

        public readonly float SizeX;
        public readonly float SizeY;
        public readonly float Radius;
        public readonly float Round;
    }

    /// <summary>32 bytes. Soft shadow and a light rim around one flat screen.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private readonly struct ShadowStyle
    {
        public ShadowStyle(PixelRect rect, float spread, float drop, float radius, float strength)
        {
            X = rect.X;
            Y = rect.Y;
            Width = rect.Width;
            Height = rect.Height;
            Spread = spread;
            Drop = drop;
            Radius = radius;
            Strength = strength;
        }

        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;
        public readonly float Spread;
        public readonly float Drop;
        public readonly float Radius;
        public readonly float Strength;
    }

    /// <summary>Clip-space quad. U grows to the right, V grows downward.</summary>
    private static float[] BackdropQuad() =>
    [
        -1f, 1f, 0f, 0f, 0f,  1f, 1f, 0f, 1f, 0f,  -1f, -1f, 0f, 0f, 1f,
        -1f, -1f, 0f, 0f, 1f,  1f, 1f, 0f, 1f, 0f,   1f, -1f, 0f, 1f, 1f,
    ];
}

/// <summary>Colors and cuts for up to three virtual screens. Read on the render thread.</summary>
public struct BackdropLook
{
    public bool Draw { get; set; }

    public int Style { get; set; }

    public int ScreenCount { get; set; }

    public float Axis { get; set; }

    public float Split1 { get; set; }

    public float Split2 { get; set; }

    public Color4 A0 { get; set; }

    public Color4 B0 { get; set; }

    public Color4 A1 { get; set; }

    public Color4 B1 { get; set; }

    public Color4 A2 { get; set; }

    public Color4 B2 { get; set; }
}

/// <summary>Live frame colors. Read on the render thread, written from the UI.</summary>
public interface IBackdropSource
{
    void Read(out BackdropLook look);
}

/// <summary>One virtual monitor in the scene: its mesh and the texture its captured frames are copied into.</summary>
public sealed class ScreenSurface : IDisposable
{
    private readonly GraphicsDevice _gd;

    internal ScreenSurface(GraphicsDevice gd, ScreenPlacement placement, int width, int height)
        : this(gd, ViewMath.BuildScreenMesh(placement), width, height)
    {
        Placement = placement;
    }

    internal ScreenSurface(GraphicsDevice gd, float[] mesh, int width, int height)
    {
        _gd = gd;
        Width = width;
        Height = height;
        VertexCount = (uint)(mesh.Length / 5);
        Vertices = gd.Device.CreateBuffer(mesh, BindFlags.VertexBuffer);
        Texture = gd.Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.ShaderResource));
        ShaderView = gd.Device.CreateShaderResourceView(Texture);
    }

    public ScreenPlacement Placement { get; private set; }

    public int Width { get; }

    public int Height { get; }

    public ID3D11Texture2D Texture { get; }

    internal ID3D11Buffer Vertices { get; private set; }

    internal uint VertexCount { get; }

    internal ID3D11ShaderResourceView ShaderView { get; }

    internal ID3D11Buffer? ShadowVertices { get; private set; }

    internal uint ShadowVertexCount { get; private set; }

    internal PixelRect PanelRect { get; private set; }

    internal float ShadowSpread { get; private set; }

    internal float ShadowDrop { get; private set; }

    internal float CornerRadius { get; private set; }

    internal bool Floats { get; private set; }

    /// <summary>Shadow mesh and rounded-corner size for a flat glasses-anchor screen.</summary>
    internal void SetFloat(float[] shadowMesh, PixelRect panel, float spread, float drop, float radius)
    {
        ShadowVertices = _gd.Device.CreateBuffer(shadowMesh, BindFlags.VertexBuffer);
        ShadowVertexCount = (uint)(shadowMesh.Length / 5);
        PanelRect = panel;
        ShadowSpread = spread;
        ShadowDrop = drop;
        CornerRadius = radius;
        Floats = true;
    }

    /// <summary>Rebuilds the mesh for a new placement (render thread).</summary>
    internal void SetPlacement(ScreenPlacement placement)
    {
        float[] mesh = ViewMath.BuildScreenMesh(placement);
        var old = Vertices;
        Vertices = _gd.Device.CreateBuffer(mesh, BindFlags.VertexBuffer);
        old.Dispose();
        Placement = placement;
    }

    /// <summary>Copies a captured frame (any thread; the device is multithread-protected).</summary>
    public void Update(ID3D11Texture2D source, int sourceWidth, int sourceHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        int w = Math.Min(Width, sourceWidth), h = Math.Min(Height, sourceHeight);
        _gd.Context.CopySubresourceRegion(Texture, 0, 0, 0, 0, source, 0, new Box(0, 0, 0, w, h, 1));
    }

    public void Dispose()
    {
        ShaderView.Dispose();
        Texture.Dispose();
        Vertices.Dispose();
        ShadowVertices?.Dispose();
    }
}

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
        float4 PS(PsIn i) : SV_Target { return float4(Tex.Sample(Samp, i.uv).rgb, 1); }
        PsIn VSBackdrop(VsIn i) { PsIn o; o.pos = float4(i.pos.xy, 0, 1); o.uv = i.uv; return o; }
        float4 PSBackdrop(PsIn i) : SV_Target {
            float3 leftColor = float3(0.12, 0.04, 0.42);
            float3 rightColor = float3(0.00, 0.55, 0.72);
            return float4(lerp(leftColor, rightColor, saturate(i.uv.x)), 1);
        }
        """;

    private readonly GraphicsDevice _gd;
    private readonly ID3D11VertexShader _vs;
    private readonly ID3D11PixelShader _ps;
    private readonly ID3D11VertexShader _backdropVs;
    private readonly ID3D11PixelShader _backdropPs;
    private readonly ID3D11InputLayout _layout;
    private readonly ID3D11Buffer _frameBuffer;
    private readonly ID3D11Buffer _backdrop;
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
        ReadOnlyMemory<byte> backdropVsCode = Compiler.Compile(Shader, "VSBackdrop", "workspace.hlsl", "vs_5_0");
        ReadOnlyMemory<byte> backdropPsCode = Compiler.Compile(Shader, "PSBackdrop", "workspace.hlsl", "ps_5_0");
        _backdropVs = device.CreateVertexShader(backdropVsCode.Span);
        _backdropPs = device.CreatePixelShader(backdropPsCode.Span);
        _backdrop = device.CreateBuffer(BackdropQuad(), BindFlags.VertexBuffer);
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

    /// <summary>Draws a left-to-right linear gradient behind the screens (glasses-anchor frame and seams).</summary>
    public bool GradientBackdrop { get; set; }

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
        ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        ctx.IASetInputLayout(_layout);
        ctx.VSSetShader(_vs);
        ctx.VSSetConstantBuffer(0, _frameBuffer);
        ctx.PSSetShader(_ps);
        ctx.PSSetSampler(0, _sampler);

        if (GradientBackdrop)
        {
            ctx.VSSetShader(_backdropVs);
            ctx.PSSetShader(_backdropPs);
            ctx.IASetVertexBuffer(0, _backdrop, VertexStride);
            ctx.Draw(6, 0);
            ctx.VSSetShader(_vs);
            ctx.PSSetShader(_ps);
        }

        foreach (var screen in _screens)
        {
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
        _backdrop.Dispose();
        _frameBuffer.Dispose();
        _layout.Dispose();
        _backdropPs.Dispose();
        _backdropVs.Dispose();
        _ps.Dispose();
        _vs.Dispose();
    }

    /// <summary>Clip-space quad. U grows to the right, V grows downward.</summary>
    private static float[] BackdropQuad() =>
    [
        -1f, 1f, 0f, 0f, 0f,  1f, 1f, 0f, 1f, 0f,  -1f, -1f, 0f, 0f, 1f,
        -1f, -1f, 0f, 0f, 1f,  1f, 1f, 0f, 1f, 0f,   1f, -1f, 0f, 1f, 1f,
    ];
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
    }
}

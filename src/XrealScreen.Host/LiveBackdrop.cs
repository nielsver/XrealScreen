using Vortice.Mathematics;
using XrealScreen.Render.Scene;

namespace XrealScreen.Host;

/// <summary>
/// Frame colors the running workspace reads every frame, so a color change shows
/// without restarting. The screen sizes still wait for Apply when the frame appears or disappears.
/// </summary>
public sealed class LiveBackdrop : IBackdropSource
{
    private readonly object _gate = new();
    private bool _draw = true;
    private int _style;
    private int _count = 1;
    private float _axis;
    private float _split1 = 1f;
    private float _split2 = 1f;
    private readonly Color4[] _colors =
    [
        new(0.12f, 0.04f, 0.42f, 1f),
        new(0f, 0.55f, 0.72f, 1f),
        new(0.12f, 0.04f, 0.42f, 1f),
        new(0f, 0.55f, 0.72f, 1f),
        new(0.12f, 0.04f, 0.42f, 1f),
        new(0f, 0.55f, 0.72f, 1f),
    ];

    /// <summary><paramref name="rgb"/> is 18 bytes: six colors, R then G then B.</summary>
    public void Update(FrameStyle style, int screenCount, bool stacked, float split1, float split2, ReadOnlySpan<byte> rgb)
    {
        if (rgb.Length < 18)
        {
            throw new ArgumentException("Need 18 color bytes.", nameof(rgb));
        }

        lock (_gate)
        {
            _draw = style != FrameStyle.SeeThrough;
            _style = FrameStyles.ShaderIndex(style);
            _count = Math.Clamp(screenCount, 1, 3);
            _axis = stacked ? 1f : 0f;
            _split1 = split1;
            _split2 = split2;
            for (int i = 0; i < 6; i++)
            {
                _colors[i] = new Color4(rgb[i * 3] / 255f, rgb[i * 3 + 1] / 255f, rgb[i * 3 + 2] / 255f, 1f);
            }
        }
    }

    public void Read(out BackdropLook look)
    {
        lock (_gate)
        {
            look = new BackdropLook
            {
                Draw = _draw,
                Style = _style,
                ScreenCount = _count,
                Axis = _axis,
                Split1 = _split1,
                Split2 = _split2,
                A0 = _colors[0],
                B0 = _colors[1],
                A1 = _colors[2],
                B1 = _colors[3],
                A2 = _colors[4],
                B2 = _colors[5],
            };
        }
    }
}

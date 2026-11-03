using System.Globalization;

namespace XrealScreen.Core.Hud;

/// <summary>Which instrument a line belongs to.</summary>
public enum HudPart
{
    Reticle,
    Horizon,
    Ladder,
    Tape,
}

/// <summary>A straight stroke in normalized device coordinates, x right, y up, each axis from -1 to 1.</summary>
public readonly record struct HudSegment(HudPart Part, float X0, float Y0, float X1, float Y1);

/// <summary>A text run. <see cref="X"/> and <see cref="Y"/> are the center of the run in the same coordinates as <see cref="HudSegment"/>.</summary>
public readonly record struct HudText(string Text, float X, float Y);

/// <summary>Lines and labels for one HUD frame. The renderer only strokes this; it does not know the instruments.</summary>
public sealed record HudFrame(IReadOnlyList<HudSegment> Lines, IReadOnlyList<HudText> Labels);

/// <summary>Which instruments to draw. Brightness is a renderer concern.</summary>
public sealed record HudLayoutOptions
{
    public bool ShowHorizon { get; init; } = true;

    public bool ShowLadder { get; init; } = true;

    public bool ShowTape { get; init; } = true;

    public bool ShowClock { get; init; } = true;

    /// <summary>Normalized-device units per degree. 0.028 puts a ±20° ladder inside the frame.</summary>
    public float DegreesToNdc { get; init; } = 0.028f;
}

/// <summary>What the clear-view HUD draws this frame. Shared by the engine, the page, and the renderer.</summary>
public sealed record HudStyle
{
    public float Brightness { get; init; } = 0.75f;

    public bool ShowHorizon { get; init; } = true;

    public bool ShowLadder { get; init; } = true;

    public bool ShowTape { get; init; } = true;

    public bool ShowClock { get; init; } = true;

    public HudLayoutOptions ToLayout() => new()
    {
        ShowHorizon = ShowHorizon,
        ShowLadder = ShowLadder,
        ShowTape = ShowTape,
        ShowClock = ShowClock,
    };
}

/// <summary>
/// Flight-style HUD in normalized screen space. Head-locked: the horizon and pitch ladder counter-rotate
/// with roll, and shift with pitch, so they stay level with the room. The heading tape stays on the top edge.
/// Yaw zero is wherever the tracker was last recentered. Positive yaw is a turn to the left
/// (see <see cref="Tracking.HeadPose"/>).
/// </summary>
public static class HudLayout
{
    private static readonly int[] LadderMarks = [-20, -15, -10, -5, 5, 10, 15, 20];

    public static HudFrame Build(float yawDegrees, float pitchDegrees, float rollDegrees, string? clock, HudLayoutOptions? options = null)
    {
        options ??= new HudLayoutOptions();
        float scale = options.DegreesToNdc;
        var lines = new List<HudSegment>(32);
        var labels = new List<HudText>(24);

        AddReticle(lines);

        if (options.ShowHorizon)
        {
            float y = -pitchDegrees * scale;
            AddRolled(lines, HudPart.Horizon, -0.40f, y, 0.40f, y, rollDegrees);
        }

        if (options.ShowLadder)
        {
            foreach (int mark in LadderMarks)
            {
                float y = (mark - pitchDegrees) * scale;
                if (MathF.Abs(y) > 0.92f)
                {
                    continue;
                }

                float half = mark % 10 == 0 ? 0.14f : 0.07f;
                AddRolled(lines, HudPart.Ladder, -half, y, half, y, rollDegrees);
            }

            labels.Add(new HudText(FormatAngle("P", pitchDegrees), 0.62f, -0.78f));
            labels.Add(new HudText(FormatAngle("R", rollDegrees), 0.62f, -0.70f));
        }

        if (options.ShowTape)
        {
            int start = (int)MathF.Floor((yawDegrees - 50f) / 10f) * 10;
            int end = (int)MathF.Ceiling((yawDegrees + 50f) / 10f) * 10;
            for (int heading = start; heading <= end; heading += 10)
            {
                float x = (yawDegrees - heading) * scale;
                if (MathF.Abs(x) > 0.92f)
                {
                    continue;
                }

                lines.Add(new HudSegment(HudPart.Tape, x, 0.80f, x, 0.88f));
                labels.Add(new HudText(Wrap360(heading).ToString(CultureInfo.InvariantCulture), x, 0.72f));
            }

            labels.Add(new HudText(string.Create(CultureInfo.InvariantCulture, $"HDG {Wrap360((int)MathF.Round(yawDegrees)):000}"), 0f, 0.62f));
        }

        if (options.ShowClock && !string.IsNullOrEmpty(clock))
        {
            labels.Add(new HudText(clock, -0.78f, -0.82f));
        }

        return new HudFrame(lines, labels);
    }

    /// <summary>Wraps a degree value to 0–359.</summary>
    public static int Wrap360(int degrees)
    {
        int wrapped = degrees % 360;
        return wrapped < 0 ? wrapped + 360 : wrapped;
    }

    private static void AddReticle(List<HudSegment> lines)
    {
        lines.Add(new HudSegment(HudPart.Reticle, -0.06f, 0f, -0.02f, 0f));
        lines.Add(new HudSegment(HudPart.Reticle, 0.02f, 0f, 0.06f, 0f));
        lines.Add(new HudSegment(HudPart.Reticle, 0f, -0.06f, 0f, -0.02f));
        lines.Add(new HudSegment(HudPart.Reticle, 0f, 0.02f, 0f, 0.06f));
    }

    private static void AddRolled(List<HudSegment> lines, HudPart part, float x0, float y0, float x1, float y1, float rollDegrees)
    {
        float radians = rollDegrees * MathF.PI / 180f;
        float c = MathF.Cos(radians);
        float s = MathF.Sin(radians);
        lines.Add(new HudSegment(
            part,
            (x0 * c) - (y0 * s),
            (x0 * s) + (y0 * c),
            (x1 * c) - (y1 * s),
            (x1 * s) + (y1 * c)));
    }

    private static string FormatAngle(string name, float degrees) =>
        string.Create(CultureInfo.InvariantCulture, $"{name} {degrees:+0.0;-0.0;0.0}");
}

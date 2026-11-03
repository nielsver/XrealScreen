using System.Globalization;

namespace XrealScreen.Core.Hud;

/// <summary>Which instrument a line belongs to.</summary>
public enum HudPart
{
    Reticle,
    Horizon,
    Ladder,
    Tape,

    /// <summary>Sparse line drawings: corners, wings, heading caret, and the recenter marker.</summary>
    Marker,
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

    /// <summary>Corner brackets, wings, the heading caret, and the recenter diamond. Still only strokes.</summary>
    public bool ShowMarkers { get; init; } = true;

    /// <summary>Look-targets for the clipboard, timer, play, and mute.</summary>
    public bool ShowGlance { get; init; } = true;

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

    /// <summary>Corner brackets, wings, the heading caret, and the recenter diamond.</summary>
    public bool ShowMarkers { get; init; } = true;

    /// <summary>Look-targets for the clipboard, timer, play, mute, and screen read.</summary>
    public bool ShowGlance { get; init; } = true;

    public string GlanceUp { get; init; } = "CLIP";

    public string GlanceLeft { get; init; } = "25:00";

    public string GlanceRight { get; init; } = "PLAY";

    public string GlanceDown { get; init; } = "MUTE";

    /// <summary>The mark further up. Looking at it reads the main monitor.</summary>
    public string GlanceRead { get; init; } = "READ";

    /// <summary>The note under the reticle. Null leaves that area clear.</summary>
    public HudCaptionPicture? Caption { get; init; }

    public HudLayoutOptions ToLayout() => new()
    {
        ShowHorizon = ShowHorizon,
        ShowLadder = ShowLadder,
        ShowTape = ShowTape,
        ShowClock = ShowClock,
        ShowMarkers = ShowMarkers,
        ShowGlance = ShowGlance,
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

    public static HudFrame Build(float yawDegrees, float pitchDegrees, float rollDegrees, string? clock, HudLayoutOptions? options = null, IReadOnlyList<HudPin>? pins = null)
    {
        options ??= new HudLayoutOptions();
        float scale = options.DegreesToNdc;
        var lines = new List<HudSegment>(64);
        var labels = new List<HudText>(24);

        AddReticle(lines);
        if (options.ShowMarkers)
        {
            AddDrawings(lines, yawDegrees, pitchDegrees, rollDegrees, scale);
        }

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

        if (options.ShowGlance && pins is not null)
        {
            foreach (HudPin pin in pins)
            {
                (float x, float y) = PinScreen(yawDegrees, pitchDegrees, rollDegrees, pin.YawDegrees, pin.PitchDegrees, scale);
                if (MathF.Abs(x) > 1.05f || MathF.Abs(y) > 1.05f)
                {
                    continue;
                }

                AddCorner(lines, x - 0.045f, y + 0.045f, 0.03f, -0.03f);
                AddCorner(lines, x + 0.045f, y + 0.045f, -0.03f, 0.03f);
                AddCorner(lines, x - 0.045f, y - 0.045f, 0.03f, 0.03f);
                AddCorner(lines, x + 0.045f, y - 0.045f, -0.03f, -0.03f);
                if (pin.Dwell > 0f)
                {
                    float half = 0.06f * Math.Clamp(pin.Dwell, 0f, 1f);
                    lines.Add(new HudSegment(HudPart.Marker, x - half, y - 0.07f, x + half, y - 0.07f));
                }

                if (!string.IsNullOrEmpty(pin.Label))
                {
                    labels.Add(new HudText(pin.Label, x, y - 0.12f));
                }
            }
        }

        return new HudFrame(lines, labels);
    }

    /// <summary>Where a world-fixed pin sits on the glasses. The same mapping as the horizon and the heading tape.</summary>
    public static (float X, float Y) PinScreen(float yawDegrees, float pitchDegrees, float rollDegrees, float pinYaw, float pinPitch, float scale)
    {
        float x = (yawDegrees - pinYaw) * scale;
        float y = (pinPitch - pitchDegrees) * scale;
        float radians = rollDegrees * MathF.PI / 180f;
        float cosine = MathF.Cos(radians);
        float sine = MathF.Sin(radians);
        return ((x * cosine) - (y * sine), (x * sine) + (y * cosine));
    }

    /// <summary>Index of the pin closest to the reticle, or -1 when none is inside <paramref name="radius"/>.</summary>
    public static int NearestPin(float yawDegrees, float pitchDegrees, float rollDegrees, IReadOnlyList<HudPin> pins, float scale, float radius)
    {
        int best = -1;
        float bestDistance = radius;
        for (int i = 0; i < pins.Count; i++)
        {
            (float x, float y) = PinScreen(yawDegrees, pitchDegrees, rollDegrees, pins[i].YawDegrees, pins[i].PitchDegrees, scale);
            float distance = MathF.Sqrt((x * x) + (y * y));
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = pins[i].Id;
            }
        }

        return best;
    }

    /// <summary>Wraps a degree value to 0–359.</summary>
    public static int Wrap360(int degrees)
    {
        int wrapped = degrees % 360;
        return wrapped < 0 ? wrapped + 360 : wrapped;
    }

    /// <summary>Line drawings only. No filled panel, so the room stays visible.</summary>
    private static void AddDrawings(List<HudSegment> lines, float yawDegrees, float pitchDegrees, float rollDegrees, float scale)
    {
        AddCorner(lines, -0.90f, 0.82f, 0.07f, -0.07f);
        AddCorner(lines, 0.90f, 0.82f, -0.07f, -0.07f);
        AddCorner(lines, -0.90f, -0.82f, 0.07f, 0.07f);
        AddCorner(lines, 0.90f, -0.82f, -0.07f, 0.07f);

        lines.Add(new HudSegment(HudPart.Marker, -0.18f, -0.02f, -0.07f, 0f));
        lines.Add(new HudSegment(HudPart.Marker, 0.07f, 0f, 0.18f, -0.02f));
        lines.Add(new HudSegment(HudPart.Marker, -0.028f, 0.90f, 0f, 0.95f));
        lines.Add(new HudSegment(HudPart.Marker, 0.028f, 0.90f, 0f, 0.95f));

        float homeX = yawDegrees * scale;
        float homeY = (-pitchDegrees * scale) + 0.12f;
        float radians = rollDegrees * MathF.PI / 180f;
        float cosine = MathF.Cos(radians);
        float sine = MathF.Sin(radians);
        float screenX = (homeX * cosine) - (homeY * sine);
        float screenY = (homeX * sine) + (homeY * cosine);
        const float edge = 0.86f;
        if (MathF.Abs(screenX) <= edge && MathF.Abs(screenY) <= edge)
        {
            AddDiamond(lines, screenX, screenY, 0.03f, cosine, sine);
        }
        else
        {
            AddEdgeChevron(lines, screenX, screenY);
        }
    }

    private static void AddCorner(List<HudSegment> lines, float x, float y, float armX, float armY)
    {
        lines.Add(new HudSegment(HudPart.Marker, x, y, x + armX, y));
        lines.Add(new HudSegment(HudPart.Marker, x, y, x, y + armY));
    }

    private static void AddDiamond(List<HudSegment> lines, float centerX, float centerY, float radius, float cosine, float sine)
    {
        Span<(float X, float Y)> points = stackalloc (float, float)[4];
        points[0] = Rotate(centerX, centerY, 0f, radius, cosine, sine);
        points[1] = Rotate(centerX, centerY, radius, 0f, cosine, sine);
        points[2] = Rotate(centerX, centerY, 0f, -radius, cosine, sine);
        points[3] = Rotate(centerX, centerY, -radius, 0f, cosine, sine);
        for (int i = 0; i < points.Length; i++)
        {
            (float x0, float y0) = points[i];
            (float x1, float y1) = points[(i + 1) % points.Length];
            lines.Add(new HudSegment(HudPart.Marker, x0, y0, x1, y1));
        }
    }

    private static (float X, float Y) Rotate(float centerX, float centerY, float x, float y, float cosine, float sine) =>
        (centerX + (x * cosine) - (y * sine), centerY + (x * sine) + (y * cosine));

    private static void AddEdgeChevron(List<HudSegment> lines, float towardX, float towardY)
    {
        float length = MathF.Max(MathF.Sqrt((towardX * towardX) + (towardY * towardY)), 0.001f);
        float ux = towardX / length;
        float uy = towardY / length;
        float tipX = ux * 0.82f;
        float tipY = uy * 0.82f;
        float backX = tipX - (ux * 0.05f);
        float backY = tipY - (uy * 0.05f);
        float px = -uy * 0.03f;
        float py = ux * 0.03f;
        lines.Add(new HudSegment(HudPart.Marker, tipX, tipY, backX + px, backY + py));
        lines.Add(new HudSegment(HudPart.Marker, tipX, tipY, backX - px, backY - py));
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

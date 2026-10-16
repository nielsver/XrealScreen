namespace XrealScreen.Core.Workspace;

public enum LayoutPreset
{
    /// <summary>Screens side by side on a cylinder around the user.</summary>
    Arc,

    /// <summary>Screens in rows of up to three, rows stacked vertically on the cylinder.</summary>
    Grid,
}

/// <summary>A virtual screen the user wants in the workspace.</summary>
public sealed record ScreenSpec(int Id, UltrawideMode Mode, int RefreshHz = 60)
{
    public Resolution Resolution => UltrawideModes.GetResolution(Mode);
}

/// <summary>Where a screen sits in the head-centered world, angles in degrees.</summary>
public readonly record struct ScreenPlacement(int ScreenId, float YawDegrees, float PitchDegrees, float WidthMeters, float HeightMeters, float DistanceMeters);

public sealed record WorkspaceSettings
{
    public LayoutPreset Preset { get; init; } = LayoutPreset.Arc;

    /// <summary>Viewing distance (cylinder radius).</summary>
    public float DistanceMeters { get; init; } = 1.5f;

    /// <summary>Physical width of a 16:9 screen at <see cref="DistanceMeters"/>.</summary>
    public float BaseScreenWidthMeters { get; init; } = 1.2f;

    /// <summary>Gap between neighbouring screens.</summary>
    public float GapMeters { get; init; } = 0.04f;
}

public static class WorkspaceLayout
{
    public const int MaxScreens = 6;

    /// <summary>Screens per row in <see cref="LayoutPreset.Grid"/>. A short last row is centered.</summary>
    public const int GridColumns = 3;

    /// <summary>
    /// Crossing from three or fewer screens to four or more selects the grid, so the extra screens
    /// sit in a second row instead of wrapping behind the user. An arc chosen after that is kept.
    /// </summary>
    public static LayoutPreset PresetForCount(int previousCount, int count, LayoutPreset current) =>
        count > GridColumns && previousCount <= GridColumns && current == LayoutPreset.Arc
            ? LayoutPreset.Grid
            : current;

    /// <summary>Computes placements so the whole workspace is centered on yaw 0 / pitch 0.</summary>
    public static IReadOnlyList<ScreenPlacement> Compute(IReadOnlyList<ScreenSpec> screens, WorkspaceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(settings);
        if (screens.Count > MaxScreens)
        {
            throw new ArgumentOutOfRangeException(nameof(screens), $"At most {MaxScreens} screens are supported.");
        }

        var rows = settings.Preset == LayoutPreset.Arc
            ? [screens.ToList()]
            : screens.Chunk(GridColumns).Select(c => c.ToList()).ToList();

        float r = settings.DistanceMeters;
        float metersPerPixel = settings.BaseScreenWidthMeters / UltrawideModes.Base.Width;
        var result = new List<ScreenPlacement>(screens.Count);

        float totalHeight = rows.Sum(row => row.Max(s => s.Resolution.Height) * metersPerPixel) + settings.GapMeters * (rows.Count - 1);
        float top = totalHeight / 2f;

        foreach (var row in rows)
        {
            float rowHeight = row.Max(s => s.Resolution.Height) * metersPerPixel;
            float rowCenterY = top - rowHeight / 2f;
            float pitch = MathF.Atan2(rowCenterY, r) * 180f / MathF.PI;

            float totalWidth = row.Sum(s => s.Resolution.Width * metersPerPixel) + settings.GapMeters * (row.Count - 1);
            float x = -totalWidth / 2f;
            foreach (var screen in row)
            {
                float w = screen.Resolution.Width * metersPerPixel;
                float h = screen.Resolution.Height * metersPerPixel;
                // Arc length → angle on the cylinder. Positive yaw = to the left (Z-up, counter-clockwise).
                float yaw = -(x + w / 2f) / r * 180f / MathF.PI;
                result.Add(new ScreenPlacement(screen.Id, yaw, pitch, w, h, r));
                x += w + settings.GapMeters;
            }

            top -= rowHeight + settings.GapMeters;
        }

        return result;
    }
}

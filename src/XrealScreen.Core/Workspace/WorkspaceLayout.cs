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

    /// <summary>One head-tracked screen: its pixels on the glasses picture, and where that rectangle sits in view.</summary>
    public readonly record struct FittedScreen(PixelRect Pixels, ScreenPlacement Placement);

    /// <summary>
    /// Shares the glasses picture across the screens and places each one so, at 100% width and height,
    /// the group fills the glasses field of view. Six screens in a grid stay in front: two rows of three.
    /// An arc stays one row, still inside the picture, instead of wrapping behind the user.
    /// </summary>
    public static IReadOnlyList<FittedScreen> FitView(
        Resolution picture,
        int count,
        LayoutPreset preset,
        double widthScale,
        double heightScale,
        int gapPixels,
        int marginPixels,
        float distanceMeters,
        UltrawideMode shape = UltrawideMode.Off,
        float horizontalFovDegrees = 45f,
        float verticalFovDegrees = 29f)
    {
        if (picture.Width < 2 || picture.Height < 2)
        {
            return [];
        }

        count = Math.Clamp(count, 1, MaxScreens);
        int columns = preset == LayoutPreset.Grid ? GridColumns : count;
        widthScale = Math.Clamp(widthScale, AnchorSplit.MinSizeScale, 1);
        heightScale = Math.Clamp(heightScale, AnchorSplit.MinSizeScale, 1);
        var rects = AnchorSplit.Place(picture, count, Math.Max(0, gapPixels), AnchorAspect.Fill, framePx: 0, widthScale, columns, heightScale, AnchorSplit.NormalizeMarginPixels(marginPixels));
        double? ratio = HeadShapeRatio(shape);
        if (ratio is { } r)
        {
            rects = [.. rects.Select(rect => FitAspect(rect, r))];
        }

        float radius = MathF.Max(0.5f, distanceMeters);
        float tanH = MathF.Tan(Math.Clamp(horizontalFovDegrees, 1f, 170f) * MathF.PI / 360f);
        float tanV = MathF.Tan(Math.Clamp(verticalFovDegrees, 1f, 170f) * MathF.PI / 360f);
        var fitted = new List<FittedScreen>(rects.Count);
        for (int i = 0; i < rects.Count; i++)
        {
            PixelRect rect = rects[i];
            float yawL = YawAt(rect.X);
            float yawR = YawAt(rect.X + rect.Width);
            float yaw = (yawL + yawR) / 2f;
            float widthMeters = MathF.Abs(yawL - yawR) * radius;
            float zTop = NdcY(rect.Y) * tanV * radius * MathF.Cos(yaw);
            float zBottom = NdcY(rect.Y + rect.Height) * tanV * radius * MathF.Cos(yaw);
            float centerZ = (zTop + zBottom) / 2f;
            fitted.Add(new FittedScreen(rect, new ScreenPlacement(
                i + 1,
                yaw * 180f / MathF.PI,
                MathF.Atan2(centerZ, radius) * 180f / MathF.PI,
                widthMeters,
                MathF.Abs(zTop - zBottom),
                radius)));
        }

        return fitted;

        float YawAt(int pixelX) => MathF.Atan(-NdcX(pixelX) * tanH);
        float NdcX(int pixelX) => pixelX / (float)picture.Width * 2f - 1f;
        float NdcY(int pixelY) => 1f - pixelY / (float)picture.Height * 2f;
    }

    /// <summary>Null keeps the whole cell. The other shapes fit inside the cell.</summary>
    public static double? HeadShapeRatio(UltrawideMode shape) => shape switch
    {
        UltrawideMode.Wide21x9 => 21.0 / 9.0,
        UltrawideMode.Wide32x9 => 32.0 / 9.0,
        UltrawideMode.Tall16x18 => 16.0 / 18.0,
        _ => null,
    };

    private static PixelRect FitAspect(PixelRect cell, double ratio)
    {
        int width = cell.Width;
        int height = EvenSize(cell.Width / ratio);
        if (height > cell.Height)
        {
            height = cell.Height;
            width = EvenSize(cell.Height * ratio);
        }

        return new PixelRect(cell.X + (cell.Width - width) / 2, cell.Y + (cell.Height - height) / 2, Math.Max(2, width), Math.Max(2, height));
    }

    private static int EvenSize(double value) => Math.Max(2, (int)Math.Floor(value / 2.0) * 2);
}

using XrealScreen.Core.Abstractions;

namespace XrealScreen.Display;

/// <summary>
/// Desktop order for the virtual workspace: [desk monitors][virtual screens][glasses], or
/// [virtual screens][desk monitors][glasses] when the glasses are the primary monitor.
/// A grid uses the same rows as the spatial layout (three across, extra rows below) so the mouse
/// moves the way the screens sit in the glasses. An arc stays one horizontal row.
/// The glasses area is covered by the 3D window, so a mouse pointer there is head-locked and
/// clicks do nothing ([verified-hw] user feedback 2026-09-26). Putting the virtual monitors right
/// next to the desk monitors means the pointer reaches them without crossing the glasses area.
/// </summary>
public static class WorkspaceArrangement
{
    /// <param name="monitors">Active monitors.</param>
    /// <param name="virtualGdiNames">Our virtual monitors, in workspace order (row by row, left to right).</param>
    /// <param name="glassesGdiName">The glasses output.</param>
    /// <param name="primaryIndex">
    /// Virtual monitor (index into <paramref name="virtualGdiNames"/>) to make the Windows primary monitor;
    /// -1 keeps the current primary. Windows' primary is the monitor at (0,0), so the whole layout shifts.
    /// </param>
    /// <param name="gridColumns">
    /// Screens per row. 0 or 1 keeps a single row. More than that stacks extra screens underneath,
    /// and a short last row is centered, matching <see cref="XrealScreen.Core.Workspace.WorkspaceLayout"/>.
    /// </param>
    public static DisplayLayout Arrange(IReadOnlyList<DisplayMonitor> monitors, IReadOnlyList<string> virtualGdiNames, string glassesGdiName, int primaryIndex = -1, int gridColumns = 0)
    {
        var layout = ArrangeKeepingPrimary(monitors, virtualGdiNames, glassesGdiName, gridColumns);
        if (primaryIndex < 0 || primaryIndex >= virtualGdiNames.Count
            || monitors.FirstOrDefault(m => m.GdiDeviceName == virtualGdiNames[primaryIndex]) is not { } primary
            || layout.Monitors.FirstOrDefault(p => p.DevicePath == primary.DevicePath) is not { } origin)
        {
            return layout;
        }

        return layout with
        {
            Monitors = [.. layout.Monitors.Select(p => p with
            {
                X = p.X - origin.X,
                Y = p.Y - origin.Y,
                IsPrimary = p.X == origin.X && p.Y == origin.Y,
            })],
        };
    }

    private static DisplayLayout ArrangeKeepingPrimary(IReadOnlyList<DisplayMonitor> monitors, IReadOnlyList<string> virtualGdiNames, string glassesGdiName, int gridColumns)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(virtualGdiNames);
        var placements = new List<MonitorPlacement>();
        var fixedMonitors = monitors.Where(m => m.GdiDeviceName != glassesGdiName && !virtualGdiNames.Contains(m.GdiDeviceName)).ToList();
        foreach (var m in fixedMonitors)
        {
            placements.Add(Place(m, m.X, m.Y));
        }

        var glasses = monitors.FirstOrDefault(m => m.GdiDeviceName == glassesGdiName);
        var virtualMonitors = virtualGdiNames.Select(n => monitors.FirstOrDefault(v => v.GdiDeviceName == n)).OfType<DisplayMonitor>().ToList();
        int y = fixedMonitors.FirstOrDefault(m => m.IsPrimary)?.Y ?? 0;
        int blockWidth = BlockWidth(virtualMonitors, gridColumns);

        // Glasses are the primary monitor (Windows does this for some UltraWide modes, [verified-hw]):
        // they must stay at (0,0), so put the virtual monitors LEFT of the desk: [virtual screens][desk][glasses].
        if (glasses is { IsPrimary: true } && fixedMonitors.Count > 0)
        {
            int left = fixedMonitors.Min(m => m.X) - blockWidth;
            foreach (var placed in PlaceRows(virtualMonitors, left, glasses.Y, gridColumns))
            {
                placements.Add(Place(placed.Monitor, placed.X, placed.Y));
            }

            placements.Add(Place(glasses, glasses.X, glasses.Y));
            return new DisplayLayout(DateTimeOffset.Now, placements);
        }

        int x = fixedMonitors.Count == 0 ? (glasses is null ? 0 : glasses.X + glasses.Resolution.Width) : fixedMonitors.Max(m => m.X + m.Resolution.Width);
        foreach (var placed in PlaceRows(virtualMonitors, x, y, gridColumns))
        {
            placements.Add(Place(placed.Monitor, placed.X, placed.Y));
        }

        if (glasses is not null)
        {
            // Only monitor, or primary without a desk monitor: it must stay at (0,0).
            // Otherwise it sits to the right of the whole block, including a second row.
            placements.Add(glasses.IsPrimary ? Place(glasses, glasses.X, glasses.Y) : Place(glasses, x + blockWidth, y));
        }

        return new DisplayLayout(DateTimeOffset.Now, placements);
    }

    private readonly record struct PlacedRow(DisplayMonitor Monitor, int X, int Y);

    /// <summary>Width of the widest row. One row when <paramref name="columns"/> is below 2.</summary>
    private static int BlockWidth(List<DisplayMonitor> monitors, int columns)
    {
        if (monitors.Count == 0)
        {
            return 0;
        }

        if (columns <= 1 || monitors.Count <= columns)
        {
            return monitors.Sum(m => m.Resolution.Width);
        }

        return monitors.Chunk(columns).Max(row => row.Sum(m => m.Resolution.Width));
    }

    /// <summary>Rows of <paramref name="columns"/>, top row first. A short last row is centered. No gaps, so the pointer can cross.</summary>
    private static List<PlacedRow> PlaceRows(List<DisplayMonitor> monitors, int left, int top, int columns)
    {
        var placed = new List<PlacedRow>(monitors.Count);
        if (monitors.Count == 0)
        {
            return placed;
        }

        var rows = columns > 1 && monitors.Count > columns
            ? monitors.Chunk(columns).Select(c => c.ToList()).ToList()
            : [monitors.ToList()];
        int blockWidth = rows.Max(row => row.Sum(m => m.Resolution.Width));
        int y = top;
        foreach (var row in rows)
        {
            int x = left + (blockWidth - row.Sum(m => m.Resolution.Width)) / 2;
            int rowHeight = 0;
            foreach (var monitor in row)
            {
                placed.Add(new PlacedRow(monitor, x, y));
                x += monitor.Resolution.Width;
                rowHeight = Math.Max(rowHeight, monitor.Resolution.Height);
            }

            y += rowHeight;
        }

        return placed;
    }

    private static MonitorPlacement Place(DisplayMonitor m, int x, int y) =>
        new(m.DevicePath, m.EdidId, m.FriendlyName, x, y, m.Resolution.Width, m.Resolution.Height, m.RefreshHz, x == 0 && y == 0);
}

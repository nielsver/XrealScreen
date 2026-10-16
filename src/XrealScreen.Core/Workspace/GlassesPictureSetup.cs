using XrealScreen.Core.Abstractions;

namespace XrealScreen.Core.Workspace;

/// <summary>The picture mode each workspace asks the glasses to show.</summary>
public static class GlassesPictureSetup
{
    /// <summary>
    /// Glasses anchor and head tracking both want the full 32:9 width (3840×1080 at 60 Hz).
    /// A 1920-wide mode stops before the real left and right edges of that picture.
    /// When 3840×1080 is not offered, head tracking uses 1920×1200 at 120 Hz, then 1920×1080 at 120 Hz.
    /// Returns null when the glasses offer no usable picture.
    /// </summary>
    public static DisplayMode? Choose(bool glassesAnchor, IReadOnlyList<DisplayMode> modes)
    {
        ArgumentNullException.ThrowIfNull(modes);
        var wide = modes.Where(m => m.Resolution.Width == 3840 && m.Resolution.Height == 1080).ToList();
        if (wide.Count > 0)
        {
            return wide.OrderBy(m => Math.Abs(m.RefreshHz - 60)).ThenByDescending(m => m.BitsPerPixel).First();
        }

        if (glassesAnchor)
        {
            var ultra = modes.Where(m => AnchorSplit.IsUltrawideSignal(m.Resolution)).ToList();
            return ultra.Count == 0
                ? null
                : ultra
                    .OrderByDescending(m => m.Resolution.Width * (long)m.Resolution.Height)
                    .ThenBy(m => Math.Abs(m.RefreshHz - 60))
                    .ThenByDescending(m => m.BitsPerPixel)
                    .First();
        }

        DisplayMode? Exact(int width, int height, int hz)
        {
            var hits = modes.Where(m => m.Resolution.Width == width && m.Resolution.Height == height && m.RefreshHz == hz)
                .OrderByDescending(m => m.BitsPerPixel)
                .ToList();
            return hits.Count == 0 ? null : hits[0];
        }

        if (Exact(1920, 1200, 120) is { } native120)
        {
            return native120;
        }

        if (Exact(1920, 1080, 120) is { } fullHd120)
        {
            return fullHd120;
        }

        if (Exact(1920, 1200, 90) is { } native90)
        {
            return native90;
        }

        if (Exact(1920, 1080, 90) is { } fullHd90)
        {
            return fullHd90;
        }

        if (Exact(1920, 1200, 60) is { } native60)
        {
            return native60;
        }

        if (Exact(1920, 1080, 60) is { } fullHd60)
        {
            return fullHd60;
        }

        var normal = modes.Where(m => !AnchorSplit.IsUltrawideSignal(m.Resolution) && m.Resolution.Width >= 1280).ToList();
        return normal.Count == 0
            ? null
            : normal
                .OrderByDescending(m => m.RefreshHz)
                .ThenByDescending(m => m.Resolution.Width * (long)m.Resolution.Height)
                .ThenByDescending(m => m.BitsPerPixel)
                .First();
    }
}

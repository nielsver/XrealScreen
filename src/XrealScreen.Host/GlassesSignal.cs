using XrealScreen.Core.Workspace;
using XrealScreen.Display;

namespace XrealScreen.Host;

/// <summary>What the glasses currently send to Windows, described for the UI.</summary>
public static class GlassesSignal
{
    /// <summary>Current glasses resolution, or null when the glasses are not an active monitor.</summary>
    public static Resolution? CurrentResolution()
    {
        try
        {
            return GlassesDisplayLocator.FindGlasses(new CcdDisplayTopology().GetActiveMonitors())?.Resolution;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public static string Describe(int screenCount, int gapPixels = 0, AnchorAspect aspect = AnchorAspect.Fill, double widthScale = 1, int columns = 0)
    {
        try
        {
            var glasses = GlassesDisplayLocator.FindGlasses(new CcdDisplayTopology().GetActiveMonitors());
            if (glasses is null)
            {
                return "Glasses not detected as a monitor (connect them; Windows must extend the desktop).";
            }

            var r = glasses.Resolution;
            if (!AnchorSplit.IsUltrawideSignal(r))
            {
                return $"The glasses send {r} — UltraWide is Off. For Glasses anchor set Spatial Screen → UltraWide Mode to 32:9, 16:18 or 21:9 in the glasses menu.";
            }

            string name = r.Height > r.Width ? "16:18" : (double)r.Width / r.Height > 3 ? "32:9" : "21:9";
            var split = AnchorSplit.ScaleWidth(AnchorSplit.Split(r, screenCount, gapPixels, aspect, columns), widthScale);
            int rows = split.Select(s => s.Y).Distinct().Count();
            string arrangement = rows > 1 ? $" in {rows} rows" : string.Empty;
            return $"The glasses send {name} ({r}) → {split.Count} screen(s){arrangement} of {split[0].Width}×{split[0].Height}. Change UltraWide in the glasses menu; a running workspace adapts automatically.";
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return $"Could not read the displays: {ex.Message}";
        }
    }
}

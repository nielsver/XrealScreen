using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using XrealScreen.Host;

namespace XrealScreen.App.Looks;

/// <summary>
/// Window looks, picked under Settings → Look (Stage is the default). Each look is a resource dictionary
/// <c>Looks/&lt;Name&gt;Look.xaml</c> plus its own Home page; the other pages only take the look's resources.
/// </summary>
public enum AppLook
{
    Classic,
    Lens,
    Instrument,
    Stage,
}

/// <summary>Formatting and actions shared by the look Home pages (used from x:Bind).</summary>
public static class LookFormat
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>"+04.6°" / "−12.0°" with a real minus sign, fixed width for live readouts.</summary>
    public static string Degrees(double value) =>
        (value < 0 ? "−" : "+") + Math.Abs(value).ToString("00.0", Invariant) + "°";

    public static string StartLabel(WorkspaceState state) => state switch
    {
        WorkspaceState.Running => "Stop",
        WorkspaceState.Starting => "Starting…",
        WorkspaceState.Stopping => "Stopping…",
        _ => "Start",
    };

    public static string StartLabelUpper(WorkspaceState state) => StartLabel(state).ToUpperInvariant();

    public static string StartGlyph(WorkspaceState state) => ((char)(state == WorkspaceState.Running ? 0xE71A : 0xE768)).ToString(); // Stop / Play

    public static string StateUpper(WorkspaceState state) => state.ToString().ToUpperInvariant();

    public static Brush StateBrush(WorkspaceState state) => new SolidColorBrush(state switch
    {
        WorkspaceState.Running => ColorHelper.FromArgb(255, 0x2F, 0xB3, 0x6E),
        WorkspaceState.Faulted => ColorHelper.FromArgb(255, 0xE0, 0x4A, 0x3A),
        WorkspaceState.Starting or WorkspaceState.Stopping => ColorHelper.FromArgb(255, 0xF2, 0xA1, 0x3B),
        _ => ColorHelper.FromArgb(255, 0x8A, 0x95, 0xA3),
    });

    public static string ScreensText(double count, int kindIndex)
    {
        int n = double.IsNaN(count) ? 1 : (int)Math.Round(count);
        return $"{n} {(n == 1 ? "screen" : "screens")} · {(kindIndex == 0 ? "glasses anchor" : "head tracking")}";
    }

    public static string ScreensTextUpper(double count, int kindIndex) => ScreensText(count, kindIndex).ToUpperInvariant();

    public static string ScreenCountText(double count)
    {
        int n = double.IsNaN(count) ? 1 : (int)Math.Round(count);
        return $"{n} {(n == 1 ? "screen" : "screens")}";
    }

    public static string DriverText(bool missing) => missing ? "MISSING" : "INSTALLED";

    public static string Upper(string? text) => (text ?? string.Empty).ToUpperInvariant();

    public static string WorkspaceTitle(string? selected) => string.IsNullOrEmpty(selected) ? "Latest settings" : selected;

    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility ShowIfEmpty(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hide(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static bool Is(int value, int expected) => value == expected;

    public static bool? Checked(int value, int expected) => value == expected;

    public static bool Below(double value, double max) => !double.IsNaN(value) && value < max;

    public static bool Above(double value, double min) => !double.IsNaN(value) && value > min;

    /// <summary>One button for both: stops a running workspace, starts an idle one.</summary>
    public static void StartOrStop()
    {
        var session = App.Session;
        if (session.IsRunning)
        {
            if (session.StopCommand.CanExecute(null))
            {
                session.StopCommand.Execute(null);
            }
        }
        else if (session.StartCommand.CanExecute(null))
        {
            session.StartCommand.Execute(null);
        }
    }

    /// <summary>Changes the number of screens by <paramref name="delta"/> within the limit of the workspace type.</summary>
    public static void StepScreens(int delta)
    {
        var workspace = App.Workspace;
        double count = double.IsNaN(workspace.ScreenCount) ? 1 : Math.Round(workspace.ScreenCount);
        workspace.ScreenCount = Math.Clamp(count + delta, 1, workspace.MaxScreenCount);
    }
}

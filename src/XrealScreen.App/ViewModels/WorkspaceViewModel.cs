using CommunityToolkit.Mvvm.ComponentModel;
using XrealScreen.Core.Workspace;

namespace XrealScreen.App.ViewModels;

/// <summary>Screens page: how many virtual screens, ultrawide mode and layout.</summary>
public sealed partial class WorkspaceViewModel : ObservableObject
{
    public WorkspaceViewModel() => Recompute();

    public event EventHandler? LayoutChanged;

    public IReadOnlyList<string> UltrawideModeNames { get; } = UltrawideModes.All.Select(UltrawideModes.GetDisplayName).ToList();

    public IReadOnlyList<string> PresetNames { get; } = ["Arc", "Grid"];

    /// <summary>0 = glasses anchor, 1 = app head tracking.</summary>
    public IReadOnlyList<string> KindNames { get; } =
    [
        "Glasses anchor — most stable. Glasses menu: Anchor mode + UltraWide 32:9, 16:18 or 21:9. 1–3 screens.",
        "App head tracking — curved screens, up to 6. Glasses menu: Follow mode, UltraWide Off, Stabilizer off.",
    ];

    [ObservableProperty]
    public partial int KindIndex { get; set; }

    public bool IsAppTracking => KindIndex == 1;

    partial void OnKindIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAppTracking));
        OnPropertyChanged(nameof(IsGlassesAnchor));
        OnPropertyChanged(nameof(MaxScreenCount));
        if (ScreenCount > MaxScreenCount)
        {
            ScreenCount = MaxScreenCount; // refreshes the preview too
            return;
        }

        RefreshGlassesSignal();
    }

    /// <summary>Glasses anchor splits one ultrawide image into at most 3 screens; app head tracking allows 6.</summary>
    public double MaxScreenCount => IsGlassesAnchor ? AnchorSplit.MaxScreens : WorkspaceLayout.MaxScreens;

    public bool IsGlassesAnchor => KindIndex == 0;

    public IReadOnlyList<string> AnchorAspectNames { get; } = ["16:9 (recommended)", "16:10", "Fill the height"];

    /// <summary>Index = (int)AnchorAspect.</summary>
    [ObservableProperty]
    public partial int AnchorAspectIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnchorGapText))]
    public partial double AnchorGapPixels { get; set; } = 10;

    public string AnchorGapText => $"{Math.Round(AnchorGapPixels):F0} px";

    public XrealScreen.Core.Workspace.AnchorAspect AnchorAspect => (XrealScreen.Core.Workspace.AnchorAspect)Math.Clamp(AnchorAspectIndex, 0, 2);

    /// <summary>True when count/gap/shape changed since the last Apply or Start.</summary>
    [ObservableProperty]
    public partial bool HasPendingScreenChanges { get; set; }

    /// <summary>Glasses image size used for the preview (the real signal, or a 32:9 example).</summary>
    public XrealScreen.Core.Workspace.Resolution AnchorCanvas { get; private set; } = new(3840, 1080);

    public bool AnchorCanvasIsExample { get; private set; } = true;

    public IReadOnlyList<XrealScreen.Core.Workspace.PixelRect> AnchorRects { get; private set; } = [];

    /// <summary>Raised when the glasses-anchor preview must be redrawn.</summary>
    public event EventHandler? AnchorPreviewChanged;

    partial void OnAnchorAspectIndexChanged(int value) => OnAnchorSettingChanged();

    partial void OnAnchorGapPixelsChanged(double value) => OnAnchorSettingChanged();

    private void OnAnchorSettingChanged()
    {
        HasPendingScreenChanges = true;
        RefreshGlassesSignal();
    }

    public void MarkScreenSettingsApplied() => HasPendingScreenChanges = false;

    /// <summary>What the glasses send right now (glasses-anchor mode), e.g. "32:9 (3840×1080) → 2 screens".</summary>
    [ObservableProperty]
    public partial string GlassesSignal { get; set; } = string.Empty;

    public void RefreshGlassesSignal()
    {
        int count = double.IsNaN(ScreenCount) ? 1 : (int)Math.Clamp(Math.Round(ScreenCount), 1, XrealScreen.Core.Workspace.AnchorSplit.MaxScreens);
        int gap = (int)Math.Round(AnchorGapPixels);
        GlassesSignal = XrealScreen.Host.GlassesSignal.Describe(count, gap, AnchorAspect);

        var current = XrealScreen.Host.GlassesSignal.CurrentResolution();
        AnchorCanvasIsExample = current is not { } r || !XrealScreen.Core.Workspace.AnchorSplit.IsUltrawideSignal(r);
        AnchorCanvas = AnchorCanvasIsExample ? new XrealScreen.Core.Workspace.Resolution(3840, 1080) : current!.Value;
        AnchorRects = XrealScreen.Core.Workspace.AnchorSplit.Split(AnchorCanvas, count, gap, AnchorAspect);
        RebuildPrimaryScreenNames();
        AnchorPreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    [ObservableProperty]
    public partial double ScreenCount { get; set; } = 3;

    private const string KeepPrimary = "Keep my current primary monitor";

    /// <summary>
    /// "Keep…" plus one entry per screen of the current layout, named like the numbers in the preview
    /// ("Screen 1 — left"). Index = WorkspaceOptions.PrimaryScreen.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> PrimaryScreenNames { get; set; } = [KeepPrimary];

    /// <summary>0 = keep the current primary monitor, n = virtual screen n (as numbered in the preview).</summary>
    [ObservableProperty]
    public partial int PrimaryScreenIndex { get; set; }

    private bool _rebuildingPrimaryNames;
    private int _primaryBeforeRebuild;

    partial void OnPrimaryScreenIndexChanged(int value)
    {
        if (_rebuildingPrimaryNames)
        {
            return;
        }

        LayoutChanged?.Invoke(this, EventArgs.Empty);
        AnchorPreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Screen centers of the current layout (x to the right, y down), in screen-number order.</summary>
    private List<(double X, double Y)> ScreenCenters() => IsGlassesAnchor
        ? [.. AnchorRects.Select(r => (r.X + r.Width / 2.0, r.Y + r.Height / 2.0))]
        : [.. Placements.OrderBy(p => p.ScreenId).Select(p => (-(double)p.YawDegrees, -(double)p.PitchDegrees))]; // positive yaw = left

    private void RebuildPrimaryScreenNames()
    {
        var centers = ScreenCenters();
        IReadOnlyList<string> names = [KeepPrimary, .. centers.Select((c, i) => $"Screen {i + 1} — {DescribePosition(centers, i)}")];
        if (names.SequenceEqual(PrimaryScreenNames))
        {
            return;
        }

        // A new list resets the ComboBox selection; put the chosen screen back (or "keep" when it no longer exists).
        _primaryBeforeRebuild = PrimaryScreenIndex;
        _rebuildingPrimaryNames = true;
        PrimaryScreenNames = names;
        _rebuildingPrimaryNames = false;
        PrimaryScreenIndex = _primaryBeforeRebuild < names.Count ? _primaryBeforeRebuild : 0;
        OnPropertyChanged(nameof(PrimaryScreenIndex));
    }

    /// <summary>"left", "middle", "top right", "only screen", … from where the screen sits among the others.</summary>
    private static string DescribePosition(List<(double X, double Y)> centers, int index)
    {
        if (centers.Count == 1)
        {
            return "the only screen";
        }

        static List<double> Distinct(IEnumerable<double> values) =>
            values.Order().Aggregate(new List<double>(), (list, v) =>
            {
                if (list.Count == 0 || v - list[^1] > 1)
                {
                    list.Add(v);
                }

                return list;
            });

        var columns = Distinct(centers.Select(c => c.X));
        var rows = Distinct(centers.Select(c => c.Y));
        int col = columns.FindLastIndex(x => centers[index].X - x >= -1);
        int row = rows.FindLastIndex(y => centers[index].Y - y >= -1);

        string horizontal = columns.Count switch
        {
            1 => string.Empty,
            2 => col == 0 ? "left" : "right",
            3 => col switch { 0 => "left", 1 => "middle", _ => "right" },
            _ => $"{col + 1}. from the left",
        };
        string vertical = rows.Count switch
        {
            1 => string.Empty,
            2 => row == 0 ? "top" : "bottom",
            _ => row switch { 0 => "top", 1 => "middle row", _ => "bottom" },
        };
        return string.Join(' ', new[] { vertical, horizontal }.Where(s => s.Length > 0));
    }

    /// <summary>Move app windows from the glasses display onto the workspace when it starts.</summary>
    [ObservableProperty]
    public partial bool MoveWindowsFromGlasses { get; set; } = true;

    /// <summary>Index into <see cref="UltrawideModes.All"/> (same order as the enum).</summary>
    [ObservableProperty]
    public partial int UltrawideModeIndex { get; set; }

    [ObservableProperty]
    public partial int PresetIndex { get; set; }

    [ObservableProperty]
    public partial double DistanceMeters { get; set; } = 1.5;

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    public IReadOnlyList<ScreenPlacement> Placements { get; private set; } = [];

    public UltrawideMode UltrawideMode => UltrawideModes.All[Math.Clamp(UltrawideModeIndex, 0, UltrawideModes.All.Count - 1)];

    partial void OnScreenCountChanged(double value)
    {
        if (value > MaxScreenCount)
        {
            ScreenCount = MaxScreenCount;
            return;
        }

        Recompute();
        if (IsGlassesAnchor)
        {
            OnAnchorSettingChanged();
        }
    }

    partial void OnUltrawideModeIndexChanged(int value) => Recompute();

    partial void OnPresetIndexChanged(int value) => Recompute();

    partial void OnDistanceMetersChanged(double value) => Recompute();

    private void Recompute()
    {
        int count = double.IsNaN(ScreenCount) ? 1 : (int)Math.Clamp(Math.Round(ScreenCount), 1, WorkspaceLayout.MaxScreens);
        var screens = Enumerable.Range(1, count).Select(i => new ScreenSpec(i, UltrawideMode)).ToList();
        var settings = new WorkspaceSettings
        {
            Preset = PresetIndex == 1 ? LayoutPreset.Grid : LayoutPreset.Arc,
            DistanceMeters = (float)Math.Clamp(DistanceMeters, 0.5, 5),
        };
        Placements = WorkspaceLayout.Compute(screens, settings);

        var res = UltrawideModes.GetResolution(UltrawideMode);
        float left = Placements.Max(p => p.YawDegrees + p.WidthMeters / p.DistanceMeters * 90f / MathF.PI);
        Summary = $"{count} × {res} ({UltrawideModes.GetDisplayName(UltrawideMode)}) · spans ±{left:F0}° · virtual monitors created in M2";
        RebuildPrimaryScreenNames();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}

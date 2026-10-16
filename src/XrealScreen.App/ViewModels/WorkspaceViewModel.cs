using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using XrealScreen.Core.Workspace;
using XrealScreen.Device.XrealOne;
using XrealScreen.Host;

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
        "Glasses anchor — most stable. Glasses menu: Anchor mode + UltraWide 32:9, 16:18 or 21:9. 1–3 screens, or up to 6 in a grid.",
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
        OnPropertyChanged(nameof(ShowAnchorChoices));
        OnPropertyChanged(nameof(ShowScreenStepper));
        if (ScreenCount > MaxScreenCount)
        {
            ScreenCount = MaxScreenCount; // refreshes the preview too
            return;
        }

        RefreshGlassesSignal();
        Recompute();
    }

    /// <summary>Glasses anchor uses 3 screens in a row, or 6 when the layout is a grid. Head tracking allows 6.</summary>
    public double MaxScreenCount => IsGlassesAnchor
        ? (PresetIndex == 1 ? AnchorSplit.MaxGridScreens : AnchorSplit.MaxScreens)
        : WorkspaceLayout.MaxScreens;

    /// <summary>The 1 / 2 / 3 choice. Hidden while a grid can add a second row.</summary>
    public bool ShowAnchorChoices => IsGlassesAnchor && PresetIndex != 1;

    /// <summary>Plus and minus, and the screen-count box. Head tracking, and glasses anchor while Grid is selected.</summary>
    public bool ShowScreenStepper => IsAppTracking || (IsGlassesAnchor && PresetIndex == 1);

    public bool IsGlassesAnchor => KindIndex == 0;

    public IReadOnlyList<string> AnchorAspectNames { get; } = ["16:9", "16:10", "Full image (recommended)"];

    /// <summary>Index = (int)AnchorAspect. 2 = Fill, the whole glasses image.</summary>
    [ObservableProperty]
    public partial int AnchorAspectIndex { get; set; } = 2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnchorGapText))]
    public partial double AnchorGapPixels { get; set; } = 16;

    public string AnchorGapText => $"{Math.Round(AnchorGapPixels):F0} px";

    /// <summary>Fraction of the glasses width the screens use. Half keeps them clear of the sides.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnchorWidthText))]
    public partial double AnchorWidth { get; set; } = AnchorSplit.DefaultWidthScale;

    public string AnchorWidthText => $"{Math.Round(AnchorWidth * 100):F0}%";

    partial void OnAnchorWidthChanged(double value) => OnAnchorSettingChanged();

    /// <summary>Fraction of the slot height the screens use. 1 fills the glasses image.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnchorHeightText))]
    public partial double AnchorHeight { get; set; } = AnchorSplit.DefaultHeightScale;

    public string AnchorHeightText => $"{Math.Round(AnchorHeight * 100):F0}%";

    partial void OnAnchorHeightChanged(double value) => OnAnchorSettingChanged();

    /// <summary>Clear pixels around the whole group. The gap between screens is separate.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnchorMarginText))]
    public partial double AnchorMarginPixels { get; set; }

    public string AnchorMarginText => $"{Math.Round(AnchorMarginPixels):F0} px";

    partial void OnAnchorMarginPixelsChanged(double value) => OnAnchorSettingChanged();

    public XrealScreen.Core.Workspace.AnchorAspect AnchorAspect => (XrealScreen.Core.Workspace.AnchorAspect)Math.Clamp(AnchorAspectIndex, 0, 2);

    /// <summary>Index matches <see cref="FrameStyle"/>.</summary>
    [ObservableProperty]
    public partial int FrameStyleIndex { get; set; } = (int)FrameStyle.Linear;

    public FrameStyle FrameStyle => (FrameStyle)Math.Clamp(FrameStyleIndex, 0, 5);

    public bool AnchorGradient => FrameStyle != FrameStyle.SeeThrough;

    public bool FrameUsesColors => FrameStyle != FrameStyle.SeeThrough;

    public bool FrameUsesSecondColor => FrameStyle is FrameStyle.Linear or FrameStyle.Vertical or FrameStyle.Vignette;

    public string FirstColorLabel => FrameStyle switch
    {
        FrameStyle.Solid => "Color",
        FrameStyle.Glow => "Glow color",
        FrameStyle.Vignette => "Edge color",
        FrameStyle.Vertical => "Top color",
        _ => "Left color",
    };

    public string SecondColorLabel => FrameStyle switch
    {
        FrameStyle.Vignette => "Center color",
        FrameStyle.Vertical => "Bottom color",
        _ => "Right color",
    };

    public string FrameStyleHelp => FrameStyle switch
    {
        FrameStyle.SeeThrough => "Each screen stays a rounded desktop with clear space around it. Apply restarts a running workspace so the screens can grow into that space.",
        FrameStyle.Vertical => "Each screen runs its own color from top to bottom. A running workspace picks this up immediately.",
        FrameStyle.Solid => "Each screen has its own flat color in the border and the gaps beside it.",
        FrameStyle.Vignette => "Each screen has its own edge color and center color.",
        FrameStyle.Glow => "Each screen has its own colored rim. The middle of that screen's gap stays clear.",
        _ => "Each screen floats on a soft shadow with rounded corners. Colors run left to right, and together they are one sweep. A running workspace picks up a color change immediately.",
    };

    partial void OnFrameStyleIndexChanged(int value)
    {
        OnPropertyChanged(nameof(FrameStyle));
        OnPropertyChanged(nameof(AnchorGradient));
        OnPropertyChanged(nameof(FrameUsesColors));
        OnPropertyChanged(nameof(FrameUsesSecondColor));
        OnPropertyChanged(nameof(FirstColorLabel));
        OnPropertyChanged(nameof(SecondColorLabel));
        OnPropertyChanged(nameof(FrameStyleHelp));
        if (_loading)
        {
            return;
        }

        if (!_screenColorsCustom)
        {
            FillDefaultScreenColors();
        }

        RefreshGlassesSignal();
    }

    /// <summary>Glasses brightness 0–9, sent when the slider settles. A double so the slider can bind to it.</summary>
    [ObservableProperty]
    public partial double GlassesBrightness { get; set; } = 5;

    /// <summary>0 clear, 1 middle, 2 dark.</summary>
    [ObservableProperty]
    public partial double GlassesDimmer { get; set; }

    public string GlassesDimmerText => (int)Math.Round(GlassesDimmer) switch { 0 => "Clear", 2 => "Dark", _ => "Middle" };

    [ObservableProperty]
    public partial string GlassesCommandStatus { get; set; } = "Brightness and dimmer are sent to the glasses over the USB link.";

    private bool _suspendGlasses;
    private int _committedBrightness = 5;
    private int _committedDimmer;
    private CancellationTokenSource? _glassesSend;
    private bool _appliedInset = true;
    private bool _loading;
    private bool _fillingColors;
    private bool _screenColorsCustom;

    partial void OnGlassesBrightnessChanged(double value) => QueueGlassesSend();

    partial void OnGlassesDimmerChanged(double value)
    {
        OnPropertyChanged(nameof(GlassesDimmerText));
        QueueGlassesSend();
    }

    /// <summary>Loads saved glasses levels without sending them. Null keeps brightness 5 and a clear dimmer.</summary>
    public void ApplyGlasses(int? brightness, int? dimmer)
    {
        _suspendGlasses = true;
        GlassesBrightness = Math.Clamp(brightness ?? 5, 0, 9);
        GlassesDimmer = Math.Clamp(dimmer ?? 0, 0, 2);
        _committedBrightness = (int)Math.Round(GlassesBrightness);
        _committedDimmer = (int)Math.Round(GlassesDimmer);
        _suspendGlasses = false;
    }

    private void QueueGlassesSend()
    {
        if (_suspendGlasses)
        {
            return;
        }

        int brightness = (int)Math.Round(Math.Clamp(GlassesBrightness, 0, 9));
        int dimmer = (int)Math.Round(Math.Clamp(GlassesDimmer, 0, 2));
        if (brightness == _committedBrightness && dimmer == _committedDimmer)
        {
            _glassesSend?.Cancel();
            return;
        }

        _glassesSend?.Cancel();
        var send = new CancellationTokenSource();
        _glassesSend = send;
        _ = SendGlassesAsync(brightness, dimmer, send.Token);
    }

    private async Task SendGlassesAsync(int brightness, int dimmer, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken).ConfigureAwait(true);
            var endpoints = OneEndpoints.Default;
            await using var control = await OneControlClient.ConnectAsync(endpoints.Host, endpoints.ControlPort, TimeSpan.FromSeconds(1.5), cancellationToken).ConfigureAwait(true);
            await control.SetBrightnessAsync(brightness, cancellationToken).ConfigureAwait(true);
            await control.SetDimmerAsync(dimmer, cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _committedBrightness = brightness;
            _committedDimmer = dimmer;
            GlassesCommandStatus = $"Sent brightness {brightness} and dimmer {(dimmer switch { 0 => "clear", 2 => "dark", _ => "middle" })}.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Net.Sockets.SocketException or OperationCanceledException)
        {
            GlassesCommandStatus = "Data link not reachable, so brightness and dimmer stayed on the glasses. Unplug them, plug them in, then move the slider again.";
        }
    }

    private Color _gradientLeft = Color.FromArgb(255, 0x1F, 0x0A, 0x6B);

    private Color _gradientRight = Color.FromArgb(255, 0x00, 0x8C, 0xB8);

    public Color GradientLeft
    {
        get => _gradientLeft;
        set => SetGradient(ref _gradientLeft, value, nameof(GradientLeft), nameof(GradientLeftBrush));
    }

    public Color GradientRight
    {
        get => _gradientRight;
        set => SetGradient(ref _gradientRight, value, nameof(GradientRight), nameof(GradientRightBrush));
    }

    public SolidColorBrush GradientLeftBrush => new(GradientLeft);

    public SolidColorBrush GradientRightBrush => new(GradientRight);

    public string GradientLeftHex => RgbHex.Format(GradientLeft.R, GradientLeft.G, GradientLeft.B);

    public string GradientRightHex => RgbHex.Format(GradientRight.R, GradientRight.G, GradientRight.B);

    /// <summary>Applies saved colors. Unknown text keeps the current color.</summary>
    public void SetGradientHex(string? left, string? right)
    {
        if (RgbHex.TryParse(left, out byte lr, out byte lg, out byte lb))
        {
            GradientLeft = Color.FromArgb(255, lr, lg, lb);
        }

        if (RgbHex.TryParse(right, out byte rr, out byte rg, out byte rb))
        {
            GradientRight = Color.FromArgb(255, rr, rg, rb);
        }
    }

    /// <summary>Hides the per-screen edits while saved settings are copied in.</summary>
    public void BeginSettingsLoad() => _loading = true;

    /// <summary>Saved settings are in place. Later edits update the running frame.</summary>
    public void EndSettingsLoad()
    {
        _loading = false;
        NotifyLookSelection();
    }

    public bool ShowScreen2 => AnchorScreenCount >= 2;

    public bool ShowScreen3 => AnchorScreenCount >= 3;

    private Color _screen1A = Color.FromArgb(255, 0x1F, 0x0A, 0x6B);
    private Color _screen1B = Color.FromArgb(255, 0x00, 0x8C, 0xB8);
    private Color _screen2A = Color.FromArgb(255, 0x1F, 0x0A, 0x6B);
    private Color _screen2B = Color.FromArgb(255, 0x00, 0x8C, 0xB8);
    private Color _screen3A = Color.FromArgb(255, 0x1F, 0x0A, 0x6B);
    private Color _screen3B = Color.FromArgb(255, 0x00, 0x8C, 0xB8);

    public Color Screen1ColorA { get => _screen1A; set => SetScreen(ref _screen1A, value, nameof(Screen1ColorA), nameof(Screen1BrushA)); }
    public Color Screen1ColorB { get => _screen1B; set => SetScreen(ref _screen1B, value, nameof(Screen1ColorB), nameof(Screen1BrushB)); }
    public Color Screen2ColorA { get => _screen2A; set => SetScreen(ref _screen2A, value, nameof(Screen2ColorA), nameof(Screen2BrushA)); }
    public Color Screen2ColorB { get => _screen2B; set => SetScreen(ref _screen2B, value, nameof(Screen2ColorB), nameof(Screen2BrushB)); }
    public Color Screen3ColorA { get => _screen3A; set => SetScreen(ref _screen3A, value, nameof(Screen3ColorA), nameof(Screen3BrushA)); }
    public Color Screen3ColorB { get => _screen3B; set => SetScreen(ref _screen3B, value, nameof(Screen3ColorB), nameof(Screen3BrushB)); }

    public SolidColorBrush Screen1BrushA => new(Screen1ColorA);
    public SolidColorBrush Screen1BrushB => new(Screen1ColorB);
    public SolidColorBrush Screen2BrushA => new(Screen2ColorA);
    public SolidColorBrush Screen2BrushB => new(Screen2ColorB);
    public SolidColorBrush Screen3BrushA => new(Screen3ColorA);
    public SolidColorBrush Screen3BrushB => new(Screen3ColorB);

    public string Screen1HexA => Hex(Screen1ColorA);

    public string Screen1HexB => Hex(Screen1ColorB);

    public string Screen2HexA => Hex(Screen2ColorA);

    public string Screen2HexB => Hex(Screen2ColorB);

    public string Screen3HexA => Hex(Screen3ColorA);

    public string Screen3HexB => Hex(Screen3ColorB);

    /// <summary>Null when the screens still share the saved pair. Otherwise six <c>#RRGGBB</c> values.</summary>
    public string?[]? SavedScreenColors() => _screenColorsCustom
        ? [Hex(Screen1ColorA), Hex(Screen1ColorB), Hex(Screen2ColorA), Hex(Screen2ColorB), Hex(Screen3ColorA), Hex(Screen3ColorB)]
        : null;

    /// <summary>Loads per-screen colors. Missing colors keep the shared pair, sliced when the frame is a left-to-right gradient.</summary>
    public void ApplyScreenColors(string?[]? stored)
    {
        _fillingColors = true;
        _screenColorsCustom = ScreenFrameColors.HasAny(stored);
        AssignSlots(FrameStyle == FrameStyle.Linear && !_screenColorsCustom);
        if (_screenColorsCustom)
        {
            Span<(byte R, byte G, byte B)> slots = stackalloc (byte R, byte G, byte B)[ScreenFrameColors.SlotCount];
            ReadSlots(slots);
            ScreenFrameColors.Overlay(slots, stored);
            Assign(slots);
            if (ScreenColorsMatchDefaults())
            {
                _screenColorsCustom = false;
            }
        }

        _fillingColors = false;
    }

    /// <summary>Applies a pictured setup. Clear removes the frame. The others set the style and both colors.</summary>
    public void ApplyLook(string name)
    {
        if (!TryFindLook(name, out LookSetup look))
        {
            return;
        }

        _screenColorsCustom = false;
        if (look.Style != FrameStyle.SeeThrough)
        {
            _gradientLeft = Color.FromArgb(255, look.R1, look.G1, look.B1);
            _gradientRight = Color.FromArgb(255, look.R2, look.G2, look.B2);
            OnPropertyChanged(nameof(GradientLeft));
            OnPropertyChanged(nameof(GradientLeftBrush));
            OnPropertyChanged(nameof(GradientRight));
            OnPropertyChanged(nameof(GradientRightBrush));
        }

        if (FrameStyleIndex != (int)look.Style)
        {
            FrameStyleIndex = (int)look.Style;
        }
        else
        {
            RefreshGlassesSignal();
        }
    }

    public string SelectedLookCaption => SelectedLookName is { } name ? $"Selected: {name}" : "Selected: Custom";

    public bool IndigoSelected => SelectedLookName == "Indigo";

    public bool ClearSelected => SelectedLookName == "Clear";

    public bool NightSelected => SelectedLookName == "Night";

    public bool SunsetSelected => SelectedLookName == "Sunset";

    public bool SeaSelected => SelectedLookName == "Sea";

    public bool GlowSelected => SelectedLookName == "Glow";

    private string? SelectedLookName
    {
        get
        {
            if (_screenColorsCustom)
            {
                return null;
            }

            foreach (var look in Looks)
            {
                if ((int)look.Style != FrameStyleIndex)
                {
                    continue;
                }

                if (look.Style == FrameStyle.SeeThrough || SameColor(GradientLeft, look) && SameSecond(GradientRight, look))
                {
                    return look.Name;
                }
            }

            return null;
        }
    }

    private readonly record struct LookSetup(string Name, FrameStyle Style, byte R1, byte G1, byte B1, byte R2, byte G2, byte B2);

    private static readonly LookSetup[] Looks =
    [
        new("Indigo", FrameStyle.Linear, 0x1F, 0x0A, 0x6B, 0x00, 0x8C, 0xB8),
        new("Clear", FrameStyle.SeeThrough, 0, 0, 0, 0, 0, 0),
        new("Night", FrameStyle.Solid, 0x1B, 0x1F, 0x2A, 0x1B, 0x1F, 0x2A),
        new("Sunset", FrameStyle.Linear, 0xE8, 0x5D, 0x04, 0x7B, 0x2C, 0xBF),
        new("Sea", FrameStyle.Vertical, 0x04, 0x2F, 0x40, 0x48, 0xCA, 0xE4),
        new("Glow", FrameStyle.Glow, 0x7B, 0x61, 0xFF, 0x7B, 0x61, 0xFF),
    ];

    private static bool TryFindLook(string name, out LookSetup look)
    {
        foreach (var candidate in Looks)
        {
            if (candidate.Name == name)
            {
                look = candidate;
                return true;
            }
        }

        look = default;
        return false;
    }

    private static bool SameColor(Color color, LookSetup look) => color.R == look.R1 && color.G == look.G1 && color.B == look.B1;

    private static bool SameSecond(Color color, LookSetup look) => color.R == look.R2 && color.G == look.G2 && color.B == look.B2;

    private bool ScreenColorsMatchDefaults()
    {
        Span<(byte R, byte G, byte B)> expected = stackalloc (byte R, byte G, byte B)[ScreenFrameColors.SlotCount];
        Span<(byte R, byte G, byte B)> actual = stackalloc (byte R, byte G, byte B)[ScreenFrameColors.SlotCount];
        ScreenFrameColors.WriteDefaults(expected, (_gradientLeft.R, _gradientLeft.G, _gradientLeft.B), (_gradientRight.R, _gradientRight.G, _gradientRight.B), AnchorScreenCount, FrameStyle == FrameStyle.Linear);
        ReadSlots(actual);
        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i])
            {
                return false;
            }
        }

        return true;
    }

    private void NotifyLookSelection()
    {
        OnPropertyChanged(nameof(SelectedLookCaption));
        OnPropertyChanged(nameof(IndigoSelected));
        OnPropertyChanged(nameof(ClearSelected));
        OnPropertyChanged(nameof(NightSelected));
        OnPropertyChanged(nameof(SunsetSelected));
        OnPropertyChanged(nameof(SeaSelected));
        OnPropertyChanged(nameof(GlowSelected));
    }

    private void FillDefaultScreenColors()
    {
        _fillingColors = true;
        AssignSlots(FrameStyle == FrameStyle.Linear);
        _fillingColors = false;
    }

    private void AssignSlots(bool slice)
    {
        Span<(byte R, byte G, byte B)> slots = stackalloc (byte R, byte G, byte B)[ScreenFrameColors.SlotCount];
        ScreenFrameColors.WriteDefaults(slots, (_gradientLeft.R, _gradientLeft.G, _gradientLeft.B), (_gradientRight.R, _gradientRight.G, _gradientRight.B), AnchorScreenCount, slice);
        Assign(slots);
    }

    private void ReadSlots(Span<(byte R, byte G, byte B)> slots)
    {
        slots[0] = (_screen1A.R, _screen1A.G, _screen1A.B);
        slots[1] = (_screen1B.R, _screen1B.G, _screen1B.B);
        slots[2] = (_screen2A.R, _screen2A.G, _screen2A.B);
        slots[3] = (_screen2B.R, _screen2B.G, _screen2B.B);
        slots[4] = (_screen3A.R, _screen3A.G, _screen3A.B);
        slots[5] = (_screen3B.R, _screen3B.G, _screen3B.B);
    }

    private void Assign(ReadOnlySpan<(byte R, byte G, byte B)> slots)
    {
        Screen1ColorA = From(slots[0]);
        Screen1ColorB = From(slots[1]);
        Screen2ColorA = From(slots[2]);
        Screen2ColorB = From(slots[3]);
        Screen3ColorA = From(slots[4]);
        Screen3ColorB = From(slots[5]);
    }

    private static Color From((byte R, byte G, byte B) color) => Color.FromArgb(255, color.R, color.G, color.B);

    private static string Hex(Color color) => RgbHex.Format(color.R, color.G, color.B);

    private void SetScreen(ref Color field, Color value, string colorName, string brushName)
    {
        if (field.Equals(value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(colorName);
        OnPropertyChanged(brushName);
        OnPropertyChanged(colorName.Replace("Color", "Hex", StringComparison.Ordinal));
        if (_loading || _fillingColors)
        {
            return;
        }

        _screenColorsCustom = true;
        OnFrameLookChanged();
    }

    private void SetGradient(ref Color field, Color value, string colorName, string brushName)
    {
        if (field.Equals(value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(colorName);
        OnPropertyChanged(brushName);
        if (!_loading && !_screenColorsCustom)
        {
            FillDefaultScreenColors();
        }

        if (!_loading)
        {
            OnFrameLookChanged();
        }
    }

    private void OnFrameLookChanged()
    {
        NotifyLookSelection();
        FrameLookChanged?.Invoke(this, EventArgs.Empty);
        AnchorPreviewChanged?.Invoke(this, EventArgs.Empty);
        if ((FrameStyle != FrameStyle.SeeThrough) != _appliedInset)
        {
            HasPendingScreenChanges = true;
        }
    }

    public event EventHandler? FrameLookChanged;

    public void WriteLive(LiveBackdrop live)
    {
        ArgumentNullException.ThrowIfNull(live);
        var rgb = new byte[18];
        WriteRgb(Screen1ColorA, rgb, 0);
        WriteRgb(Screen1ColorB, rgb, 3);
        WriteRgb(Screen2ColorA, rgb, 6);
        WriteRgb(Screen2ColorB, rgb, 9);
        WriteRgb(Screen3ColorA, rgb, 12);
        WriteRgb(Screen3ColorB, rgb, 15);
        int count = AnchorScreenCount;
        bool stacked = AnchorCanvas.Height > AnchorCanvas.Width;
        float split1 = count >= 2 ? 1f / count : 1f;
        float split2 = count >= 3 ? 2f / count : 1f;
        if (AnchorRects.Count >= 2 && AnchorCanvas.Width > 0 && AnchorCanvas.Height > 0)
        {
            (split1, split2, stacked) = AnchorSplit.Bands(AnchorCanvas.Width, AnchorCanvas.Height, AnchorRects);
        }

        live.Update(FrameStyle, count, stacked, split1, split2, rgb);
    }

    private static void WriteRgb(Color color, byte[] rgb, int offset)
    {
        rgb[offset] = color.R;
        rgb[offset + 1] = color.G;
        rgb[offset + 2] = color.B;
    }

    private int AnchorScreenCount => double.IsNaN(ScreenCount) ? 1 : (int)Math.Clamp(Math.Round(ScreenCount), 1, AnchorSplit.MaxScreens);

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
        Recompute();
    }

    public void MarkScreenSettingsApplied()
    {
        HasPendingScreenChanges = false;
        _appliedInset = FrameStyle != FrameStyle.SeeThrough;
    }

    /// <summary>What the glasses send right now (glasses-anchor mode), e.g. "32:9 (3840×1080) → 2 screens".</summary>
    [ObservableProperty]
    public partial string GlassesSignal { get; set; } = string.Empty;

    public void RefreshGlassesSignal()
    {
        int columns = PresetIndex == 1 ? WorkspaceLayout.GridColumns : 0;
        int limit = columns > 1 ? AnchorSplit.MaxGridScreens : AnchorSplit.MaxScreens;
        int count = double.IsNaN(ScreenCount) ? 1 : (int)Math.Clamp(Math.Round(ScreenCount), 1, limit);
        int gap = (int)Math.Round(AnchorGapPixels);
        double width = AnchorSplit.NormalizeWidthScale(AnchorWidth);
        double height = AnchorSplit.NormalizeHeightScale(AnchorHeight);
        int margin = AnchorSplit.NormalizeMarginPixels((int)Math.Round(AnchorMarginPixels));
        GlassesSignal = XrealScreen.Host.GlassesSignal.Describe(count, gap, AnchorAspect, width, columns, height, margin);

        var current = XrealScreen.Host.GlassesSignal.CurrentResolution();
        AnchorCanvasIsExample = current is not { } r || !XrealScreen.Core.Workspace.AnchorSplit.IsUltrawideSignal(r);
        AnchorCanvas = AnchorCanvasIsExample ? new XrealScreen.Core.Workspace.Resolution(3840, 1080) : current!.Value;
        int framePx = FrameStyle == FrameStyle.SeeThrough ? 0 : FrameStyles.FramePixels;
        AnchorRects = AnchorSplit.Place(AnchorCanvas, count, gap, AnchorAspect, framePx, width, columns, height, margin);
        if (!_loading && !_screenColorsCustom)
        {
            FillDefaultScreenColors();
        }

        OnPropertyChanged(nameof(ShowScreen2));
        OnPropertyChanged(nameof(ShowScreen3));
        RebuildPrimaryScreenNames();
        if (_loading)
        {
            AnchorPreviewChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            OnFrameLookChanged();
        }
    }

    [ObservableProperty]
    public partial double ScreenCount { get; set; } = 3;

    /// <summary>0 = 1 screen, 1 = 2 screens, 2 = 3 screens. Glasses anchor offers only these three.</summary>
    public int ScreenCountIndex
    {
        get
        {
            int count = double.IsNaN(ScreenCount) ? 1 : (int)Math.Round(ScreenCount);
            return Math.Clamp(count, 1, AnchorSplit.MaxScreens) - 1;
        }
        set => ScreenCount = Math.Clamp(value, 0, AnchorSplit.MaxScreens - 1) + 1;
    }

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

    /// <summary>Pixel size of each head-tracked screen, in screen order. Empty in glasses anchor.</summary>
    public IReadOnlyList<Resolution> HeadResolutions { get; private set; } = [];

    /// <summary>Native glasses picture head tracking fills. UltraWide Off, 1920×1200.</summary>
    public static Resolution HeadPicture { get; } = new(1920, 1200);

    public string HeadPixelSize(int screenId)
    {
        int index = screenId - 1;
        if ((uint)index < (uint)HeadResolutions.Count)
        {
            Resolution size = HeadResolutions[index];
            return $"{size.Width} × {size.Height}";
        }

        Resolution fallback = UltrawideModes.GetResolution(UltrawideMode);
        return $"{fallback.Width} × {fallback.Height}";
    }

    public UltrawideMode UltrawideMode => UltrawideModes.All[Math.Clamp(UltrawideModeIndex, 0, UltrawideModes.All.Count - 1)];

    /// <summary>Last screen count that was accepted. Used so only the step from 3 to 4 selects the grid.</summary>
    private int _committedScreenCount;

    partial void OnScreenCountChanged(double value)
    {
        if (value > MaxScreenCount)
        {
            ScreenCount = MaxScreenCount;
            return;
        }

        int count = double.IsNaN(value) ? 1 : (int)Math.Round(value);
        var preset = WorkspaceLayout.PresetForCount(_committedScreenCount, count, PresetIndex == 1 ? LayoutPreset.Grid : LayoutPreset.Arc);
        _committedScreenCount = count;
        if (!_loading && preset == LayoutPreset.Grid && PresetIndex != 1)
        {
            PresetIndex = 1; // OnPresetIndexChanged recomputes
        }
        else
        {
            Recompute();
        }

        OnPropertyChanged(nameof(ScreenCountIndex));
        if (IsGlassesAnchor)
        {
            OnAnchorSettingChanged();
        }
    }

    partial void OnUltrawideModeIndexChanged(int value)
    {
        Recompute();
        if (!_loading && IsAppTracking)
        {
            HasPendingScreenChanges = true;
        }
    }

    partial void OnPresetIndexChanged(int value)
    {
        OnPropertyChanged(nameof(MaxScreenCount));
        OnPropertyChanged(nameof(ShowAnchorChoices));
        OnPropertyChanged(nameof(ShowScreenStepper));
        if (ScreenCount > MaxScreenCount)
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

    partial void OnDistanceMetersChanged(double value) => Recompute();

    private void Recompute()
    {
        int count = double.IsNaN(ScreenCount) ? 1 : (int)Math.Clamp(Math.Round(ScreenCount), 1, WorkspaceLayout.MaxScreens);
        var preset = PresetIndex == 1 ? LayoutPreset.Grid : LayoutPreset.Arc;
        if (!IsGlassesAnchor)
        {
            var fitted = WorkspaceLayout.FitView(
                HeadPicture,
                count,
                preset,
                AnchorSplit.NormalizeWidthScale(AnchorWidth),
                AnchorSplit.NormalizeHeightScale(AnchorHeight),
                (int)Math.Round(AnchorGapPixels),
                (int)Math.Round(AnchorMarginPixels),
                (float)Math.Clamp(DistanceMeters, 0.5, 5),
                UltrawideMode);
            Placements = [.. fitted.Select(s => s.Placement)];
            HeadResolutions = [.. fitted.Select(s => new Resolution(s.Pixels.Width, s.Pixels.Height))];
            int rows = preset == LayoutPreset.Grid && count > WorkspaceLayout.GridColumns
                ? (count + WorkspaceLayout.GridColumns - 1) / WorkspaceLayout.GridColumns
                : 1;
            string arrangement = rows > 1
                ? $"grid {WorkspaceLayout.GridColumns} × {rows}"
                : preset == LayoutPreset.Grid
                    ? "one row — a fourth screen starts the row below"
                    : "one row";
            Resolution size = HeadResolutions[0];
            int widthPercent = (int)Math.Round(AnchorSplit.NormalizeWidthScale(AnchorWidth) * 100);
            int heightPercent = (int)Math.Round(AnchorSplit.NormalizeHeightScale(AnchorHeight) * 100);
            Summary = $"{count} × {size.Width}×{size.Height} · {arrangement} · {widthPercent}% of the glasses width, {heightPercent}% of the height";
        }
        else
        {
            HeadResolutions = [];
            var screens = Enumerable.Range(1, count).Select(i => new ScreenSpec(i, UltrawideMode)).ToList();
            var settings = new WorkspaceSettings
            {
                Preset = preset,
                DistanceMeters = (float)Math.Clamp(DistanceMeters, 0.5, 5),
            };
            Placements = WorkspaceLayout.Compute(screens, settings);
            var res = UltrawideModes.GetResolution(UltrawideMode);
            float left = Placements.Max(p => p.YawDegrees + p.WidthMeters / p.DistanceMeters * 90f / MathF.PI);
            int rows = (count + WorkspaceLayout.GridColumns - 1) / WorkspaceLayout.GridColumns;
            string arrangement = settings.Preset == LayoutPreset.Grid && count > WorkspaceLayout.GridColumns
                ? $"grid {WorkspaceLayout.GridColumns} × {rows}"
                : settings.Preset == LayoutPreset.Grid
                    ? "one row — a fourth screen starts the row below"
                    : "one row";
            Summary = $"{count} × {res} ({UltrawideModes.GetDisplayName(UltrawideMode)}) · {arrangement} · spans ±{left:F0}°";
        }

        RebuildPrimaryScreenNames();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}

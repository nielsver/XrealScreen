using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using XrealScreen.Core.Gaming;
using XrealScreen.Sdk;

namespace XrealScreen.App.ViewModels;

/// <summary>
/// Games page: head look for games (ADR-0011). Runs <see cref="HeadLookEngine"/> on the glasses' IMU,
/// optionally with the glasses as primary monitor. Settings live in <see cref="AppPreferences.HeadLook"/>.
/// Cannot run together with a workspace (both use the IMU and the layout snapshot).
/// </summary>
public sealed partial class HeadLookViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly DispatcherQueueTimer _saveTimer;
    private HeadLookEngine? _engine;
    private GlassesPrimarySession? _primary;
    private CancellationTokenSource? _cts;
    private Task? _run;
    private bool _loading;
    private volatile XrealGlasses? _activeGlasses;

    public HeadLookViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _statusTimer = dispatcher.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(250);
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _saveTimer = dispatcher.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => App.Preferences.Save();

        _loading = true;
        var s = App.Preferences.HeadLook;
        ModeIndex = s.Mode == HeadMouseMode.Stick ? 1 : 0;
        Sensitivity = s.CountsPerDegree;
        VerticalScale = s.VerticalScale;
        InvertY = s.InvertY;
        Deadband = s.DeadbandDegreesPerSecond;
        StickDeadZone = s.StickDeadZoneDegrees;
        StickFull = s.StickFullDegrees;
        StickSpeed = s.StickMaxCountsPerSecond;
        GlassesPrimary = App.Preferences.HeadLookGlassesPrimary;
        _loading = false;
    }

    public IReadOnlyList<string> ModeNames { get; } =
    [
        "Look: turning your head turns the camera the same amount",
        "Joystick: turn your head away from center to keep turning, faster the further you turn",
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStick), nameof(IsLook))]
    public partial int ModeIndex { get; set; }

    public bool IsStick => ModeIndex == 1;

    public bool IsLook => !IsStick;

    /// <summary>Look: mouse counts per degree of head rotation.</summary>
    [ObservableProperty]
    public partial double Sensitivity { get; set; }

    [ObservableProperty]
    public partial double VerticalScale { get; set; }

    [ObservableProperty]
    public partial bool InvertY { get; set; }

    [ObservableProperty]
    public partial double Deadband { get; set; }

    [ObservableProperty]
    public partial double StickDeadZone { get; set; }

    [ObservableProperty]
    public partial double StickFull { get; set; }

    [ObservableProperty]
    public partial double StickSpeed { get; set; }

    /// <summary>Make the glasses the primary monitor while head look runs, so games open there.</summary>
    [ObservableProperty]
    public partial bool GlassesPrimary { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStopped))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(ToggleCommand), nameof(RecenterCommand))]
    public partial bool IsRunning { get; set; }

    public bool IsStopped => !IsRunning;

    /// <summary>Mouse output on (Ctrl+Alt+H).</summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Stopped.";

    [ObservableProperty]
    public partial string LastMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Pose { get; set; } = string.Empty;

    public string ToggleText => IsOn ? "Turn head look off" : "Turn head look on";

    partial void OnIsOnChanged(bool value) => OnPropertyChanged(nameof(ToggleText));

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading || e.PropertyName is not (nameof(ModeIndex) or nameof(Sensitivity) or nameof(VerticalScale) or nameof(InvertY)
            or nameof(Deadband) or nameof(StickDeadZone) or nameof(StickFull) or nameof(StickSpeed) or nameof(GlassesPrimary)))
        {
            return;
        }

        App.Preferences.HeadLook = CurrentSettings();
        App.Preferences.HeadLookGlassesPrimary = GlassesPrimary;
        if (_engine is { } engine)
        {
            engine.Settings = App.Preferences.HeadLook;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private HeadMouseSettings CurrentSettings() => new()
    {
        Mode = IsStick ? HeadMouseMode.Stick : HeadMouseMode.Look,
        CountsPerDegree = (float)Sensitivity,
        VerticalScale = (float)VerticalScale,
        InvertY = InvertY,
        DeadbandDegreesPerSecond = (float)Deadband,
        StickDeadZoneDegrees = (float)StickDeadZone,
        StickFullDegrees = (float)Math.Max(StickFull, StickDeadZone + 1),
        StickMaxCountsPerSecond = (float)StickSpeed,
    };

    [RelayCommand(CanExecute = nameof(IsStopped))]
    private async Task StartAsync()
    {
        if (!App.Session.IsIdle)
        {
            LastMessage = "Stop the workspace first: head look and a workspace cannot run at the same time.";
            return;
        }

        if (App.Tracking.IsRunning)
        {
            await App.Tracking.StopCommand.ExecuteAsync(null).ConfigureAwait(true);
        }

        IsRunning = true;
        LastMessage = string.Empty;
        if (GlassesPrimary)
        {
            try
            {
                _primary = await GlassesPrimarySession.StartAsync(CancellationToken.None).ConfigureAwait(true);
                LastMessage = _primary is null ? "The glasses are already the primary monitor." : "The glasses are now the primary monitor. Your layout comes back when you stop.";
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                LastMessage = $"Primary monitor not changed: {ex.Message}";
            }
        }

        var engine = new HeadLookEngine(new XrealGlasses()) { Settings = CurrentSettings() };
        engine.Message += message => _dispatcher.TryEnqueue(() => LastMessage = message);
        engine.EnabledChanged += on => _dispatcher.TryEnqueue(() => IsOn = on);
        _engine = engine;
        _activeGlasses = engine.Glasses;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _run = Task.Run(() => engine.RunAsync(token), token);
        _statusTimer.Start();
        RefreshStatus();
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task StopAsync()
    {
        _statusTimer.Stop();
        if (_cts is { } cts)
        {
            await cts.CancelAsync().ConfigureAwait(true);
        }

        if (_run is { } run)
        {
            try
            {
                await run.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts?.Dispose();
        _cts = null;
        _run = null;
        _engine = null;
        _activeGlasses = null;
        LastMessage = string.Empty;
        await RestorePrimaryAsync().ConfigureAwait(true);
        IsOn = false;
        IsRunning = false;
        Pose = string.Empty;
        Status = "Stopped.";
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Toggle()
    {
        if (_engine is { } engine)
        {
            engine.Enabled = !engine.Enabled;
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Recenter() => _engine?.Recenter();

    private async Task RestorePrimaryAsync()
    {
        if (_primary is { } primary)
        {
            _primary = null;
            try
            {
                await primary.DisposeAsync().ConfigureAwait(true);
                LastMessage = "Monitor layout restored.";
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                LastMessage = $"Could not restore the monitor layout: {ex.Message}. It is restored the next time XrealScreen starts.";
            }
        }
    }

    private void RefreshStatus()
    {
        if (_engine is not { } engine)
        {
            return;
        }

        Status = !engine.IsConnected
            ? "Waiting for the glasses…"
            : engine.Enabled
                ? $"Head look is ON. Mouse moves sent: {engine.Sends}{(engine.Rejected > 0 ? $", refused: {engine.Rejected} (is the game running as administrator?)" : string.Empty)}"
                : "Connected. Head look is off: press Ctrl+Alt+H in the game.";
        Pose = engine.IsConnected && engine.Glasses.LatestPose is { } pose
            ? string.Create(CultureInfo.CurrentCulture, $"Yaw {pose.Yaw:+0.0;-0.0;0.0}°   Pitch {pose.Pitch:+0.0;-0.0;0.0}°")
            : string.Empty;
    }

    // ---- local API (ADR-0012) -------------------------------------------------------------------

    /// <summary>Glasses of the running head look, or null. Thread-safe (the local API reads poses from it).</summary>
    public XrealGlasses? ActiveGlasses => _activeGlasses;

    /// <summary>Mouse output on/off for the local API. UI thread.</summary>
    public void SetEnabled(bool enabled)
    {
        if (_engine is not { } engine)
        {
            throw new XrealScreen.Contracts.XrealScreenApiException(XrealScreen.Contracts.ApiErrorCodes.Conflict, "Head look is not started.");
        }

        engine.Enabled = enabled;
    }

    /// <summary>All settings in API form. UI thread.</summary>
    public XrealScreen.Contracts.HeadLookSettings GetApiSettings() => new()
    {
        Mode = IsStick ? XrealScreen.Contracts.HeadLookModes.Joystick : XrealScreen.Contracts.HeadLookModes.Look,
        Sensitivity = (float)Sensitivity,
        VerticalScale = (float)VerticalScale,
        InvertY = InvertY,
        Deadband = (float)Deadband,
        StickDeadZone = (float)StickDeadZone,
        StickFull = (float)StickFull,
        StickSpeed = (float)StickSpeed,
        GlassesPrimary = GlassesPrimary,
    };

    /// <summary>Applies the non-null members (clamped to the page's slider ranges), like moving the sliders. UI thread.</summary>
    public void ApplyApiSettings(XrealScreen.Contracts.HeadLookSettings changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Mode is { } mode)
        {
            ModeIndex = mode == XrealScreen.Contracts.HeadLookModes.Joystick ? 1 : 0;
        }

        if (changes.Sensitivity is { } sensitivity)
        {
            Sensitivity = Math.Clamp(sensitivity, 1, 100);
        }

        if (changes.VerticalScale is { } vertical)
        {
            VerticalScale = Math.Clamp(vertical, 0, 2);
        }

        if (changes.InvertY is { } invert)
        {
            InvertY = invert;
        }

        if (changes.Deadband is { } deadband)
        {
            Deadband = Math.Clamp(deadband, 0, 3);
        }

        if (changes.StickDeadZone is { } deadZone)
        {
            StickDeadZone = Math.Clamp(deadZone, 0, 30);
        }

        if (changes.StickFull is { } full)
        {
            StickFull = Math.Clamp(full, 5, 60);
        }

        if (changes.StickSpeed is { } speed)
        {
            StickSpeed = Math.Clamp(speed, 100, 5000);
        }

        if (changes.GlassesPrimary is { } primary)
        {
            GlassesPrimary = primary;
        }
    }

    /// <summary>App exit: stop the IMU loop and hand the monitor layout back synchronously.</summary>
    public void Dispose()
    {
        _statusTimer.Stop();
        _cts?.Cancel();
        try
        {
            _run?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts?.Dispose();
        _cts = null;
        if (_primary is { } primary)
        {
            _primary = null;
            primary.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}

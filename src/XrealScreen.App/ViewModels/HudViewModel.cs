using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using XrealScreen.Core.Hud;
using XrealScreen.Host.Hud;

namespace XrealScreen.App.ViewModels;

/// <summary>
/// HUD page: a clear glasses view with a flight-style instrument overlay (ADR-0017).
/// Refuses to start while a workspace is using the glasses display.
/// </summary>
public sealed partial class HudViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly DispatcherQueueTimer _saveTimer;
    private ArHudEngine? _engine;
    private bool _loading;
    private bool _timerRunning;
    private bool _timerStarted;
    private TimeSpan _timerLeft = TimeSpan.FromMinutes(25);
    private DateTime _timerEnd;
    private DateTime _clipboardReadAt;
    private bool _clipboardPending;
    private string _clipboard = "CLIP";

    public HudViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _statusTimer = dispatcher.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(200);
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _saveTimer = dispatcher.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => App.Preferences.Save();

        _loading = true;
        Brightness = App.Preferences.HudBrightness;
        ShowHorizon = App.Preferences.HudShowHorizon;
        ShowLadder = App.Preferences.HudShowLadder;
        ShowTape = App.Preferences.HudShowTape;
        ShowClock = App.Preferences.HudShowClock;
        ShowMarkers = App.Preferences.HudShowMarkers;
        ShowGlance = App.Preferences.HudShowGlance;
        _loading = false;
    }

    [ObservableProperty]
    public partial double Brightness { get; set; }

    [ObservableProperty]
    public partial bool ShowHorizon { get; set; }

    [ObservableProperty]
    public partial bool ShowLadder { get; set; }

    [ObservableProperty]
    public partial bool ShowTape { get; set; }

    [ObservableProperty]
    public partial bool ShowClock { get; set; }

    [ObservableProperty]
    public partial bool ShowMarkers { get; set; }

    [ObservableProperty]
    public partial bool ShowGlance { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStopped))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(RecenterCommand))]
    public partial bool IsRunning { get; set; }

    public bool IsStopped => !IsRunning;

    [ObservableProperty]
    public partial string Status { get; set; } = "Stopped.";

    [ObservableProperty]
    public partial string Pose { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HeadLookNote { get; set; } = string.Empty;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading || e.PropertyName is not (nameof(Brightness) or nameof(ShowHorizon) or nameof(ShowLadder) or nameof(ShowTape) or nameof(ShowClock) or nameof(ShowMarkers) or nameof(ShowGlance)))
        {
            return;
        }

        App.Preferences.HudBrightness = Brightness;
        App.Preferences.HudShowHorizon = ShowHorizon;
        App.Preferences.HudShowLadder = ShowLadder;
        App.Preferences.HudShowTape = ShowTape;
        App.Preferences.HudShowClock = ShowClock;
        App.Preferences.HudShowMarkers = ShowMarkers;
        App.Preferences.HudShowGlance = ShowGlance;
        _engine?.UpdateStyle(CurrentStyle());
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    [RelayCommand(CanExecute = nameof(IsStopped))]
    private async Task StartAsync()
    {
        if (!App.Session.IsIdle)
        {
            Status = "Stop the workspace first. The clear HUD and a workspace both use the glasses display.";
            return;
        }

        if (App.Tracking.IsRunning)
        {
            await App.Tracking.StopCommand.ExecuteAsync(null).ConfigureAwait(true);
        }

        RequestClipboard();
        var engine = new ArHudEngine();
        engine.UpdateStyle(CurrentStyle());
        engine.Log += OnEngineLog;
        engine.PinSelected += OnPinSelected;
        _engine = engine;
        try
        {
            await Task.Run(() => engine.StartAsync(CancellationToken.None)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (ex is not (InvalidOperationException or IOException or TimeoutException or OperationCanceledException))
            {
                CrashLog.Write("Hud", ex);
            }

            Status = "Could not start the HUD.";
            Pose = ex.Message;
            await StopEngineAsync(engine).ConfigureAwait(true);
            return;
        }

        IsRunning = true;
        Status = "Look at a mark and hold still. Up is what you copied, left is a 25 minute timer, right plays or pauses, down mutes.";
        RefreshStatus();
        _statusTimer.Start();
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task StopAsync()
    {
        _statusTimer.Stop();
        if (_engine is { } engine)
        {
            await StopEngineAsync(engine).ConfigureAwait(true);
        }

        IsRunning = false;
        Status = "Stopped. The glasses show your desktop again.";
        Pose = string.Empty;
        HeadLookNote = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Recenter() => _engine?.Recenter();

    public void Dispose()
    {
        _statusTimer.Stop();
        _saveTimer.Stop();
        if (_engine is { } engine)
        {
            _engine = null;
            engine.Log -= OnEngineLog;
            engine.PinSelected -= OnPinSelected;
            try
            {
                engine.StopAsync().Wait(TimeSpan.FromSeconds(6));
            }
            catch (AggregateException)
            {
            }
        }
    }

    private HudStyle CurrentStyle() => new()
    {
        Brightness = (float)Brightness,
        ShowHorizon = ShowHorizon,
        ShowLadder = ShowLadder,
        ShowTape = ShowTape,
        ShowClock = ShowClock,
        ShowMarkers = ShowMarkers,
        ShowGlance = ShowGlance,
        GlanceUp = _clipboard,
        GlanceLeft = TimerLabel(),
        GlanceRight = "PLAY",
        GlanceDown = "MUTE",
    };

    private async Task StopEngineAsync(ArHudEngine engine)
    {
        if (ReferenceEquals(_engine, engine))
        {
            _engine = null;
        }

        engine.Log -= OnEngineLog;
        engine.PinSelected -= OnPinSelected;
        try
        {
            await Task.Run(engine.StopAsync).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException or OperationCanceledException)
        {
            Pose = ex.Message;
        }
    }

    private void OnEngineLog(object? sender, string message) => _dispatcher.TryEnqueue(() => Pose = message);

    private void RefreshStatus()
    {
        if (_engine is not { } engine)
        {
            return;
        }

        var pose = engine.CurrentPose;
        Pose = string.Create(CultureInfo.CurrentCulture, $"Heading {pose.YawDegrees:+0;-0;0}°   pitch {pose.PitchDegrees:+0;-0;0}°   roll {pose.RollDegrees:+0;-0;0}°");
        HeadLookNote = App.HeadLook.IsRunning
            ? "Head look is also running. Both read the glasses. Stop one of them if the HUD stops moving."
            : string.Empty;
        if (_timerRunning)
        {
            TimeSpan left = _timerEnd - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
            {
                _timerRunning = false;
                _timerLeft = TimeSpan.Zero;
            }
            else
            {
                _timerLeft = left;
            }
        }

        if (DateTime.UtcNow - _clipboardReadAt > TimeSpan.FromSeconds(1))
        {
            RequestClipboard();
        }

        engine.UpdateStyle(CurrentStyle());
    }

    private void OnPinSelected(int id) => _dispatcher.TryEnqueue(() => UsePin(id));

    private void UsePin(int id)
    {
        switch (id)
        {
            case 0:
                RequestClipboard();
                break;
            case 1:
                ToggleTimer();
                break;
            case 2:
                GlanceInput.Tap(GlanceInput.MediaPlayPause);
                break;
            case 3:
                GlanceInput.Tap(GlanceInput.VolumeMute);
                break;
        }

        _engine?.UpdateStyle(CurrentStyle());
    }

    private void RequestClipboard()
    {
        if (_clipboardPending)
        {
            return;
        }

        _clipboardPending = true;
        _clipboardReadAt = DateTime.UtcNow;
        GlanceInput.ReadClipboard(text =>
        {
            _dispatcher.TryEnqueue(() =>
            {
                _clipboardPending = false;
                _clipboard = text;
                _engine?.UpdateStyle(CurrentStyle());
            });
        });
    }

    private void ToggleTimer()
    {
        if (_timerLeft <= TimeSpan.Zero)
        {
            _timerLeft = TimeSpan.FromMinutes(25);
            _timerStarted = true;
            _timerRunning = true;
            _timerEnd = DateTime.UtcNow + _timerLeft;
            return;
        }

        if (_timerRunning)
        {
            _timerRunning = false;
            return;
        }

        _timerStarted = true;
        _timerRunning = true;
        _timerEnd = DateTime.UtcNow + _timerLeft;
    }

    private string TimerLabel()
    {
        if (_timerLeft <= TimeSpan.Zero && _timerStarted)
        {
            return "DONE";
        }

        int seconds = (int)Math.Ceiling(_timerLeft.TotalSeconds);
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }
}

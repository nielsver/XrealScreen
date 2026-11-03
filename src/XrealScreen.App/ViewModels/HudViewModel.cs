using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using XrealScreen.Core.Hud;
using XrealScreen.Host.Hud;

namespace XrealScreen.App.ViewModels;

/// <summary>
/// HUD page: the main monitor on the glasses, with a flight overlay and a live Grok suggestion (ADR-0019).
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
    private HudCaptionPicture? _screen;
    private int _screenRevision;
    private HudCaptionPicture? _caption;
    private DateTime _captionUntil;
    private int _captionRevision;
    private CancellationTokenSource? _live;
    private CancellationTokenSource? _suggestDelay;
    private int _suggestGeneration;
    private int _suggestNow;
    private readonly object _suggestGate = new();

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
        Status = "Your main monitor is on the glasses. Grok looks at it and puts a suggestion on the screen. Look further up and hold still to ask again.";
        BeginLive();
        RefreshStatus();
        _statusTimer.Start();
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task StopAsync()
    {
        _statusTimer.Stop();
        StopLive();
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
        StopLive();
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
        GlanceRead = "READ",
        Screen = Volatile.Read(ref _screen),
        Caption = DateTime.UtcNow < _captionUntil ? _caption : null,
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

        if (_caption is not null && DateTime.UtcNow >= _captionUntil)
        {
            _caption = null;
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
            case 4:
                RequestSuggestion();
                break;
        }

        _engine?.UpdateStyle(CurrentStyle());
    }

    private void BeginLive()
    {
        StopLive();
        _live = new CancellationTokenSource();
        CancellationToken token = _live.Token;
        ShowCaption(CaptionRaster.Draw("Looking at the screen", ++_captionRevision));
        _ = MirrorAsync(token);
        _ = SuggestAsync(token);
    }

    private void StopLive()
    {
        Volatile.Write(ref _suggestGeneration, _suggestGeneration + 1);
        lock (_suggestGate)
        {
            _suggestDelay?.Cancel();
        }

        _live?.Cancel();
        _live?.Dispose();
        _live = null;
        Volatile.Write(ref _screen, null);
        _caption = null;
    }

    private void RequestSuggestion()
    {
        Interlocked.Exchange(ref _suggestNow, 1);
        lock (_suggestGate)
        {
            _suggestDelay?.Cancel();
        }
    }

    private async Task MirrorAsync(CancellationToken token)
    {
        int misses = 0;
        while (!token.IsCancellationRequested)
        {
            long started = Environment.TickCount64;
            try
            {
                DeskMirror.Pixels? frame = await DeskMirror.CaptureAsync(1280, token).ConfigureAwait(false);
                if (frame is { } pixels)
                {
                    misses = 0;
                    byte[] bgra = pixels.Bgra;
                    int width = pixels.Width;
                    int height = pixels.Height;
                    _dispatcher.TryEnqueue(() =>
                    {
                        if (token.IsCancellationRequested || _engine is null)
                        {
                            return;
                        }

                        Volatile.Write(ref _screen, new HudCaptionPicture(++_screenRevision, width, height, bgra));
                        _engine.UpdateStyle(CurrentStyle());
                    });
                }
                else
                {
                    NoteMissingScreen(++misses, token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                NoteMissingScreen(++misses, token);
            }

            int wait = 250 - (int)(Environment.TickCount64 - started);
            if (wait > 0)
            {
                try
                {
                    await Task.Delay(wait, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void NoteMissingScreen(int misses, CancellationToken token)
    {
        if (misses != 8)
        {
            return;
        }

        _dispatcher.TryEnqueue(() =>
        {
            if (!token.IsCancellationRequested && _engine is not null)
            {
                ShowCaption(CaptionRaster.Draw("NO SCREEN", ++_captionRevision));
            }
        });
    }

    private async Task SuggestAsync(CancellationToken token)
    {
        int generation = _suggestGeneration;
        while (!token.IsCancellationRequested && generation == Volatile.Read(ref _suggestGeneration))
        {
            HudCaptionPicture? frame = Volatile.Read(ref _screen);
            if (frame is null)
            {
                try
                {
                    await Task.Delay(200, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            string note;
            try
            {
                note = await GrokScreen.SuggestAsync(frame.Bgra, frame.Width, frame.Height, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _dispatcher.TryEnqueue(() =>
            {
                if (generation != _suggestGeneration || _engine is null)
                {
                    return;
                }

                ShowCaption(CaptionRaster.Draw(note, ++_captionRevision));
            });

            if (Interlocked.Exchange(ref _suggestNow, 0) == 1)
            {
                continue;
            }

            CancellationTokenSource delay;
            lock (_suggestGate)
            {
                _suggestDelay?.Dispose();
                _suggestDelay = CancellationTokenSource.CreateLinkedTokenSource(token);
                delay = _suggestDelay;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), delay.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ShowCaption(HudCaptionPicture picture)
    {
        _caption = picture;
        _captionUntil = DateTime.MaxValue;
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

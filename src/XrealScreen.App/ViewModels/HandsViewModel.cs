using System.Drawing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using XrealScreen.Core.Abstractions;
using XrealScreen.Display;
using XrealScreen.Host.Hands;
using XrealScreen.Vision.Hands;

namespace XrealScreen.App.ViewModels;

/// <summary>
/// Hands page: Eye camera to pointer (ADR-0016). The camera socket is open only while this is running.
/// A five-point calibration still comes from <c>xrs hands targets --calibrate</c>.
/// </summary>
public sealed partial class HandsViewModel : ObservableObject, IDisposable
{
    private const double MaxCalibrationError = 0.1;
    private const int EyeWidth = 504;
    private const int EyeHeight = 378;
    private const double CameraEdgeMargin = 25;

    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _saveTimer;
    private HandControlEngine? _engine;
    private CancellationTokenSource? _run;
    private Task? _loop;
    private int _generation;
    private bool _loading;

    public HandsViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _saveTimer = dispatcher.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => App.Preferences.Save();

        _loading = true;
        ClickModeIndex = Math.Clamp(App.Preferences.HandClick, 0, 2);
        _loading = false;
        CameraNote = "The Eye camera is closed.";
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    public partial bool IsRunning { get; set; }

    public bool IsStopped => !IsRunning;

    [ObservableProperty]
    public partial string Status { get; set; } = "Stopped.";

    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CameraNote { get; set; }

    /// <summary>0 pinch, 1 hold still, 2 either.</summary>
    [ObservableProperty]
    public partial int ClickModeIndex { get; set; }

    partial void OnClickModeIndexChanged(int value)
    {
        if (_loading || value is < 0 or > 2)
        {
            return;
        }

        App.Preferences.HandClick = value;
        if (_engine is not null)
        {
            _engine.Click = (ClickMode)value;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(IsStopped));

    [RelayCommand(CanExecute = nameof(IsStopped))]
    private void Start()
    {
        if (IsRunning)
        {
            return;
        }

        if (App.HeadLook.IsRunning)
        {
            Status = "Stop head look first. Hand control and head look both move the pointer.";
            return;
        }

        string? models = ResolveModels();
        if (models is null)
        {
            Status = "Hand models are not on this PC. Run tools\\Convert-HandModels.ps1, or set XREALSCREEN_HAND_MODELS to that folder.";
            return;
        }

        DisplayMonitor? glasses;
        try
        {
            glasses = GlassesDisplayLocator.FindGlasses(new CcdDisplayTopology().GetActiveMonitors());
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Status = "Could not read the monitors. " + ex.Message;
            return;
        }

        if (glasses is null)
        {
            Status = "The glasses monitor is not connected. Plug them in and choose Extend, then start again.";
            return;
        }

        var store = new HandCalibrationStore();
        var saved = store.Load();
        if (saved is not null && (saved.RmsError > MaxCalibrationError || !saved.Calibration.CoversScreenInsideCamera(EyeWidth, EyeHeight, CameraEdgeMargin)))
        {
            saved = null;
        }

        var area = new Rectangle(glasses.X, glasses.Y, glasses.Resolution.Width, glasses.Resolution.Height);
        var engine = new HandControlEngine(new HandControlOptions
        {
            ModelDirectory = models,
            TargetArea = area,
            Calibration = saved?.Calibration,
            PointerOptions = new HandPointerOptions { Click = (ClickMode)ClickModeIndex },
        })
        {
            Click = (ClickMode)ClickModeIndex,
        };
        engine.FrameProcessed += OnFrame;

        var cts = new CancellationTokenSource();
        int generation = ++_generation;
        _engine = engine;
        _run = cts;
        IsRunning = true;
        Status = saved is null
            ? "Running with the default mapping. Calibrate once with xrs hands targets --calibrate for a closer fit."
            : $"Running. Calibration from {saved.Created.LocalDateTime:yyyy-MM-dd HH:mm}.";
        Detail = "Connecting to the Eye camera…";
        CameraNote = "The Eye camera is open. Turn Gesture Control off on the glasses, or the glasses click on the same pinch.";

        _loop = Task.Run(() => engine.RunAsync(cts.Token));
        _ = ObserveLoopAsync(_loop, generation);
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task StopAsync()
    {
        await StopCoreAsync().ConfigureAwait(true);
        Status = "Stopped.";
        Detail = string.Empty;
        CameraNote = "The Eye camera is closed.";
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        _generation++;
        _run?.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(4));
        }
        catch (AggregateException)
        {
        }
    }

    private async Task ObserveLoopAsync(Task loop, int generation)
    {
        try
        {
            await loop.ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (generation != _generation)
            {
                return;
            }

            _dispatcher.TryEnqueue(() =>
            {
                if (generation != _generation)
                {
                    return;
                }

                Status = "Hand control stopped. " + ex.GetBaseException().Message;
                Detail = string.Empty;
                CameraNote = "The Eye camera is closed.";
                IsRunning = false;
                _run?.Dispose();
                _run = null;
                DetachEngine();
            });
        }
    }

    private async Task StopCoreAsync()
    {
        _generation++;
        var cts = _run;
        var loop = _loop;
        _run = null;
        cts?.Cancel();
        DetachEngine();
        IsRunning = false;
        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or AggregateException)
            {
            }
        }

        cts?.Dispose();
    }

    private void DetachEngine()
    {
        if (_engine is { } engine)
        {
            engine.FrameProcessed -= OnFrame;
            _engine = null;
        }
    }

    private void OnFrame(HandControlFrame frame)
    {
        string state = frame.State switch
        {
            HandControlState.Starting => "Connecting to the Eye camera…",
            HandControlState.NoHand => "No hand in view.",
            HandControlState.Active => "The hand is moving the pointer.",
            HandControlState.PausedByRealInput => "Paused because the mouse or keyboard was used.",
            HandControlState.OutputOff => "Tracking, not moving the pointer.",
            HandControlState.Reconnecting => "Camera lost. Connecting again…",
            _ => frame.State.ToString(),
        };
        string hand = frame.Hand is null ? "hand not found" : "hand found";
        string line = string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{state}  {hand}  {frame.ProcessMs:0} ms");
        _dispatcher.TryEnqueue(() => Detail = line);
    }

    private static string? ResolveModels()
    {
        if (Environment.GetEnvironmentVariable("XREALSCREEN_HAND_MODELS") is { Length: > 0 } env && HandTracker.ModelsExist(env))
        {
            return env;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "artifacts", "models", "hands");
            if (HandTracker.ModelsExist(path))
            {
                return path;
            }
        }

        return null;
    }
}

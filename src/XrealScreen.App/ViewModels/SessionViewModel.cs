using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using XrealScreen.Core.Tracking;
using XrealScreen.Core.Workspace;
using XrealScreen.Display;
using XrealScreen.Host;

namespace XrealScreen.App.ViewModels;

/// <summary>A message for the tray balloon.</summary>
public sealed record SessionNotification(string Title, string Message);

/// <summary>
/// Owns the workspace session (WorkspaceEngine): builds options from the Screens and Tracking pages,
/// persists them, and reports state/log/pose to the UI thread.
/// </summary>
public sealed partial class SessionViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly WorkspaceEngine _engine = new();
    private readonly WorkspaceSettingsStore _store = new();
    private readonly WorkspaceLibrary _library = new();
    private readonly DispatcherQueueTimer _autoSaveTimer;
    private readonly Task _recovery;

    /// <summary>Last loaded options; keeps the fields the UI does not edit (prediction, filter tuning).</summary>
    private WorkspaceOptions _base = new();

    /// <summary>What settings.json holds (null = unknown: save on the next check).</summary>
    private WorkspaceOptions? _lastSaved;
    private readonly WorkspaceViewModel _workspace;
    private readonly TrackingViewModel _tracking;
    private readonly DispatcherQueueTimer _poseTimer;
    private bool _applyingEngineDistance;

    public SessionViewModel(DispatcherQueue dispatcher, WorkspaceViewModel workspace, TrackingViewModel tracking)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _workspace = workspace;
        _tracking = tracking;
        _poseTimer = dispatcher.CreateTimer();
        _poseTimer.Interval = TimeSpan.FromMilliseconds(33);
        _poseTimer.Tick += (_, _) => _tracking.ApplyExternalPose(_engine.CurrentPose);
        _engine.StateChanged += (_, state) => _dispatcher.TryEnqueue(() => OnEngineState(state));
        _engine.Log += (_, message) => _dispatcher.TryEnqueue(() => AddLog(message));

        // Distance: hotkeys (Ctrl+Alt+Up/Down) update the slider; the slider moves the screens live.
        _engine.DistanceChanged += (_, meters) => _dispatcher.TryEnqueue(() =>
        {
            _applyingEngineDistance = true;
            _workspace.DistanceMeters = Math.Round(meters, 2);
            _applyingEngineDistance = false;
        });
        _workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.DistanceMeters) && IsRunning && !_applyingEngineDistance)
            {
                _engine.SetDistance((float)_workspace.DistanceMeters);
            }
        };

        // Every setting is saved automatically shortly after it changes.
        _autoSaveTimer = dispatcher.CreateTimer();
        _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _autoSaveTimer.IsRepeating = false;
        _autoSaveTimer.Tick += (_, _) => SaveIfChanged();
        _engine.LiveBackdrop = new LiveBackdrop();
        _workspace.FrameLookChanged += (_, _) => _workspace.WriteLive(_engine.LiveBackdrop!);
        _workspace.PropertyChanged += (_, _) => ScheduleAutoSave();
        _tracking.PropertyChanged += (_, _) => ScheduleAutoSave();

        _lastSaved = _store.Load();
        ApplySettings(_lastSaved);
        RefreshSavedWorkspaces(App.Preferences.LastWorkspaceName);
        _recovery = RecoverAsync();

        // Glasses plugged in while XrealScreen runs → optionally start the selected workspace.
        _glassesConnected = GlassesSignal.CurrentResolution() is not null;
        _glassesTimer = dispatcher.CreateTimer();
        _glassesTimer.Interval = TimeSpan.FromSeconds(2);
        _glassesTimer.Tick += (_, _) => CheckGlassesConnection();
        _glassesTimer.Start();
    }

    private readonly DispatcherQueueTimer _glassesTimer;
    private bool _glassesConnected;
    private bool _autoStartPending;

    /// <summary>Raised for messages that should also reach a user who is not looking at the window (tray balloon).</summary>
    public event EventHandler<SessionNotification>? Notification;

    /// <summary>A start was refused because the glasses are not in UltraWide mode (Glasses anchor); starts when they are.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsWaitingForGlassesMode { get; set; }

    /// <summary>Why the chosen workspace type cannot use the glasses' current mode, or null when it can (or no glasses are found).</summary>
    private string? GlassesModeProblem()
    {
        if (!_workspace.IsGlassesAnchor || GlassesSignal.CurrentResolution() is not { } r || AnchorSplit.IsUltrawideSignal(r))
        {
            return null;
        }

        return $"The glasses send {r} (UltraWide Off). Glasses anchor needs UltraWide: in the glasses menu set Spatial Screen → UltraWide Mode to 32:9, 21:9 or 16:18. The workspace starts automatically when you do. Stop cancels.";
    }

    private void CheckGlassesConnection()
    {
        bool connected = GlassesSignal.CurrentResolution() is not null;
        bool justConnected = connected && !_glassesConnected;
        _glassesConnected = connected;
        if (IsWaitingForGlassesMode)
        {
            if (!_workspace.IsGlassesAnchor)
            {
                IsWaitingForGlassesMode = false; // the user switched to App head tracking: start manually
            }
            else if (connected && GlassesModeProblem() is null && !_autoStartPending)
            {
                _ = StartAfterModeChangeAsync();
            }

            return;
        }

        if (justConnected && IsIdle && !_starting && !_autoStartPending && App.Preferences.StartWorkspaceWhenGlassesConnect)
        {
            _ = StartAfterGlassesConnectedAsync();
        }
    }

    private async Task StartAfterGlassesConnectedAsync()
    {
        _autoStartPending = true;
        try
        {
            ShowInfo("Glasses connected", $"Starting {AutoStartDescription}…");
            await Task.Delay(3000).ConfigureAwait(true); // let Windows finish setting up the glasses monitor
            if (!_glassesConnected || !IsIdle || _starting)
            {
                return;
            }

            await StartAutoStartWorkspaceAsync().ConfigureAwait(true);
        }
        finally
        {
            _autoStartPending = false;
        }
    }

    private async Task StartAfterModeChangeAsync()
    {
        _autoStartPending = true;
        try
        {
            ShowInfo("Glasses in UltraWide mode", "Starting the workspace…");
            await Task.Delay(2500).ConfigureAwait(true); // the glasses re-enumerate when UltraWide changes
            if (IsWaitingForGlassesMode && IsIdle && !_starting)
            {
                await StartAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            _autoStartPending = false;
        }
    }

    /// <summary>"Start a workspace when XrealScreen starts": waits for the crash-safe restore first.</summary>
    public async Task StartOnLaunchAsync()
    {
        await _recovery.ConfigureAwait(true);
        if (IsIdle)
        {
            await StartAutoStartWorkspaceAsync().ConfigureAwait(true);
        }
    }

    private static string AutoStartDescription =>
        App.Preferences.AutoStartWorkspaceName is { } name ? $"\"{name}\"" : "the latest workspace";

    /// <summary>Loads the workspace chosen in Settings (none = keep the latest settings), then starts.</summary>
    private async Task StartAutoStartWorkspaceAsync()
    {
        if (App.Preferences.AutoStartWorkspaceName is { } name)
        {
            if (_library.Load(name) is { } options)
            {
                ApplySettings(options);
                SaveIfChanged();
                _workspace.RefreshGlassesSignal();
                RememberWorkspace(name);
                AddLog($"automatic start: \"{name}\"");
            }
            else
            {
                AddLog($"automatic start: saved workspace \"{name}\" not found; using the latest settings");
            }
        }

        await StartAsync().ConfigureAwait(true);
    }

    /// <summary>Crash-safe restore: clean up a session that did not end normally (app crash, power loss).</summary>
    private async Task RecoverAsync()
    {
        try
        {
            string? message = await Task.Run(() => WorkspaceRecovery.RecoverAsync(CancellationToken.None)).ConfigureAwait(true);
            if (message is not null)
            {
                StatusTitle = "Monitors restored";
                StatusMessage = message;
                Severity = InfoBarSeverity.Warning;
                AddLog(message);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Recovery", ex);
            AddLog($"recovery failed: {ex.Message} — run `xrs vdd clear` and `xrs display restore`");
        }

        await CheckDriverAsync().ConfigureAwait(true);
    }

    /// <summary>The virtual display driver is not installed (Store installs have no driver, ADR-0005): workspaces cannot start.</summary>
    [ObservableProperty]
    public partial bool IsDriverMissing { get; set; }

    public Uri DriverDownloadUri { get; } = new(VirtualDisplayDriver.DownloadUrl);

    private async Task<bool> CheckDriverAsync()
    {
        bool available = await Task.Run(() => VirtualDisplayDriver.IsAvailableAsync(CancellationToken.None)).ConfigureAwait(true);
        if (!available && !IsDriverMissing)
        {
            AddLog("virtual display driver not found: workspaces need it (head look on the Games page does not)");
        }

        IsDriverMissing = !available;
        return available;
    }

    public ObservableCollection<string> Log { get; } = [];

    // ---- local API (ADR-0012) -------------------------------------------------------------------

    /// <summary>Pose of the running workspace, or null. Thread-safe (read by the local API).</summary>
    public XrealScreen.Core.Tracking.HeadPose? LivePose => _engine.State == WorkspaceState.Running ? _engine.CurrentPose : null;

    /// <summary>Saved workspace names (local API).</summary>
    public IReadOnlyList<string> SavedWorkspaceNames => _library.List();

    /// <summary>Writes a line to the Home page log (local API connections).</summary>
    public void LogMessage(string message) => AddLog(message);

    /// <summary>Local API: loads the saved workspace <paramref name="name"/> (if given) and starts. UI thread.</summary>
    public async Task StartForApiAsync(string? name)
    {
        if (!IsIdle || _starting)
        {
            throw new XrealScreen.Contracts.XrealScreenApiException(XrealScreen.Contracts.ApiErrorCodes.Conflict, "A workspace is already running or starting.");
        }

        if (name is not null)
        {
            var options = _library.Load(name)
                ?? throw new XrealScreen.Contracts.XrealScreenApiException(XrealScreen.Contracts.ApiErrorCodes.Unavailable, $"No saved workspace \"{name}\".");
            ApplySettings(options);
            SaveIfChanged();
            _workspace.RefreshGlassesSignal();
            RememberWorkspace(name);
            AddLog($"local API: start \"{name}\"");
        }

        await StartAsync().ConfigureAwait(true);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(RecenterCommand), nameof(TakeScreenshotCommand))]
    public partial WorkspaceState State { get; set; }

    /// <summary>Last saved screenshot (shown on the Home page), or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScreenshot))]
    public partial string? LastScreenshotPath { get; set; }

    public bool HasScreenshot => LastScreenshotPath is not null;

    /// <summary>Saves what the glasses show to Pictures\XrealScreen (also Ctrl+Alt+P and the tray menu).</summary>
    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task TakeScreenshotAsync()
    {
        try
        {
            string path = await Task.Run(() => GlassesScreenshot.SaveAsync(null, CancellationToken.None)).ConfigureAwait(true);
            LastScreenshotPath = path;
            AddLog($"screenshot saved: {path}");
            Notification?.Invoke(this, new SessionNotification("Screenshot saved", path));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            AddLog($"screenshot failed: {ex.Message}");
            Notification?.Invoke(this, new SessionNotification("Screenshot failed", ex.Message));
        }
    }

    [RelayCommand]
    private void OpenScreenshotFolder()
    {
        string argument = LastScreenshotPath is { } path && File.Exists(path)
            ? $"/select,\"{path}\""
            : $"\"{Directory.CreateDirectory(GlassesScreenshot.DefaultFolder).FullName}\"";
        using var _ = System.Diagnostics.Process.Start("explorer.exe", argument);
    }

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = "Workspace stopped";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Choose the workspace type on the Screens page, set the glasses menu as described there, then select Start workspace.";

    [ObservableProperty]
    public partial InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Informational;

    public bool IsRunning => State == WorkspaceState.Running;

    /// <summary>Result of sending a window or finding the cursor.</summary>
    [ObservableProperty]
    public partial string DeskStatus { get; set; } = "Ctrl+Alt+1, 2 or 3 sends the window you are using. Ctrl+Alt+0 finds the cursor.";

    /// <summary>Moves the window you are using onto virtual screen <paramref name="screen"/> (1, 2, or 3).</summary>
    public void SendWindowTo(int screen)
    {
        if (!IsRunning)
        {
            DeskStatus = "Start the workspace, then send the window.";
            return;
        }

        var bounds = _engine.VirtualScreenBounds;
        if (screen < 1 || screen > bounds.Count)
        {
            DeskStatus = "That screen is not in this workspace.";
            return;
        }

        string? title = WindowMover.MoveForegroundWindow(bounds[screen - 1]);
        DeskStatus = title is null
            ? "That window stayed where it is. A window running as administrator cannot be moved."
            : $"Sent {title} to screen {screen}.";
        AddLog(DeskStatus);
    }

    /// <summary>Puts the pointer in the middle of the primary virtual screen, or screen 1 when the desk monitor stays primary.</summary>
    public void FindCursor()
    {
        if (!IsRunning)
        {
            DeskStatus = "Start the workspace, then find the cursor.";
            return;
        }

        var bounds = _engine.VirtualScreenBounds;
        if (bounds.Count == 0)
        {
            DeskStatus = "The virtual screens are not ready yet.";
            return;
        }

        int index = ScreenDesk.CursorScreen(_workspace.PrimaryScreenIndex, bounds.Count);
        DeskStatus = ScreenDesk.TryPlaceCursor(bounds[index])
            ? $"Cursor is on screen {index + 1}."
            : "The cursor could not be moved.";
        AddLog(DeskStatus);
    }

    public bool IsIdle => State is WorkspaceState.Stopped or WorkspaceState.Faulted;

    /// <summary>A start is in progress (the engine reports Starting asynchronously; this blocks a second start meanwhile).</summary>
    private bool _starting;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task StartAsync()
    {
        if (_starting)
        {
            return;
        }

        _starting = true;
        try
        {
            await StartCoreAsync().ConfigureAwait(true);
        }
        finally
        {
            _starting = false;
        }
    }

    private async Task StartCoreAsync()
    {
        if (GlassesModeProblem() is { } problem)
        {
            // Glasses anchor with UltraWide Off: wait; CheckGlassesConnection starts once the mode is switched.
            IsWaitingForGlassesMode = true;
            StatusTitle = "Waiting for UltraWide mode — workspace not started";
            StatusMessage = problem;
            Severity = InfoBarSeverity.Warning;
            AddLog("not started: glasses are not in UltraWide mode; waiting for the mode change");
            Notification?.Invoke(this, new SessionNotification(
                "Workspace not started: UltraWide is Off",
                "Glasses anchor needs UltraWide. In the glasses menu set Spatial Screen → UltraWide Mode to 32:9, 21:9 or 16:18; the workspace then starts automatically."));
            return;
        }

        IsWaitingForGlassesMode = false;
        if (!await CheckDriverAsync().ConfigureAwait(true))
        {
            StatusTitle = "Virtual display driver not installed — workspace not started";
            StatusMessage = "Workspaces create virtual monitors and need the virtual display driver. Install it with the driver installer (link above), then select Start workspace again. Head look on the Games page works without it.";
            Severity = InfoBarSeverity.Error;
            Notification?.Invoke(this, new SessionNotification(
                "Workspace not started: driver missing",
                "Install the XrealScreen virtual display driver, then start the workspace again."));
            return;
        }

        if (_tracking.IsRunning)
        {
            await _tracking.StopCommand.ExecuteAsync(null).ConfigureAwait(true);
        }

        // Head look (Games page) uses the IMU and may have made the glasses primary: hand both back first.
        if (App.HeadLook.IsRunning)
        {
            await App.HeadLook.StopCommand.ExecuteAsync(null).ConfigureAwait(true);
        }

        var options = SaveIfChanged();
        _workspace.MarkScreenSettingsApplied();
        StatusTitle = "Starting…";
        StatusMessage = "Creating virtual monitors and preparing the glasses.";
        Severity = InfoBarSeverity.Informational;
        try
        {
            await Task.Run(() => _engine.StartAsync(options, CancellationToken.None)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (ex is not (InvalidOperationException or OperationCanceledException or IOException or TimeoutException))
            {
                CrashLog.Write("Start", ex);
            }

            StatusTitle = "Could not start";
            StatusMessage = ex.Message;
            Severity = InfoBarSeverity.Error;
        }
    }

    private bool CanStop => IsRunning || IsWaitingForGlassesMode;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (IsWaitingForGlassesMode)
        {
            IsWaitingForGlassesMode = false;
            ShowInfo("Workspace not started", "Stopped waiting for UltraWide mode.");
            return;
        }

        try
        {
            await Task.Run(_engine.StopAsync).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Stop", ex);
            StatusTitle = "Stop reported an error";
            StatusMessage = ex.Message;
            Severity = InfoBarSeverity.Warning;
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Recenter() => _engine.Recenter();

    /// <summary>Stops the session from the window-closed handler (engine continuations do not need the UI thread).</summary>
    public void StopBlocking() => _engine.StopAsync().GetAwaiter().GetResult();

    public void Dispose()
    {
        _autoSaveTimer.Stop();
        _glassesTimer.Stop();
        SaveIfChanged();
        StopBlocking();
        _engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void OnEngineState(WorkspaceState state)
    {
        State = state;
        _tracking.SessionActive = state is WorkspaceState.Starting or WorkspaceState.Running or WorkspaceState.Stopping;
        if (state == WorkspaceState.Running)
        {
            _poseTimer.Start();
            StatusTitle = "Workspace running";
            StatusMessage = _workspace.KindIndex == 0
                ? "Screens are ready. Ctrl+Alt+1, 2 or 3 sends the window you are using. Ctrl+Alt+0 finds the cursor. Long-press X on the glasses recenters. Ctrl+Alt+Q stops."
                : "Screens are ready. Ctrl+Alt+1, 2 or 3 sends the window you are using. Ctrl+Alt+0 finds the cursor. Ctrl+Alt+R recenters. Ctrl+Alt+Q stops.";
            Severity = InfoBarSeverity.Success;
        }
        else if (state is WorkspaceState.Stopped or WorkspaceState.Faulted)
        {
            _poseTimer.Stop();
            _tracking.ClearExternalPose();
            _workspace.RefreshGlassesSignal();
            if (_engine.LastStopReason == WorkspaceStopReason.GlassesDisplayChanged)
            {
                _ = RestartAfterGlassesChangeAsync();
                return;
            }

            if (state == WorkspaceState.Faulted)
            {
                StatusTitle = "Workspace stopped with an error";
                StatusMessage = _engine.LastError?.Message ?? "See the log.";
                Severity = InfoBarSeverity.Error;
            }
            else if (Severity != InfoBarSeverity.Error)
            {
                StatusTitle = "Workspace stopped";
                StatusMessage = "Your monitor layout and refresh rates were restored.";
                Severity = InfoBarSeverity.Informational;
            }
        }
    }

    /// <summary>The glasses re-enumerate when their UltraWide mode changes: start again with the new split.</summary>
    private async Task RestartAfterGlassesChangeAsync()
    {
        StatusTitle = "Glasses display changed";
        StatusMessage = "Restarting the workspace for the new glasses mode…";
        Severity = InfoBarSeverity.Informational;
        await Task.Delay(2500).ConfigureAwait(true); // let Windows finish re-enumerating the glasses
        _workspace.RefreshGlassesSignal();
        if (IsIdle)
        {
            await StartAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Applies the screen settings (count, gap, shape): saves them and, when a workspace runs,
    /// restarts it so the new virtual monitors are created. Nothing changes until this is pressed.
    /// </summary>
    [RelayCommand]
    private async Task ApplyScreenSettingsAsync()
    {
        SaveIfChanged();
        _workspace.MarkScreenSettingsApplied();
        if (!IsRunning)
        {
            StatusTitle = "Screen settings saved";
            StatusMessage = "They are used when you start the workspace.";
            Severity = InfoBarSeverity.Informational;
            return;
        }

        StatusTitle = "Applying new screen layout";
        await RestartAsync().ConfigureAwait(true);
    }

    private async Task RestartAsync()
    {
        StatusMessage = "Restarting the workspace…";
        await StopAsync().ConfigureAwait(true);
        await Task.Delay(1000).ConfigureAwait(true);
        if (IsIdle)
        {
            await StartAsync().ConfigureAwait(true);
        }
    }

    private void AddLog(string message)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (Log.Count > 200)
        {
            Log.RemoveAt(Log.Count - 1);
        }
    }

    private WorkspaceOptions BuildOptions() => _base with
    {
        Kind = _workspace.KindIndex == 0 ? WorkspaceKind.GlassesAnchor : WorkspaceKind.AppTracking,
        AnchorGapPixels = (int)Math.Round(_workspace.AnchorGapPixels),
        AnchorAspect = _workspace.AnchorAspect,
        AnchorGradient = _workspace.AnchorGradient,
        AnchorFrame = _workspace.FrameStyle,
        AnchorGradientLeft = _workspace.GradientLeftHex,
        AnchorGradientRight = _workspace.GradientRightHex,
        AnchorScreenColors = _workspace.SavedScreenColors(),
        GlassesBrightness = (int)Math.Round(_workspace.GlassesBrightness),
        GlassesDimmer = (int)Math.Round(_workspace.GlassesDimmer),
        ScreenCount = (int)Math.Clamp(Math.Round(double.IsNaN(_workspace.ScreenCount) ? 1 : _workspace.ScreenCount), 1, WorkspaceLayout.MaxScreens),
        PrimaryScreen = Math.Clamp(_workspace.PrimaryScreenIndex, 0, WorkspaceLayout.MaxScreens),
        MoveWindowsFromGlasses = _workspace.MoveWindowsFromGlasses,
        Mode = _workspace.UltrawideMode,
        Preset = _workspace.PresetIndex == 1 ? LayoutPreset.Grid : LayoutPreset.Arc,
        DistanceMeters = (float)_workspace.DistanceMeters,
        Source = _tracking.SourceIndex switch { 0 => TrackingSource.Simulated, 2 => TrackingSource.Fixed, _ => TrackingSource.Glasses },
        Stabilizer = _tracking.StabilizerPreset,
        NeckModel = _tracking.NeckModel,
        Axes = (TrackingAxes)Math.Clamp(_tracking.AxesIndex, 0, Enum.GetValues<TrackingAxes>().Length - 1),
        AutoCenter = new AutoCenterSettings
        {
            Policy = _tracking.PolicyIndex == 1 ? RecenterPolicy.Follow : RecenterPolicy.Manual,
            DeadZoneDegrees = (float)_tracking.DeadZoneDegrees,
            SmoothingSeconds = (float)_tracking.SmoothingSeconds,
            MaxFollowSpeedDegrees = (float)_tracking.MaxFollowSpeed,
        },
    };

    private void ApplySettings(WorkspaceOptions o)
    {
        _base = o;
        _workspace.BeginSettingsLoad();
        _workspace.KindIndex = o.Kind == WorkspaceKind.GlassesAnchor ? 0 : 1;
        _workspace.AnchorGapPixels = o.AnchorGapPixels;
        _workspace.AnchorAspectIndex = (int)o.AnchorAspect;
        _workspace.FrameStyleIndex = (int)FrameStyles.Resolve(o.AnchorFrame, o.AnchorGradient);
        _workspace.SetGradientHex(o.AnchorGradientLeft, o.AnchorGradientRight);
        _workspace.ApplyScreenColors(o.AnchorScreenColors);
        _workspace.ApplyGlasses(o.GlassesBrightness, o.GlassesDimmer);
        _workspace.EndSettingsLoad();
        _workspace.WriteLive(_engine.LiveBackdrop!);
        _workspace.MarkScreenSettingsApplied();
        _workspace.ScreenCount = o.ScreenCount;
        _workspace.PrimaryScreenIndex = Math.Clamp(o.PrimaryScreen, 0, WorkspaceLayout.MaxScreens);
        _workspace.MoveWindowsFromGlasses = o.MoveWindowsFromGlasses;
        _workspace.UltrawideModeIndex = Math.Max(0, UltrawideModes.All.ToList().IndexOf(o.Mode));
        _workspace.PresetIndex = o.Preset == LayoutPreset.Grid ? 1 : 0;
        _workspace.DistanceMeters = o.DistanceMeters;
        _tracking.SourceIndex = o.Source switch { TrackingSource.Simulated => 0, TrackingSource.Fixed => 2, _ => 1 };
        _tracking.StabilizerIndex = o.Stabilizer switch { "off" => 0, "balanced" => 1, "ultra" => 3, _ => 2 };
        _tracking.NeckModel = o.NeckModel;
        _tracking.AxesIndex = (int)o.Axes;
        _tracking.PolicyIndex = o.AutoCenter.Policy == RecenterPolicy.Follow ? 1 : 0;
        _tracking.DeadZoneDegrees = o.AutoCenter.DeadZoneDegrees;
        _tracking.SmoothingSeconds = o.AutoCenter.SmoothingSeconds;
        _tracking.MaxFollowSpeed = o.AutoCenter.MaxFollowSpeedDegrees;
        _workspace.MarkScreenSettingsApplied();
    }

    private void ScheduleAutoSave()
    {
        if (!_autoSaveTimer.IsRunning)
        {
            _autoSaveTimer.Start();
        }
    }

    /// <summary>Saves the current settings when they differ from the last saved ones; returns them.</summary>
    private WorkspaceOptions SaveIfChanged()
    {
        var options = BuildOptions();
        if (options == _lastSaved)
        {
            return options;
        }

        try
        {
            _store.Save(options);
            _lastSaved = options;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("Settings", ex);
        }

        return options;
    }

    // ---- Saved workspaces ----

    public ObservableCollection<string> SavedWorkspaces { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadWorkspaceCommand), nameof(DeleteWorkspaceCommand))]
    public partial string? SelectedWorkspace { get; set; }

    /// <summary>Name for "Save"; follows the selected workspace, so Save overwrites it unless a new name is typed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveWorkspaceCommand))]
    public partial string WorkspaceName { get; set; } = string.Empty;

    partial void OnSelectedWorkspaceChanged(string? value)
    {
        if (value is not null)
        {
            WorkspaceName = value;
        }
    }

    private bool HasSelectedWorkspace => SelectedWorkspace is not null;

    private bool HasValidWorkspaceName => WorkspaceLibrary.NormalizeName(WorkspaceName) is not null;

    [RelayCommand(CanExecute = nameof(HasValidWorkspaceName))]
    private void SaveWorkspace()
    {
        string name = WorkspaceLibrary.NormalizeName(WorkspaceName)!;
        try
        {
            _library.Save(name, SaveIfChanged());
            RememberWorkspace(name);
            ShowInfo("Workspace saved", $"Saved as \"{name}\".");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Could not save the workspace", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWorkspace))]
    private async Task LoadWorkspaceAsync()
    {
        string name = SelectedWorkspace!;
        if (_library.Load(name) is not { } options)
        {
            ShowError("Could not load the workspace", $"\"{name}\" is missing or damaged.");
            RefreshSavedWorkspaces(null);
            return;
        }

        RememberWorkspace(name);
        await UseSettingsAsync(options, $"Loaded \"{name}\".").ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWorkspace))]
    private void DeleteWorkspace()
    {
        string name = SelectedWorkspace!;
        try
        {
            _library.Delete(name);
            RefreshSavedWorkspaces(null);
            WorkspaceName = string.Empty;
            ShowInfo("Workspace deleted", $"\"{name}\" was deleted.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Could not delete the workspace", ex.Message);
        }
    }

    /// <summary>Writes the current settings and all saved workspaces to <paramref name="path"/>.</summary>
    public void ExportSettings(string path)
    {
        try
        {
            _library.Export(path, SaveIfChanged());
            ShowInfo("Settings exported", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Could not export the settings", ex.Message);
        }
    }

    /// <summary>Adds the saved workspaces from <paramref name="path"/> and uses its current settings.</summary>
    public async Task ImportSettingsAsync(string path)
    {
        WorkspaceOptions options;
        try
        {
            options = _library.Import(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowError("Could not import the settings", ex.Message);
            return;
        }

        RefreshSavedWorkspaces(SelectedWorkspace);
        await UseSettingsAsync(options, $"Imported from {Path.GetFileName(path)}.").ConfigureAwait(true);
    }

    /// <summary>Shows <paramref name="options"/> in the UI, saves them, and restarts a running workspace with them.</summary>
    private async Task UseSettingsAsync(WorkspaceOptions options, string message)
    {
        ApplySettings(options);
        SaveIfChanged();
        _workspace.RefreshGlassesSignal();
        if (IsRunning)
        {
            StatusTitle = message;
            await RestartAsync().ConfigureAwait(true);
        }
        else
        {
            ShowInfo("Settings loaded", message + " They are used when you start the workspace.");
        }
    }

    private void RememberWorkspace(string name)
    {
        RefreshSavedWorkspaces(name);
        App.Preferences.LastWorkspaceName = name;
        App.Preferences.Save();
    }

    private void RefreshSavedWorkspaces(string? select)
    {
        SavedWorkspaces.Clear();
        foreach (string name in _library.List())
        {
            SavedWorkspaces.Add(name);
        }

        SelectedWorkspace = select is not null && SavedWorkspaces.Contains(select) ? select : null;
    }

    private void ShowInfo(string title, string message)
    {
        StatusTitle = title;
        StatusMessage = message;
        Severity = InfoBarSeverity.Informational;
    }

    private void ShowError(string title, string message)
    {
        StatusTitle = title;
        StatusMessage = message;
        Severity = InfoBarSeverity.Error;
    }
}

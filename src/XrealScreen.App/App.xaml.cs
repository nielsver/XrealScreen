using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using XrealScreen.App.Api;
using XrealScreen.App.Tray;
using XrealScreen.App.ViewModels;
using XrealScreen.Host;
using XrealScreen.Host.Api;

namespace XrealScreen.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "App lives for the whole process; the tray icon is disposed in ExitApp / window closing.")]
public partial class App : Application
{
    private TrayIcon? _tray;
    private AppApiHandler? _apiHandler;
    private LocalApiServer? _apiServer;
    private bool _exiting;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            CrashLog.Write("UI", e.Exception);
            e.Handled = true; // keep running; the session engine restores the desktop on its own
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("Task", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>App-wide view models (single window app).</summary>
    public static WorkspaceViewModel Workspace { get; private set; } = null!;

    public static TrackingViewModel Tracking { get; private set; } = null!;

    public static DeviceViewModel Device { get; private set; } = null!;

    public static SessionViewModel Session { get; private set; } = null!;

    public static HeadLookViewModel HeadLook { get; private set; } = null!;

    public static MainWindow MainWindow { get; private set; } = null!;

    public static AppPreferences Preferences { get; private set; } = null!;

    public static new App Current => (App)Application.Current;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        Preferences = AppPreferences.Load();
        Workspace = new WorkspaceViewModel();
        Tracking = new TrackingViewModel(dispatcher);
        Device = new DeviceViewModel();
        Session = new SessionViewModel(dispatcher, Workspace, Tracking);
        HeadLook = new HeadLookViewModel(dispatcher);

        MainWindow = new MainWindow();
        MainWindow.SetTheme(ThemeFromIndex(Preferences.Theme));
        MainWindow.ApplyLook((Looks.AppLook)Math.Clamp(Preferences.Look, 0, 3));
        MainWindow.AppWindow.Closing += OnWindowClosing;
        MainWindow.Closed += (_, _) => ShutDownSession();

        _tray = new TrayIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), "XrealScreen — workspace stopped");
        _tray.CommandInvoked += OnTrayCommand;
        Session.Notification += (_, n) => _tray?.ShowNotification(n.Title, n.Message);
        Session.PropertyChanged += (_, e) =>
        {
            // The tray is gone once the app is exiting, but stopping the session still raises State changes.
            if (e.PropertyName == nameof(SessionViewModel.State) && _tray is { } tray)
            {
                tray.WorkspaceRunning = Session.IsRunning;
                tray.SetTooltip(Session.IsRunning ? "XrealScreen — workspace running" : "XrealScreen — workspace stopped");
            }
        };

        if (Preferences.EnableLocalApi)
        {
            _ = SetLocalApiEnabledAsync(true);
        }

        // Started by Windows at sign-in: stay in the tray.
        if (!AppStartup.StartedByWindows)
        {
            MainWindow.Activate();
        }

        if (Preferences.StartWorkspaceOnLaunch)
        {
            _ = Session.StartOnLaunchAsync();
        }
    }

    /// <summary>Starts or stops the local API (ADR-0012; Settings → Developers).</summary>
    public async Task SetLocalApiEnabledAsync(bool enabled)
    {
        if (enabled && _apiServer is null)
        {
            _apiHandler = new AppApiHandler(DispatcherQueue.GetForCurrentThread());
            _apiServer = new LocalApiServer(_apiHandler);
            _apiServer.Log += message => MainWindow.DispatcherQueue.TryEnqueue(() => Session.LogMessage(message));
            _apiServer.Start();
        }
        else if (!enabled && _apiServer is { } server)
        {
            _apiServer = null;
            await server.DisposeAsync().ConfigureAwait(true);
            _apiHandler?.Dispose();
            _apiHandler = null;
        }
    }

    public static ElementTheme ThemeFromIndex(int index) => index switch
    {
        1 => ElementTheme.Light,
        2 => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    public void ShowMainWindow()
    {
        MainWindow.AppWindow.Show();
        if (MainWindow.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        MainWindow.Activate();
    }

    /// <summary>Quits for real: stops the workspace (restoring the desktop) and removes the tray icon.</summary>
    public void ExitApp()
    {
        _exiting = true;
        ShutDownSession();
        _tray?.Dispose();
        _tray = null;
        Exit();
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting || !Preferences.KeepRunningInTray)
        {
            _exiting = true;
            _tray?.Dispose();
            _tray = null;
            return;
        }

        // Keep running in the tray (the workspace keeps running too).
        args.Cancel = true;
        sender.Hide();
        if (!Preferences.TrayHintShown)
        {
            _tray?.ShowNotification("XrealScreen is still running", "Open it or start/stop the workspace from the tray icon, or press Ctrl+Alt+W.");
            Preferences.TrayHintShown = true;
            Preferences.Save();
        }
    }

    private void OnTrayCommand(TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.Open:
                ShowMainWindow();
                break;
            case TrayCommand.StartWorkspace:
                Session.StartCommand.Execute(null);
                break;
            case TrayCommand.StopWorkspace:
                Session.StopCommand.Execute(null);
                break;
            case TrayCommand.Recenter:
                Session.RecenterCommand.Execute(null);
                break;
            case TrayCommand.Screenshot:
                if (Session.TakeScreenshotCommand.CanExecute(null))
                {
                    Session.TakeScreenshotCommand.Execute(null);
                }

                break;
            case TrayCommand.ToggleWorkspace:
                if (Session.IsRunning)
                {
                    Session.StopCommand.Execute(null);
                }
                else if (Session.IsIdle)
                {
                    Session.StartCommand.Execute(null);
                }

                break;
            case TrayCommand.Exit:
                ExitApp();
                break;
        }
    }

    private bool _shutDown;

    private void ShutDownSession()
    {
        if (_shutDown)
        {
            return;
        }

        _shutDown = true;
        // Always hand the desktop back (virtual monitors, layout, refresh rates).
        // Disconnect API clients first so no command arrives while the session shuts down.
        if (_apiServer is { } server)
        {
            _apiServer = null;
            server.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
            _apiHandler?.Dispose();
        }

        HeadLook.Dispose();
        Session.Dispose();
        Tracking.Dispose();
    }
}

using Microsoft.UI.Dispatching;
using XrealScreen.Contracts;
using XrealScreen.Core.Tracking;
using XrealScreen.Host;
using XrealScreen.Host.Api;
using XrealScreen.Sdk;

namespace XrealScreen.App.Api;

/// <summary>
/// The app side of the local API (ADR-0012). Commands run on the UI thread, exactly like the buttons;
/// status is a snapshot refreshed on the UI thread every 250 ms, and poses are read from the running
/// workspace or head look directly (both thread-safe), so the server never waits for the UI.
/// </summary>
internal sealed class AppApiHandler : ILocalApiHandler, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly Timer _glassesTimer;
    private volatile StatusResult _status = new();
    private volatile GlassesStatus _glasses = new();

    public AppApiHandler(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _statusTimer = dispatcher.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(250);
        _statusTimer.Tick += (_, _) => _status = BuildStatus();
        _statusTimer.Start();
        _status = BuildStatus();

        // Display query (CCD) off the UI thread, every 2 s like the glasses-connect check.
        _glassesTimer = new Timer(_ => RefreshGlasses(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    public string AppVersion { get; } = typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public StatusResult GetStatus() => _status;

    public PoseData? GetLatestPose()
    {
        if (App.HeadLook.ActiveGlasses?.LatestPose is { } headLook)
        {
            return ToPoseData(headLook);
        }

        return App.Session.LivePose is { } workspace && QuaternionMath.IsValidRotation(workspace.Relative)
            ? ToPoseData(workspace)
            : null;
    }

    public Task<IReadOnlyList<string>> ListWorkspacesAsync(CancellationToken cancellationToken) =>
        OnUiAsync(() => Task.FromResult(App.Session.SavedWorkspaceNames));

    public Task StartWorkspaceAsync(string? name, CancellationToken cancellationToken) => OnUiAsync(async () =>
    {
        await App.Session.StartForApiAsync(name).ConfigureAwait(true);

        // The engine reports Running through the dispatcher shortly after StartAsync returns.
        for (int i = 0; i < 40 && App.Session.State == WorkspaceState.Starting; i++)
        {
            await Task.Delay(50, cancellationToken).ConfigureAwait(true);
        }

        if (App.Session.IsWaitingForGlassesMode)
        {
            throw new XrealScreenApiException(ApiErrorCodes.Unavailable, App.Session.StatusMessage);
        }

        if (!App.Session.IsRunning)
        {
            throw new XrealScreenApiException(ApiErrorCodes.Failed, $"{App.Session.StatusTitle}: {App.Session.StatusMessage}");
        }

        return true;
    });

    public Task StopWorkspaceAsync(CancellationToken cancellationToken) => OnUiAsync(async () =>
    {
        if (App.Session.StopCommand.CanExecute(null))
        {
            await App.Session.StopCommand.ExecuteAsync(null).ConfigureAwait(true);
        }

        return true;
    });

    public Task RecenterWorkspaceAsync(CancellationToken cancellationToken) => OnUiAsync(() =>
    {
        if (!App.Session.IsRunning)
        {
            throw new XrealScreenApiException(ApiErrorCodes.Conflict, "No workspace is running.");
        }

        App.Session.RecenterCommand.Execute(null);
        return Task.FromResult(true);
    });

    public async Task<string> TakeScreenshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GlassesScreenshot.SaveAsync(null, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            throw new XrealScreenApiException(ApiErrorCodes.Unavailable, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            throw new XrealScreenApiException(ApiErrorCodes.Failed, ex.Message);
        }
    }

    public Task StartHeadLookAsync(CancellationToken cancellationToken) => OnUiAsync(async () =>
    {
        if (!App.Session.IsIdle)
        {
            throw new XrealScreenApiException(ApiErrorCodes.Conflict, "Stop the workspace first: head look and a workspace cannot run at the same time.");
        }

        if (!App.HeadLook.IsRunning)
        {
            await App.HeadLook.StartCommand.ExecuteAsync(null).ConfigureAwait(true);
        }

        return true;
    });

    public Task StopHeadLookAsync(CancellationToken cancellationToken) => OnUiAsync(async () =>
    {
        if (App.HeadLook.IsRunning)
        {
            await App.HeadLook.StopCommand.ExecuteAsync(null).ConfigureAwait(true);
        }

        return true;
    });

    public Task SetHeadLookEnabledAsync(bool enabled, CancellationToken cancellationToken) => OnUiAsync(() =>
    {
        App.HeadLook.SetEnabled(enabled);
        return Task.FromResult(true);
    });

    public Task RecenterHeadLookAsync(CancellationToken cancellationToken) => OnUiAsync(() =>
    {
        if (!App.HeadLook.IsRunning)
        {
            throw new XrealScreenApiException(ApiErrorCodes.Conflict, "Head look is not started.");
        }

        App.HeadLook.RecenterCommand.Execute(null);
        return Task.FromResult(true);
    });

    public Task<HeadLookSettings> GetHeadLookSettingsAsync(CancellationToken cancellationToken) =>
        OnUiAsync(() => Task.FromResult(App.HeadLook.GetApiSettings()));

    public Task<HeadLookSettings> SetHeadLookSettingsAsync(HeadLookSettings changes, CancellationToken cancellationToken) => OnUiAsync(() =>
    {
        App.HeadLook.ApplyApiSettings(changes);
        return Task.FromResult(App.HeadLook.GetApiSettings());
    });

    public void Dispose()
    {
        _statusTimer.Stop();
        _glassesTimer.Dispose();
    }

    private StatusResult BuildStatus()
    {
        var session = App.Session;
        var headLook = App.HeadLook;
        string state = session.State switch
        {
            WorkspaceState.Starting => WorkspaceStates.Starting,
            WorkspaceState.Running => WorkspaceStates.Running,
            WorkspaceState.Stopping => WorkspaceStates.Stopping,
            WorkspaceState.Faulted => WorkspaceStates.Faulted,
            _ => WorkspaceStates.Stopped,
        };
        var status = new StatusResult
        {
            Workspace = new WorkspaceStatus { State = state, Name = App.Preferences.LastWorkspaceName, Message = session.StatusMessage },
            HeadLook = new HeadLookStatus { Running = headLook.IsRunning, Enabled = headLook.IsOn, Connected = headLook.ActiveGlasses?.IsConnected == true },
            Glasses = _glasses,
            PoseSource = headLook.ActiveGlasses is not null ? PoseSources.HeadLook : session.IsRunning ? PoseSources.Workspace : PoseSources.None,
        };

        // Keep the same instance when nothing changed (cheap equality for subscribers).
        return status == _status ? _status : status;
    }

    private void RefreshGlasses()
    {
        try
        {
            var g = GlassesDisplay.Find();
            _glasses = g is null
                ? new GlassesStatus()
                : new GlassesStatus { DisplayConnected = true, DisplayName = g.FriendlyName, Model = g.Model?.ToString() };
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _glasses = new GlassesStatus();
        }
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread and returns its result (or exception).</summary>
    private Task<T> OnUiAsync<T>(Func<Task<T>> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = _dispatcher.TryEnqueue(async () =>
        {
            try
            {
                tcs.TrySetResult(await action().ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        if (!queued)
        {
            tcs.TrySetException(new XrealScreenApiException(ApiErrorCodes.Unavailable, "XrealScreen is closing."));
        }

        return tcs.Task;
    }

    private static PoseData ToPoseData(GlassesPose p) => new()
    {
        TimestampNs = p.TimestampNs,
        Yaw = p.Yaw,
        Pitch = p.Pitch,
        Roll = p.Roll,
        Orientation = new QuaternionData { X = p.Orientation.X, Y = p.Orientation.Y, Z = p.Orientation.Z, W = p.Orientation.W },
        AngularVelocity = new Vector3Data { X = p.AngularVelocity.X, Y = p.AngularVelocity.Y, Z = p.AngularVelocity.Z },
        Source = PoseSources.HeadLook,
    };

    private static PoseData ToPoseData(HeadPose p)
    {
        const float RadToDeg = 180f / MathF.PI;
        return new PoseData
        {
            TimestampNs = p.TimestampNs,
            Yaw = p.YawDegrees,
            Pitch = p.PitchDegrees,
            Roll = p.RollDegrees,
            Orientation = new QuaternionData { X = p.Relative.X, Y = p.Relative.Y, Z = p.Relative.Z, W = p.Relative.W },
            AngularVelocity = new Vector3Data { X = p.AngularVelocity.X * RadToDeg, Y = p.AngularVelocity.Y * RadToDeg, Z = p.AngularVelocity.Z * RadToDeg },
            Source = PoseSources.Workspace,
        };
    }
}

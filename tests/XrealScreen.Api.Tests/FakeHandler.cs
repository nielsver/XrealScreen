using XrealScreen.Contracts;
using XrealScreen.Host.Api;

namespace XrealScreen.Api.Tests;

/// <summary>Scriptable app side for server tests.</summary>
internal sealed class FakeHandler : ILocalApiHandler
{
    private readonly Lock _lock = new();
    private PoseData? _pose;
    private StatusResult _status = new();
    private HeadLookSettings _settings = new()
    {
        Mode = HeadLookModes.Look, Sensitivity = 20, VerticalScale = 1, InvertY = false, Deadband = 0.5f,
        StickDeadZone = 5, StickFull = 25, StickSpeed = 1500, GlassesPrimary = true,
    };

    public string AppVersion => "9.9.9-test";

    public List<string> Calls { get; } = [];

    public string? StartedWorkspace { get; private set; }

    public bool? HeadLookEnabled { get; private set; }

    /// <summary>When set, <see cref="StartWorkspaceAsync"/> waits for it (to test concurrent requests).</summary>
    public TaskCompletionSource? StartGate { get; set; }

    /// <summary>When set, the next call throws it.</summary>
    public Exception? Throw { get; set; }

    public void SetPose(long timestampNs, float yaw)
    {
        lock (_lock)
        {
            _pose = new PoseData { TimestampNs = timestampNs, Yaw = yaw, Source = PoseSources.HeadLook };
        }
    }

    public void SetStatus(StatusResult status)
    {
        lock (_lock)
        {
            _status = status;
        }
    }

    public StatusResult GetStatus()
    {
        lock (_lock)
        {
            return _status;
        }
    }

    public PoseData? GetLatestPose()
    {
        lock (_lock)
        {
            return _pose;
        }
    }

    public Task<IReadOnlyList<string>> ListWorkspacesAsync(CancellationToken cancellationToken) =>
        Record<IReadOnlyList<string>>("list", ["Desk", "Games"]);

    public async Task StartWorkspaceAsync(string? name, CancellationToken cancellationToken)
    {
        await Record("start");
        if (name == "Missing")
        {
            throw new XrealScreenApiException(ApiErrorCodes.Unavailable, "No saved workspace \"Missing\".");
        }

        if (StartGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        StartedWorkspace = name ?? "(latest)";
    }

    public Task StopWorkspaceAsync(CancellationToken cancellationToken) => Record("stop");

    public Task RecenterWorkspaceAsync(CancellationToken cancellationToken) => Record("recenter");

    public Task<string> TakeScreenshotAsync(CancellationToken cancellationToken) => Record("screenshot", @"C:\Pictures\XrealScreen\shot.png");

    public Task StartHeadLookAsync(CancellationToken cancellationToken) => Record("headLook.start");

    public Task StopHeadLookAsync(CancellationToken cancellationToken) => Record("headLook.stop");

    public async Task SetHeadLookEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        await Record("headLook.setEnabled");
        HeadLookEnabled = enabled;
    }

    public Task RecenterHeadLookAsync(CancellationToken cancellationToken) => Record("headLook.recenter");

    public Task<HeadLookSettings> GetHeadLookSettingsAsync(CancellationToken cancellationToken) => Record("headLook.getSettings", _settings);

    public Task<HeadLookSettings> SetHeadLookSettingsAsync(HeadLookSettings changes, CancellationToken cancellationToken)
    {
        _settings = _settings with
        {
            Mode = changes.Mode ?? _settings.Mode,
            Sensitivity = changes.Sensitivity ?? _settings.Sensitivity,
            StickSpeed = changes.StickSpeed ?? _settings.StickSpeed,
        };
        return Record("headLook.setSettings", _settings);
    }

    private Task<object?> Record(string call) => Record<object?>(call, null);

    private Task<T> Record<T>(string call, T result)
    {
        lock (_lock)
        {
            Calls.Add(call);
        }

        if (Throw is { } ex)
        {
            Throw = null;
            return Task.FromException<T>(ex);
        }

        return Task.FromResult(result);
    }
}

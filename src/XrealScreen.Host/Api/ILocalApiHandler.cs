using XrealScreen.Contracts;

namespace XrealScreen.Host.Api;

/// <summary>
/// What the local API can do in the app (ADR-0012). Implemented by the app; <see cref="LocalApiServer"/>
/// calls it from thread-pool threads. Throw <see cref="XrealScreenApiException"/> to return an API error.
/// </summary>
public interface ILocalApiHandler
{
    /// <summary>App version for <see cref="ApiMethods.Hello"/>.</summary>
    string AppVersion { get; }

    /// <summary>Current state. Called often (status subscriptions poll it); must be cheap and thread-safe.</summary>
    StatusResult GetStatus();

    /// <summary>Newest pose, or null when nothing is tracking. Called at the subscribed rate; must be cheap and thread-safe.</summary>
    PoseData? GetLatestPose();

    /// <summary>Names of the saved workspaces.</summary>
    Task<IReadOnlyList<string>> ListWorkspacesAsync(CancellationToken cancellationToken);

    /// <summary>Starts the latest settings, or the saved workspace <paramref name="name"/>; returns when started.</summary>
    Task StartWorkspaceAsync(string? name, CancellationToken cancellationToken);

    /// <summary>Stops the workspace; returns when the monitors are restored.</summary>
    Task StopWorkspaceAsync(CancellationToken cancellationToken);

    /// <summary>Recenters the running workspace.</summary>
    Task RecenterWorkspaceAsync(CancellationToken cancellationToken);

    /// <summary>Saves what the glasses show; returns the PNG path.</summary>
    Task<string> TakeScreenshotAsync(CancellationToken cancellationToken);

    /// <summary>Starts head look (mouse output off).</summary>
    Task StartHeadLookAsync(CancellationToken cancellationToken);

    /// <summary>Stops head look.</summary>
    Task StopHeadLookAsync(CancellationToken cancellationToken);

    /// <summary>Mouse output on/off.</summary>
    Task SetHeadLookEnabledAsync(bool enabled, CancellationToken cancellationToken);

    /// <summary>Recenters head look.</summary>
    Task RecenterHeadLookAsync(CancellationToken cancellationToken);

    /// <summary>All head look settings.</summary>
    Task<HeadLookSettings> GetHeadLookSettingsAsync(CancellationToken cancellationToken);

    /// <summary>Applies the non-null members of <paramref name="changes"/>; returns all settings.</summary>
    Task<HeadLookSettings> SetHeadLookSettingsAsync(HeadLookSettings changes, CancellationToken cancellationToken);
}

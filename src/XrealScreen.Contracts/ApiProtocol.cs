namespace XrealScreen.Contracts;

/// <summary>Constants of the XrealScreen local API (see <c>docs/sdk/local-api.md</c>).</summary>
public static class ApiProtocol
{
    /// <summary>
    /// Named pipe of the running XrealScreen app: <c>\\.\pipe\XrealScreen.Api</c>. Only the Windows
    /// user who runs the app can open it.
    /// </summary>
    public const string PipeName = "XrealScreen.Api";

    /// <summary>API version reported by <see cref="ApiMethods.Hello"/>. Raised only for breaking changes.</summary>
    public const int Version = 1;

    /// <summary>Largest accepted message (one line of JSON) in bytes.</summary>
    public const int MaxMessageBytes = 64 * 1024;

    /// <summary>The <c>jsonrpc</c> member of every message.</summary>
    public const string JsonRpcVersion = "2.0";

    /// <summary>Highest pose rate a client can subscribe to.</summary>
    public const int MaxPoseRateHz = 250;
}

/// <summary>Method names (requests from a client to the app).</summary>
public static class ApiMethods
{
    /// <summary>Handshake: API and app version, list of methods. Params <see cref="HelloParams"/>, result <see cref="HelloResult"/>.</summary>
    public const string Hello = "api.hello";

    /// <summary>Current state. Result <see cref="StatusResult"/>.</summary>
    public const string StatusGet = "status.get";

    /// <summary>Send a <see cref="ApiNotifications.Status"/> notification now and whenever the state changes.</summary>
    public const string StatusSubscribe = "status.subscribe";

    /// <summary>Stop status notifications.</summary>
    public const string StatusUnsubscribe = "status.unsubscribe";

    /// <summary>Stream <see cref="ApiNotifications.Pose"/> notifications. Params <see cref="PoseSubscribeParams"/>.</summary>
    public const string PoseSubscribe = "pose.subscribe";

    /// <summary>Stop pose notifications.</summary>
    public const string PoseUnsubscribe = "pose.unsubscribe";

    /// <summary>Saved workspaces. Result <see cref="WorkspaceListResult"/>.</summary>
    public const string WorkspaceList = "workspace.list";

    /// <summary>Start the latest or a saved workspace. Params <see cref="WorkspaceStartParams"/>.</summary>
    public const string WorkspaceStart = "workspace.start";

    /// <summary>Stop the workspace and restore the monitors.</summary>
    public const string WorkspaceStop = "workspace.stop";

    /// <summary>Recenter the running workspace.</summary>
    public const string WorkspaceRecenter = "workspace.recenter";

    /// <summary>Save what the glasses show as a PNG. Result <see cref="ScreenshotResult"/>.</summary>
    public const string GlassesScreenshot = "glasses.screenshot";

    /// <summary>Start head look (Games page); mouse output stays off until <see cref="HeadLookSetEnabled"/> or Ctrl+Alt+H.</summary>
    public const string HeadLookStart = "headLook.start";

    /// <summary>Stop head look and restore the monitor layout.</summary>
    public const string HeadLookStop = "headLook.stop";

    /// <summary>Switch mouse output on or off. Params <see cref="SetEnabledParams"/>.</summary>
    public const string HeadLookSetEnabled = "headLook.setEnabled";

    /// <summary>Recenter head look.</summary>
    public const string HeadLookRecenter = "headLook.recenter";

    /// <summary>Head look settings. Result <see cref="HeadLookSettings"/>.</summary>
    public const string HeadLookGetSettings = "headLook.getSettings";

    /// <summary>Change head look settings; omitted members keep their value. Params and result <see cref="HeadLookSettings"/>.</summary>
    public const string HeadLookSetSettings = "headLook.setSettings";

    /// <summary>Every method of this API version.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Hello, StatusGet, StatusSubscribe, StatusUnsubscribe, PoseSubscribe, PoseUnsubscribe,
        WorkspaceList, WorkspaceStart, WorkspaceStop, WorkspaceRecenter, GlassesScreenshot,
        HeadLookStart, HeadLookStop, HeadLookSetEnabled, HeadLookRecenter, HeadLookGetSettings, HeadLookSetSettings,
    ];
}

/// <summary>Notification names (messages from the app without an id).</summary>
public static class ApiNotifications
{
    /// <summary>A head pose. Params <see cref="PoseData"/>.</summary>
    public const string Pose = "pose";

    /// <summary>The state changed. Params <see cref="StatusResult"/>.</summary>
    public const string Status = "status";
}

/// <summary>JSON-RPC error codes used by the API.</summary>
public static class ApiErrorCodes
{
    /// <summary>The line is not valid JSON, or is longer than <see cref="ApiProtocol.MaxMessageBytes"/>.</summary>
    public const int ParseError = -32700;

    /// <summary>Valid JSON but not a JSON-RPC 2.0 request.</summary>
    public const int InvalidRequest = -32600;

    /// <summary>Unknown method.</summary>
    public const int MethodNotFound = -32601;

    /// <summary>Missing or wrong parameters.</summary>
    public const int InvalidParams = -32602;

    /// <summary>Unexpected error in the app.</summary>
    public const int InternalError = -32603;

    /// <summary>Not possible right now, e.g. head look while a workspace runs.</summary>
    public const int Conflict = -32000;

    /// <summary>Something needed is missing, e.g. no glasses or no pose source.</summary>
    public const int Unavailable = -32001;

    /// <summary>The operation was tried and failed (the message says why).</summary>
    public const int Failed = -32002;
}

/// <summary>An API error: thrown by the client for error responses and by the app's handler to send one.</summary>
public sealed class XrealScreenApiException : Exception
{
    /// <summary>Creates an exception with an <see cref="ApiErrorCodes"/> code.</summary>
    public XrealScreenApiException(int code, string message)
        : base(message) => Code = code;

    /// <summary>Creates an exception with <see cref="ApiErrorCodes.Failed"/>.</summary>
    public XrealScreenApiException()
        : this(ApiErrorCodes.Failed, "The operation failed.")
    {
    }

    /// <summary>Creates an exception with <see cref="ApiErrorCodes.Failed"/>.</summary>
    public XrealScreenApiException(string message)
        : this(ApiErrorCodes.Failed, message)
    {
    }

    /// <summary>Creates an exception with <see cref="ApiErrorCodes.Failed"/>.</summary>
    public XrealScreenApiException(string message, Exception innerException)
        : base(message, innerException) => Code = ApiErrorCodes.Failed;

    /// <summary>Error code, see <see cref="ApiErrorCodes"/>.</summary>
    public int Code { get; }
}

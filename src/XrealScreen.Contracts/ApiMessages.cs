using System.Text.Json;
using System.Text.Json.Serialization;

namespace XrealScreen.Contracts;

/// <summary>
/// One JSON-RPC 2.0 message: a request (<see cref="Method"/> + <see cref="Id"/>), a notification
/// (<see cref="Method"/>, no id) or a response (<see cref="Id"/> + <see cref="Result"/> or <see cref="Error"/>).
/// </summary>
public sealed class RpcMessage
{
    /// <summary>Always <c>"2.0"</c>.</summary>
    [JsonPropertyName("jsonrpc")]
    public string? JsonRpc { get; set; } = ApiProtocol.JsonRpcVersion;

    /// <summary>Request id (number or string), echoed in the response. Absent in notifications.</summary>
    public JsonElement? Id { get; set; }

    /// <summary>Method or notification name.</summary>
    public string? Method { get; set; }

    /// <summary>Method or notification parameters (an object).</summary>
    public JsonElement? Params { get; set; }

    /// <summary>Successful result.</summary>
    public JsonElement? Result { get; set; }

    /// <summary>Error result.</summary>
    public RpcError? Error { get; set; }

    /// <summary>True for a request or notification.</summary>
    [JsonIgnore]
    public bool IsCall => Method is not null;

    /// <summary>True for a response.</summary>
    [JsonIgnore]
    public bool IsResponse => Method is null && (Result is not null || Error is not null);

    /// <summary>Creates a request.</summary>
    public static RpcMessage Request(long id, string method, JsonElement? parameters = null) =>
        new() { Id = JsonSerializer.SerializeToElement(id, ApiJsonContext.Default.Int64), Method = method, Params = parameters };

    /// <summary>Creates a notification.</summary>
    public static RpcMessage Notification(string method, JsonElement parameters) => new() { Method = method, Params = parameters };

    /// <summary>Creates a success response.</summary>
    public static RpcMessage Success(JsonElement? id, JsonElement result) => new() { Id = id, Result = result };

    /// <summary>Creates an error response.</summary>
    public static RpcMessage Failure(JsonElement? id, int code, string message) => new() { Id = id, Error = new RpcError { Code = code, Message = message } };
}

/// <summary>JSON-RPC error object. Codes: <see cref="ApiErrorCodes"/>.</summary>
public sealed class RpcError
{
    /// <summary>Error code, see <see cref="ApiErrorCodes"/>.</summary>
    public int Code { get; set; }

    /// <summary>User-readable description.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Params of <see cref="ApiMethods.Hello"/>.</summary>
public sealed record HelloParams
{
    /// <summary>Your program's name; shown in the app log.</summary>
    public string? ClientName { get; init; }
}

/// <summary>Result of <see cref="ApiMethods.Hello"/>.</summary>
public sealed record HelloResult
{
    /// <summary>API version, see <see cref="ApiProtocol.Version"/>.</summary>
    public int ApiVersion { get; init; }

    /// <summary>XrealScreen app version.</summary>
    public string AppVersion { get; init; } = string.Empty;

    /// <summary>Supported methods.</summary>
    public IReadOnlyList<string> Methods { get; init; } = [];
}

/// <summary>Where <see cref="ApiNotifications.Pose"/> notifications come from.</summary>
public static class PoseSources
{
    /// <summary>Nothing is tracking; no poses are sent.</summary>
    public const string None = "none";

    /// <summary>A running workspace (App head tracking or Glasses anchor).</summary>
    public const string Workspace = "workspace";

    /// <summary>Head look (Games page).</summary>
    public const string HeadLook = "headLook";
}

/// <summary>Workspace states in <see cref="WorkspaceStatus.State"/>.</summary>
public static class WorkspaceStates
{
    /// <summary>Not running.</summary>
    public const string Stopped = "stopped";

    /// <summary>Creating virtual monitors.</summary>
    public const string Starting = "starting";

    /// <summary>Running.</summary>
    public const string Running = "running";

    /// <summary>Restoring the monitors.</summary>
    public const string Stopping = "stopping";

    /// <summary>Stopped after an error; see <see cref="WorkspaceStatus.Message"/>.</summary>
    public const string Faulted = "faulted";
}

/// <summary>Result of <see cref="ApiMethods.StatusGet"/> and params of <see cref="ApiNotifications.Status"/>.</summary>
public sealed record StatusResult
{
    /// <summary>Workspace state.</summary>
    public WorkspaceStatus Workspace { get; init; } = new();

    /// <summary>Head look state.</summary>
    public HeadLookStatus HeadLook { get; init; } = new();

    /// <summary>The glasses as a monitor.</summary>
    public GlassesStatus Glasses { get; init; } = new();

    /// <summary>Where poses come from now, see <see cref="PoseSources"/>.</summary>
    public string PoseSource { get; init; } = PoseSources.None;
}

/// <summary>Workspace part of <see cref="StatusResult"/>.</summary>
public sealed record WorkspaceStatus
{
    /// <summary>One of <see cref="WorkspaceStates"/>.</summary>
    public string State { get; init; } = WorkspaceStates.Stopped;

    /// <summary>Saved workspace last loaded or started, if any.</summary>
    public string? Name { get; init; }

    /// <summary>The app's status line, e.g. why a start failed.</summary>
    public string? Message { get; init; }
}

/// <summary>Head look part of <see cref="StatusResult"/>.</summary>
public sealed record HeadLookStatus
{
    /// <summary>Head look is started (reading the glasses).</summary>
    public bool Running { get; init; }

    /// <summary>Mouse output is on.</summary>
    public bool Enabled { get; init; }

    /// <summary>Poses are arriving from the glasses.</summary>
    public bool Connected { get; init; }
}

/// <summary>Glasses part of <see cref="StatusResult"/>.</summary>
public sealed record GlassesStatus
{
    /// <summary>The glasses are connected as a monitor.</summary>
    public bool DisplayConnected { get; init; }

    /// <summary>Monitor name, e.g. <c>XREAL 1S</c>.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Glasses model, e.g. <c>XReal1S</c>, when known.</summary>
    public string? Model { get; init; }
}

/// <summary>Params of <see cref="ApiMethods.PoseSubscribe"/>.</summary>
public sealed record PoseSubscribeParams
{
    /// <summary>Notifications per second, 1 to <see cref="ApiProtocol.MaxPoseRateHz"/>. Default 60.</summary>
    public int RateHz { get; init; } = 60;
}

/// <summary>Params of <see cref="ApiNotifications.Pose"/>: one head pose. Angles in degrees.</summary>
/// <remarks>Yaw + = turned left, pitch + = looking up, roll + = tilted toward the right shoulder.</remarks>
public sealed record PoseData
{
    /// <summary>Glasses clock in nanoseconds.</summary>
    public long TimestampNs { get; init; }

    /// <summary>Degrees left (+) / right (−) of the center.</summary>
    public float Yaw { get; init; }

    /// <summary>Degrees up (+) / down (−).</summary>
    public float Pitch { get; init; }

    /// <summary>Degrees toward the right (+) / left (−) shoulder.</summary>
    public float Roll { get; init; }

    /// <summary>Orientation relative to the center as a unit quaternion (Z-up frame: X forward, Y left, Z up).</summary>
    public QuaternionData Orientation { get; init; } = new();

    /// <summary>Rotation speed in degrees per second, head frame (X forward, Y left, Z up).</summary>
    public Vector3Data AngularVelocity { get; init; } = new();

    /// <summary>Where the pose came from, see <see cref="PoseSources"/>.</summary>
    public string Source { get; init; } = PoseSources.None;
}

/// <summary>A quaternion.</summary>
public sealed record QuaternionData
{
    /// <summary>X component.</summary>
    public float X { get; init; }

    /// <summary>Y component.</summary>
    public float Y { get; init; }

    /// <summary>Z component.</summary>
    public float Z { get; init; }

    /// <summary>W (scalar) component.</summary>
    public float W { get; init; } = 1f;
}

/// <summary>A 3D vector.</summary>
public sealed record Vector3Data
{
    /// <summary>X component.</summary>
    public float X { get; init; }

    /// <summary>Y component.</summary>
    public float Y { get; init; }

    /// <summary>Z component.</summary>
    public float Z { get; init; }
}

/// <summary>Result of <see cref="ApiMethods.WorkspaceList"/>.</summary>
public sealed record WorkspaceListResult
{
    /// <summary>Names of the saved workspaces.</summary>
    public IReadOnlyList<string> Names { get; init; } = [];
}

/// <summary>Params of <see cref="ApiMethods.WorkspaceStart"/>.</summary>
public sealed record WorkspaceStartParams
{
    /// <summary>Saved workspace to load first; null starts with the latest settings.</summary>
    public string? Name { get; init; }
}

/// <summary>Result of <see cref="ApiMethods.GlassesScreenshot"/>.</summary>
public sealed record ScreenshotResult
{
    /// <summary>Full path of the saved PNG.</summary>
    public string Path { get; init; } = string.Empty;
}

/// <summary>Params of <see cref="ApiMethods.HeadLookSetEnabled"/>.</summary>
public sealed record SetEnabledParams
{
    /// <summary>True switches mouse output on.</summary>
    public bool Enabled { get; init; }
}

/// <summary>Result of methods that only report success.</summary>
public sealed record OkResult
{
    /// <summary>Always true.</summary>
    public bool Ok { get; init; } = true;
}

/// <summary>Head look modes in <see cref="HeadLookSettings.Mode"/>.</summary>
public static class HeadLookModes
{
    /// <summary>Head rotation turns the camera 1:1.</summary>
    public const string Look = "look";

    /// <summary>Head offset from center sets a turn speed.</summary>
    public const string Joystick = "joystick";
}

/// <summary>
/// Head look settings (Games page). In <see cref="ApiMethods.HeadLookSetSettings"/> every member is
/// optional; omitted members keep their value. Results always have every member.
/// </summary>
public sealed record HeadLookSettings
{
    /// <summary>One of <see cref="HeadLookModes"/>.</summary>
    public string? Mode { get; init; }

    /// <summary>Look: mouse steps per degree (1–100).</summary>
    public float? Sensitivity { get; init; }

    /// <summary>Up/down multiplier (0–2; 0 = left/right only).</summary>
    public float? VerticalScale { get; init; }

    /// <summary>Invert up/down.</summary>
    public bool? InvertY { get; init; }

    /// <summary>Look: ignore head rates below this, degrees per second (0–3).</summary>
    public float? Deadband { get; init; }

    /// <summary>Joystick: center zone in degrees (0–30).</summary>
    public float? StickDeadZone { get; init; }

    /// <summary>Joystick: degrees from center for full speed (5–60).</summary>
    public float? StickFull { get; init; }

    /// <summary>Joystick: mouse steps per second at full speed (100–5000).</summary>
    public float? StickSpeed { get; init; }

    /// <summary>Make the glasses the primary monitor while head look runs (applies at the next start).</summary>
    public bool? GlassesPrimary { get; init; }
}

/// <summary>System.Text.Json source generation for every API type (camelCase, nulls omitted).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RpcMessage))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(HelloParams))]
[JsonSerializable(typeof(HelloResult))]
[JsonSerializable(typeof(StatusResult))]
[JsonSerializable(typeof(PoseSubscribeParams))]
[JsonSerializable(typeof(PoseData))]
[JsonSerializable(typeof(WorkspaceListResult))]
[JsonSerializable(typeof(WorkspaceStartParams))]
[JsonSerializable(typeof(ScreenshotResult))]
[JsonSerializable(typeof(SetEnabledParams))]
[JsonSerializable(typeof(OkResult))]
[JsonSerializable(typeof(HeadLookSettings))]
public sealed partial class ApiJsonContext : JsonSerializerContext;

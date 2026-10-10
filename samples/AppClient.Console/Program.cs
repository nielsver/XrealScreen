// Talks to the running XrealScreen app through its local API.
//   dotnet run --project samples/AppClient.Console                    status, saved workspaces, settings
//   dotnet run --project samples/AppClient.Console -- poses           start head look (mouse off) and print poses for 10 s
//   dotnet run --project samples/AppClient.Console -- start <name>    start a saved workspace (no name: the latest settings)
//   dotnet run --project samples/AppClient.Console -- stop            stop the workspace
using XrealScreen.Contracts;
using XrealScreen.Sdk.Client;

try
{
    await using var app = await XrealScreenClient.ConnectAsync("AppClient sample");
    Console.WriteLine($"XrealScreen {app.ServerInfo.AppVersion}, API v{app.ServerInfo.ApiVersion}");

    switch (args.FirstOrDefault())
    {
        case "poses":
            await PrintPosesAsync(app);
            break;
        case "start":
            await app.StartWorkspaceAsync(args.ElementAtOrDefault(1));
            Console.WriteLine("Workspace running.");
            break;
        case "stop":
            await app.StopWorkspaceAsync();
            Console.WriteLine("Workspace stopped, monitors restored.");
            break;
        default:
            var status = await app.GetStatusAsync();
            Console.WriteLine($"Workspace: {status.Workspace.State}{(status.Workspace.Name is { } n ? $" ({n})" : "")}");
            Console.WriteLine($"Head look: {(status.HeadLook.Running ? status.HeadLook.Enabled ? "on" : "started, mouse off" : "stopped")}");
            Console.WriteLine($"Glasses:   {(status.Glasses.DisplayConnected ? $"{status.Glasses.DisplayName} ({status.Glasses.Model})" : "not connected as a display")}");
            Console.WriteLine($"Saved workspaces: {string.Join(", ", await app.ListWorkspacesAsync())}");
            var settings = await app.GetHeadLookSettingsAsync();
            Console.WriteLine($"Head look settings: mode {settings.Mode}, sensitivity {settings.Sensitivity}, joystick speed {settings.StickSpeed}");
            break;
    }

    return 0;
}
catch (XrealScreenApiException ex)
{
    Console.Error.WriteLine($"Error {ex.Code}: {ex.Message}");
    return 1;
}

static async Task PrintPosesAsync(XrealScreenClient app)
{
    var status = await app.GetStatusAsync();
    bool startedHeadLook = false;
    if (status.PoseSource == PoseSources.None)
    {
        // Head look gives poses without a workspace; its mouse output stays off.
        await app.StartHeadLookAsync();
        startedHeadLook = true;
        Console.WriteLine("Started head look (mouse output off) to get poses.");
    }

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    int count = 0;
    await foreach (var pose in app.ReadPosesAsync(rateHz: 10, cts.Token))
    {
        count++;
        Console.WriteLine($"[{pose.Source}] yaw {pose.Yaw,7:F1}°  pitch {pose.Pitch,7:F1}°  roll {pose.Roll,7:F1}°");
    }

    Console.WriteLine($"{count} poses in 10 s.");
    if (startedHeadLook)
    {
        await app.StopHeadLookAsync();
    }
}

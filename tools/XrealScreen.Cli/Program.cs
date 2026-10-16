using System.Globalization;
using System.CommandLine;
using XrealScreen.Cli;

// Numbers on the command line use "." regardless of the Windows locale (e.g. --latch-lead-ms 1.5).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var root = new RootCommand("xrs — XrealScreen developer/hardware tool (see docs/testing/hardware-test-M1.md)");
root.Subcommands.Add(DeviceCommands.CreateDevices());
root.Subcommands.Add(DeviceCommands.CreateProbe());
root.Subcommands.Add(DeviceCommands.CreateLink());
root.Subcommands.Add(ImuCommands.Create());
root.Subcommands.Add(DisplayCommands.Create());
root.Subcommands.Add(VddCommands.Create());
root.Subcommands.Add(DriverCommands.Create());
root.Subcommands.Add(CaptureCommands.Create());
root.Subcommands.Add(RenderCommands.Create());
var recover = new Command("recover", "Clean up after a crashed session: remove XrealScreen virtual monitors, restore layout and refresh rates.");
recover.SetAction(async (_, ct) =>
{
    Console.WriteLine(await XrealScreen.Host.WorkspaceRecovery.RecoverAsync(ct).ConfigureAwait(false) ?? "nothing to recover");
    return 0;
});
root.Subcommands.Add(recover);

return await root.Parse(args).InvokeAsync().ConfigureAwait(false);

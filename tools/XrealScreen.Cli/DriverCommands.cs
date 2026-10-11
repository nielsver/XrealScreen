using System.CommandLine;
using XrealScreen.Host;

namespace XrealScreen.Cli;

/// <summary>Install/remove the bundled virtual display driver (ADR-0008, ADR-0013); used by the MSI. Needs admin.</summary>
internal static class DriverCommands
{
    public static Command Create()
    {
        var driver = new Command("driver", "Install or remove the bundled virtual display driver (virtual-display-rs). Run as administrator.");
        driver.Subcommands.Add(CreateStatus());
        driver.Subcommands.Add(CreateInstall());
        driver.Subcommands.Add(CreateUninstall());
        return driver;
    }

    private static Command CreateStatus()
    {
        var command = new Command("status", "Show the virtual-display-rs device and whether XrealScreen installed it.");
        command.SetAction(_ =>
        {
            var status = DriverSetup.GetStatus();
            Console.WriteLine(status.DeviceExists
                ? $"device {status.InstanceId}  driver {status.DriverVersion ?? "?"}  inf {status.PublishedInf ?? "?"}"
                : "no virtual-display-rs device");
            Console.WriteLine($"installed by XrealScreen: {(status.InstalledByXrealScreen ? "yes" : "no")} ({DriverSetup.StateFile})");
            return 0;
        });
        return command;
    }

    private static Command CreateInstall()
    {
        var dir = new Option<DirectoryInfo>("--driver-dir")
        {
            Description = "Folder with VirtualDisplayDriver.inf and XrealScreenDriver.cer (default: 'driver' next to xrs.exe).",
            DefaultValueFactory = _ => new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "driver")),
        };
        var update = new Option<bool>("--update") { Description = "Replace an existing virtual-display-rs driver with the bundled one." };
        var command = new Command("install", "Trust the driver's test certificate, create the device and install the driver. An existing driver is kept unless --update.") { dir, update };
        command.SetAction(parse =>
        {
            var outcome = DriverSetup.Install(parse.GetValue(dir)!.FullName, parse.GetValue(update));
            Console.WriteLine(outcome switch
            {
                DriverInstallOutcome.KeptExisting => "OK virtual display driver already installed - kept (use --update to replace it).",
                DriverInstallOutcome.InstalledRebootRequired => "OK virtual display driver installed - restart Windows to finish.",
                _ => "OK virtual display driver installed.",
            });
            return 0;
        });
        return command;
    }

    private static Command CreateUninstall()
    {
        var command = new Command("uninstall", "Remove the device, driver package and certificate - only what XrealScreen installed.");
        command.SetAction(_ =>
        {
            Console.WriteLine(DriverSetup.Uninstall()
                ? "OK virtual display driver removed (device, driver package, driver certificate)."
                : "Driver: nothing installed by XrealScreen (any existing virtual display driver is left alone).");
            return 0;
        });
        return command;
    }
}

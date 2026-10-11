using System.Text;
using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// XrealScreen.msi (ADR-0013): per-machine, unpackaged self-contained app in Program Files, optional virtual display
// driver installed by "xrs driver" (DriverSetup). Arguments (from installer\Build-Msi.ps1):
//   --payload <dir>   publish folder (XrealScreen.App.exe, xrs.exe, optional driver\)
//   --license <file>  plain-text license shown in the wizard (LICENSE)
//   --version <x.y.z> product version (MSI compares major.minor.build)
//   -o <dir>          output folder (FalkForge names the file XrealScreen-<version>.msi)
// Signing (later, ADR-0013): set XRS_SIGN_THUMBPRINT (cert in CurrentUser\My) to sign the MSI.
//
// Wizard: Welcome → License → Install folder → [Virtual display driver] → progress → finish.

if (Arg("--payload") is not { } payloadArg || Arg("--license") is not { } licenseArg)
{
    Console.Error.WriteLine("""
        This tool BUILDS XrealScreen.msi; it is not the installer.
          Build the MSI:   .\installer\Build-Msi.ps1          (publishes the app, then runs this tool)
          Install:         artifacts\installer\XrealScreen-<version>-x64.msi
        Direct use: XrealScreen.Installer --payload <publish folder> --license <LICENSE> [--version x.y.z] [-o <output folder>]
        """);
    return 2;
}

string payload = Path.GetFullPath(payloadArg);
string license = Path.GetFullPath(licenseArg);
var version = Version.Parse(Arg("--version") ?? "0.1.0");
bool hasDriver = File.Exists(Path.Combine(payload, "driver", "VirtualDisplayDriver.inf"));

var installDir = KnownFolder.ProgramFiles64 / "XrealScreen";

return Installer.Build(args, p =>
{
    p.Name = "XrealScreen";
    p.Manufacturer = "Niklas Rydén";
    p.Version = version;
    p.Description = "Head-tracked multi-monitor workspace for XREAL glasses";
    p.HelpUrl = "https://github.com/nikryden/Screen-XREAL-1S";
    p.AboutUrl = "https://github.com/nikryden/Screen-XREAL-1S";
    // Never change: Windows Installer finds older versions to upgrade by this code.
    p.UpgradeCode = new Guid("d322530b-9c76-4b91-882d-db8616d6ea57");
    p.Scope = InstallScope.PerMachine;
    p.Architecture = ProcessorArchitecture.X64;
    p.DefaultInstallDirectory = installDir;
    p.ProductIcon(Path.Combine(payload, "Assets", "AppIcon.ico"));
    p.LicenseFile = WriteLicenseRtf(license);
    // The app usually runs in the tray: let Restart Manager close it (it restores the desktop on exit).
    p.EnableRestartManagerSupport();

    // Windows 11 check. Windows Installer reports VersionNT 603 on Windows 10/11 and has no WindowsBuildNumber
    // property (FalkForge's MsiProperty.WindowsBuildNumber / Condition.IsWindows11OrLater never match), so read the
    // build from the registry with AppSearch + RegLocator (FalkForge has no registry-search builder yet).
    p.CustomTable(t => t
        .Name("RegLocator")
        .Column("Signature_", CustomTableColumnType.String, c => c.PrimaryKey().Width(72))
        .Column("Root", CustomTableColumnType.Int16)
        .Column("Key", CustomTableColumnType.String, c => c.Width(255))
        .Column("Name", CustomTableColumnType.String, c => c.Width(255).Nullable())
        .Column("Type", CustomTableColumnType.Int16, c => c.Nullable())
        // Root 2 = HKLM, Type 2 = raw value, 0x10 = 64-bit view
        .Row(r => r.Set("Signature_", "WinBuildSearch").Set("Root", 2)
            .Set("Key", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion").Set("Name", "CurrentBuildNumber").Set("Type", 0x12)));
    p.CustomTable(t => t // AppSearch queries it for every signature; empty = registry-only search
        .Name("Signature")
        .Column("Signature", CustomTableColumnType.String, c => c.PrimaryKey().Width(72))
        .Column("FileName", CustomTableColumnType.String, c => c.Width(255))
        .Column("MinVersion", CustomTableColumnType.String, c => c.Width(20).Nullable())
        .Column("MaxVersion", CustomTableColumnType.String, c => c.Width(20).Nullable())
        .Column("MinSize", CustomTableColumnType.Int32, c => c.Nullable())
        .Column("MaxSize", CustomTableColumnType.Int32, c => c.Nullable())
        .Column("MinDate", CustomTableColumnType.Int32, c => c.Nullable())
        .Column("MaxDate", CustomTableColumnType.Int32, c => c.Nullable())
        .Column("Languages", CustomTableColumnType.String, c => c.Width(255).Nullable()));
    p.CustomTable(t => t
        .Name("AppSearch")
        .Column("Property", CustomTableColumnType.String, c => c.PrimaryKey().Width(72))
        .Column("Signature_", CustomTableColumnType.String, c => c.PrimaryKey().Width(72))
        .Row(r => r.Set("Property", "WINBUILD").Set("Signature_", "WinBuildSearch")));
    p.Require("Installed OR WINBUILD >= 22000", "XrealScreen needs Windows 11.");
    p.Require(Condition.Is64BitOS, "XrealScreen needs 64-bit Windows.");

    p.MajorUpgrade(mu => mu.Schedule(RemoveExistingProductsSchedule.AfterInstallValidate));
    p.Downgrade(d => d.Block("A newer version of XrealScreen is already installed."));

    p.Feature("App", f =>
    {
        f.Title = "XrealScreen";
        f.Description = "The XrealScreen app and the xrs command-line tool.";
        f.IsRequired = true;
        f.Files(files => files.FromDirectory(payload).To(installDir));
        f.Shortcut("XrealScreen", "XrealScreen.App.exe")
            .WithDescription("Head-tracked multi-monitor workspace for XREAL glasses")
            .OnStartMenu();
    });

    // "Start with Windows" writes HKCU\...\Run\XrealScreen (AppStartup); remove it for the installing user.
    p.RemoveRegistry(r => r
        .Id("RemoveRunValue")
        .Root(RegistryRoot.CurrentUser)
        .Key(@"Software\Microsoft\Windows\CurrentVersion\Run")
        .Name("XrealScreen")
        .RemoveValue());

    if (hasDriver)
    {
        // The InstallDir page's last button becomes "Next"; the driver page has "Install".
        p.UseDialogSet(MsiDialogSet.InstallDir, ui => ui.OverrideButtonLabel(DialogButton.Install, "&Next"));
        AddDriver(p);
    }
    else
    {
        p.UseDialogSet(MsiDialogSet.InstallDir);
    }

    if (Environment.GetEnvironmentVariable("XRS_SIGN_THUMBPRINT") is { Length: > 0 } thumbprint)
    {
        p.Signing(s => s.Thumbprint(thumbprint).Timestamp("http://timestamp.digicert.com"));
    }
}, new MsiCompiler());

// Virtual display driver (virtual-display-rs, ADR-0008): a wizard page with a check box (INSTALLDRIVER, on by
// default) and deferred, elevated custom actions that run the installed xrs.exe. DriverSetup keeps an existing driver
// and removes only what it installed. Silent installs: INSTALLDRIVER=0 skips the driver.
// (FalkForge cannot insert a page into a stock wizard from outside, so the page runs after the wizard ends.)
static void AddDriver(PackageBuilder p)
{
    p.Property("INSTALLDRIVER", "1", b => b.IsSecure = true);

    p.AddCustomDialog("DriverDlg", dlg => dlg
        .Title("[ProductName] Setup")
        .Size(370, 270)
        .FirstControl("DriverCheck")
        .DefaultControl("InstallButton")
        .CancelControl("CancelButton")
        .Text("Title", 15, 6, 280, 15, "{\\DlgFontBold8}Virtual display driver", b => b.Transparent().NoPrefix())
        .Text("Description", 25, 23, 280, 15, "XrealScreen creates its virtual screens with this driver.", b => b.Transparent().NoPrefix())
        .Line("BannerLine", 0, 44, 370)
        .Text("Body", 20, 54, 330, 40,
            "virtual-display-rs (MIT license) adds virtual monitors to Windows. It is signed with a test "
            + "certificate, which Setup trusts on this PC while the driver is installed.",
            b => b.NoPrefix())
        .Text("Existing", 20, 98, 330, 26,
            "An already installed virtual-display-rs driver (for example from VertoXR) is kept.",
            b => b.NoPrefix())
        .Text("Without", 20, 128, 330, 26,
            "Without the driver XrealScreen cannot create virtual screens; head look for games still works.",
            b => b.NoPrefix())
        .CheckBox("DriverCheck", 20, 166, 330, 18, property: "INSTALLDRIVER",
            text: "&Install the virtual display driver (recommended)",
            configure: b => b.Next("BackButton"))
        .Line("BottomLine", 0, 234, 370)
        .PushButton("BackButton", 180, 243, 56, 17, "< &Back", b => b
            .Next("InstallButton")
            .NavigateTo("InstallDirDlg"))
        .PushButton("InstallButton", 236, 243, 56, 17, "&Install", b => b
            .Next("CancelButton")
            .EndDialog("Return"))
        .PushButton("CancelButton", 304, 243, 56, 17, "Cancel", b => b
            .Next("DriverCheck")
            .SpawnDialog("CancelDlg")));

    // After the stock wizard (WelcomeDlg starts it at 1100), before the progress dialog; first install only.
    p.UISequence(seq => seq
        .Action("DriverDlg")
            .After("WelcomeDlg")
            .Condition("NOT Installed"));

    // MSI expands [INSTALLDIR] when the deferred action is scheduled. No other square brackets in these scripts.
    const string Xrs = "& '[INSTALLDIR]xrs.exe'";
    p.CustomAction("XrsDriverInstall", ca =>
    {
        ca.PowerShellScript($"{Xrs} driver install --driver-dir '[INSTALLDIR]driver'; exit $LASTEXITCODE");
        ca.Deferred();
        ca.NoImpersonate();
    });
    p.CustomAction("XrsDriverInstallRollback", ca =>
    {
        ca.PowerShellScript($"{Xrs} driver uninstall; exit 0");
        ca.Rollback();
        ca.NoImpersonate();
    });
    p.CustomAction("XrsDriverUninstall", ca =>
    {
        ca.PowerShellScript($"{Xrs} driver uninstall; exit 0");
        ca.Deferred();
        ca.NoImpersonate();
        ca.ContinueOnError();
    });

    // Install on first install and on upgrades (an existing device is kept); uninstall only on a real removal
    // (a major upgrade removes the old product first, and the driver must survive that).
    p.ExecuteSequence(seq => seq
        .Action("XrsDriverInstallRollback")
            .After("InstallFiles")
            .Condition("INSTALLDRIVER=\"1\" AND NOT Installed")
        .Action("XrsDriverInstall")
            .After("XrsDriverInstallRollback")
            .Condition("INSTALLDRIVER=\"1\" AND NOT Installed")
        .Action("XrsDriverUninstall")
            .Before("RemoveFiles")
            .Condition("REMOVE~=\"ALL\" AND NOT UPGRADINGPRODUCTCODE"));
}

// The license page shows RTF: wrap the plain-text LICENSE (paragraphs = blank-line separated).
static string WriteLicenseRtf(string textFile)
{
    var rtf = new StringBuilder(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fswiss Segoe UI;}}\viewkind4\uc1\pard\f0\fs17 ");
    foreach (string paragraph in File.ReadAllText(textFile).Replace("\r\n", "\n").Split("\n\n"))
    {
        foreach (char c in paragraph.Replace('\n', ' ').Trim())
        {
            rtf.Append(c switch
            {
                '\\' or '{' or '}' => "\\" + c,
                > (char)127 => $"\\u{(int)c}?",
                _ => c.ToString(),
            });
        }

        rtf.Append(@"\par\par ");
    }

    rtf.Append('}');
    string path = Path.Combine(Path.GetTempPath(), "XrealScreen-License.rtf");
    File.WriteAllText(path, rtf.ToString(), Encoding.ASCII);
    return path;
}

string? Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

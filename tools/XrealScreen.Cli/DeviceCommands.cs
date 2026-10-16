using System.CommandLine;
using System.Diagnostics;
using XrealScreen.Core.Devices;
using XrealScreen.Device.XrealOne;

namespace XrealScreen.Cli;

internal static class DeviceCommands
{
    public static Command CreateDevices()
    {
        var command = new Command("devices", "List known XREAL product IDs and their source.");
        command.SetAction(_ =>
        {
            Console.WriteLine($"Vendor ID 0x{GlassesCatalog.XrealVendorId:X4}");
            foreach (var p in GlassesCatalog.Products)
            {
                Console.WriteLine($"  PID 0x{p.ProductId:X4}  {p.Model,-10} {p.Transport,-8} {p.Source}");
            }

            return 0;
        });
        return command;
    }

    public static Command CreateProbe()
    {
        var host = new Option<string>("--host") { Description = "Glasses IP.", DefaultValueFactory = _ => OneEndpoints.Default.Host };
        var seconds = new Option<double>("--seconds") { Description = "How long to sample the IMU stream.", DefaultValueFactory = _ => 2.0 };
        var command = new Command("probe", "Check USB network adapter, TCP ports and IMU stream of connected One-series glasses.") { host, seconds };
        command.SetAction(async (parse, ct) =>
        {
            var endpoints = OneEndpoints.Default with { Host = parse.GetValue(host)! };
            return await ProbeAsync(endpoints, parse.GetValue(seconds), ct).ConfigureAwait(false);
        });
        return command;
    }

    public static Command CreateLink()
    {
        var tracking = new Option<bool>("--tracking") { Description = "Send the head-tracking picture instead of the glasses-anchor picture." };
        var setup = new Command("setup", "Send the workspace picture mode to the glasses, then run the same checks.") { tracking };
        setup.SetAction(async (parse, ct) => await PrintLinkAsync(sendSetup: true, parse.GetValue(tracking), ct).ConfigureAwait(false));

        var command = new Command("link", "Check the glasses picture, USB network, ports, firmware and a short IMU sample.") { setup };
        command.SetAction(async (_, ct) => await PrintLinkAsync(sendSetup: false, tracking: false, ct).ConfigureAwait(false));
        return command;
    }

    private static async Task<int> PrintLinkAsync(bool sendSetup, bool tracking, CancellationToken ct)
    {
        var report = sendSetup
            ? await XrealScreen.Host.GlassesLink.SendSetupAsync(!tracking, ct).ConfigureAwait(false)
            : await XrealScreen.Host.GlassesLink.CheckAsync(glassesAnchor: true, ct).ConfigureAwait(false);
        Console.WriteLine(report.Title);
        foreach (string line in report.Lines)
        {
            Console.WriteLine("  " + line);
        }

        return report.Title is "Glasses not connected" ? 2 : 0;
    }

    private static async Task<int> ProbeAsync(OneEndpoints endpoints, double seconds, CancellationToken ct)
    {
        Console.WriteLine("1) USB network adapter (expect PC address 169.254.2.x)");
        var adapters = NcmAdapterLocator.FindCandidates(endpoints.Host);
        if (adapters.Count == 0)
        {
            Console.WriteLine("   none found — are the glasses plugged in and is the adapter enabled? (Get-NetAdapter)");
        }

        foreach (var a in adapters)
        {
            Console.WriteLine($"   {a.Name} | {a.Description} | {a.LocalAddress} | {a.Status}");
        }

        Console.WriteLine($"2) TCP ports on {endpoints.Host}");
        var open = new List<string>();
        foreach (var (name, port) in endpoints.AllPorts())
        {
            var elapsed = await NcmAdapterLocator.TryConnectAsync(endpoints.Host, port, TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            Console.WriteLine($"   {port} {name,-9} {(elapsed is null ? "closed/unreachable" : $"open ({elapsed.Value.TotalMilliseconds:F0} ms)")}");
            if (elapsed is not null)
            {
                open.Add(name);
            }
        }

        if (!open.Contains("stream"))
        {
            Console.WriteLine("RESULT: IMU stream port not reachable. Record this in docs/findings/xreal-one-protocol.md.");
            return 2;
        }

        Console.WriteLine($"3) IMU stream for {seconds:F1} s");
        var source = new XrealOneImuSource(new XrealOneImuOptions { Endpoints = endpoints });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        long samples = 0;
        long firstNs = 0, lastNs = 0;
        var sw = Stopwatch.StartNew();
        try
        {
            await foreach (var s in source.ReadAsync(cts.Token).ConfigureAwait(false))
            {
                if (samples++ == 0)
                {
                    firstNs = s.TimestampNs;
                    Console.WriteLine($"   first sample: gyro={s.Gyro} accel={s.Accel} |a|={s.Accel.Length():F3} temp={s.TemperatureC:F1}");
                }

                lastNs = s.TimestampNs;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
        }
        catch (IOException ex)
        {
            Console.WriteLine($"   stream error: {ex.Message}");
        }

        double hostRate = samples / sw.Elapsed.TotalSeconds;
        double deviceSpan = (lastNs - firstNs) / 1e9;
        Console.WriteLine($"   framer: {source.Framer.Stats}");
        Console.WriteLine($"   samples={samples} hostRate={hostRate:F0}/s deviceClockSpan={deviceSpan:F3}s (compare with {sw.Elapsed.TotalSeconds:F3}s wall)");
        Console.WriteLine(samples > 0
            ? "RESULT: IMU stream decoded. Next: `xrs imu record`."
            : "RESULT: connected but no IMU reports decoded. Capture raw bytes with `xrs imu record` and analyse.");
        return samples > 0 ? 0 : 3;
    }

}

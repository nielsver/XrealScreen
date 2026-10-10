using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using XrealScreen.Core.Abstractions;
using XrealScreen.Core.Recording;
using XrealScreen.Core.Tracking;
using XrealScreen.Device.Simulated;
using XrealScreen.Device.XrealOne;

namespace XrealScreen.Cli;

internal static class ImuCommands
{
    public static Command Create()
    {
        var imu = new Command("imu", "Record, decode, replay and synthesize IMU data.");
        imu.Subcommands.Add(CreateRecord());
        imu.Subcommands.Add(CreateDecode());
        imu.Subcommands.Add(CreateLive());
        imu.Subcommands.Add(CreateReplay());
        imu.Subcommands.Add(CreateSynth());
        imu.Subcommands.Add(HeadMouseCommands.Create());
        return imu;
    }

    private static Command CreateRecord()
    {
        var seconds = new Option<double>("--seconds") { Description = "Recording length.", DefaultValueFactory = _ => 30 };
        var output = new Option<string>("--out") { Description = "Output .xrcap path (a decoded .xrimu is written next to it).", Required = true };
        var note = new Option<string>("--note") { Description = "Free text stored in metadata (motion, firmware...).", DefaultValueFactory = _ => "" };
        var host = new Option<string>("--host") { DefaultValueFactory = _ => OneEndpoints.Default.Host };
        var command = new Command("record", "Record raw stream bytes (.xrcap) and decoded samples (.xrimu) from the glasses.") { seconds, output, note, host };
        command.SetAction(async (parse, ct) =>
        {
            string path = parse.GetValue(output)!;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string meta = $"created={DateTime.UtcNow:O} host={parse.GetValue(host)} machine={Environment.MachineName} note={parse.GetValue(note)}";
            await using var cap = await XrcapWriter.CreateAsync(path, meta, ct).ConfigureAwait(false);
            var options = new XrealOneImuOptions
            {
                Endpoints = OneEndpoints.Default with { Host = parse.GetValue(host)! },
                RawTap = (t, data, token) => cap.WriteAsync(t, data, token),
            };
            var source = new XrealOneImuSource(options);
            string imuPath = Path.ChangeExtension(path, ".xrimu");
            var metadata = new Dictionary<string, string> { ["source"] = "xrs-record", ["note"] = parse.GetValue(note)!, ["axes"] = $"gyro={options.GyroAxes};accel={options.AccelAxes}" };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(parse.GetValue(seconds)));
            Console.WriteLine($"Recording {parse.GetValue(seconds)} s from {source.Name} → {path}");
            try
            {
                await XrimuFile.WriteAsync(imuPath, source.ReadAsync(cts.Token), metadata, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
            }

            Console.WriteLine($"framer: {source.Framer.Stats}");
            Console.WriteLine($"wrote {path} and {imuPath}");
            return 0;
        });
        return command;
    }

    private static Command CreateDecode()
    {
        var input = new Argument<string>("xrcap") { Description = "Raw capture to decode." };
        var output = new Option<string?>("--out") { Description = "Output .xrimu (default: next to input)." };
        var gyroAxes = new Option<string>("--gyro-axes") { DefaultValueFactory = _ => XrealOneImuOptions.SensorToBody.ToString() };
        var accelAxes = new Option<string>("--accel-axes") { DefaultValueFactory = _ => XrealOneImuOptions.SensorToBody.ToString() };
        var command = new Command("decode", "Re-decode a raw .xrcap with the current parser (use after protocol changes).") { input, output, gyroAxes, accelAxes };
        command.SetAction(async (parse, ct) =>
        {
            string path = parse.GetValue(input)!;
            string outPath = parse.GetValue(output) ?? Path.ChangeExtension(path, ".xrimu");
            var options = new XrealOneImuOptions { GyroAxes = AxisMap.Parse(parse.GetValue(gyroAxes)!), AccelAxes = AxisMap.Parse(parse.GetValue(accelAxes)!) };
            var framer = new OneReportFramer();
            var samples = new List<ImuSample>();
            await foreach (var chunk in XrcapReader.ReadAsync(path, ct).ConfigureAwait(false))
            {
                framer.Append(chunk.Data, r =>
                {
                    if (r.Type == OneReportType.Imu)
                    {
                        samples.Add(XrealOneImuSource.ToSample(r, options));
                    }
                });
            }

            var metadata = new Dictionary<string, string> { ["source"] = "xrs-decode", ["from"] = Path.GetFileName(path), ["axes"] = $"gyro={options.GyroAxes};accel={options.AccelAxes}" };
            await XrimuFile.WriteAsync(outPath, samples.ToAsyncEnumerable(), metadata, ct).ConfigureAwait(false);
            Console.WriteLine($"framer: {framer.Stats}");
            Console.WriteLine($"capture metadata: {XrcapReader.ReadMetadata(path)}");
            Console.WriteLine($"wrote {samples.Count} samples → {outPath}");
            return 0;
        });
        return command;
    }

    private static Command CreateLive()
    {
        var seconds = new Option<double>("--seconds") { DefaultValueFactory = _ => 60 };
        var host = new Option<string>("--host") { DefaultValueFactory = _ => OneEndpoints.Default.Host };
        var command = new Command("live", "Print fused head pose from the glasses (press Ctrl+C to stop; 'r' + Enter recenters).") { seconds, host };
        command.SetAction(async (parse, ct) =>
        {
            var source = new XrealOneImuSource(new XrealOneImuOptions { Endpoints = OneEndpoints.Default with { Host = parse.GetValue(host)! } });
            return await RunTrackerAsync(source, TimeSpan.FromSeconds(parse.GetValue(seconds)), printEvery: TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
        });
        return command;
    }

    private static Command CreateReplay()
    {
        var input = new Argument<string>("xrimu");
        var realTime = new Option<bool>("--realtime") { Description = "Pace by timestamps." };
        var command = new Command("replay", "Run the head tracker over a recorded .xrimu and print the pose.") { input, realTime };
        command.SetAction(async (parse, ct) =>
        {
            var source = new XrimuReplaySource(parse.GetValue(input)!, parse.GetValue(realTime));
            return await RunTrackerAsync(source, Timeout.InfiniteTimeSpan, printEvery: TimeSpan.FromMilliseconds(500), ct, useDeviceClock: true).ConfigureAwait(false);
        });
        return command;
    }

    private static Command CreateSynth()
    {
        var output = new Option<string>("--out") { Required = true };
        var motion = new Option<SyntheticMotion>("--motion") { DefaultValueFactory = _ => SyntheticMotion.YawSweep };
        var seconds = new Option<double>("--seconds") { DefaultValueFactory = _ => 10 };
        var rate = new Option<int>("--rate") { DefaultValueFactory = _ => 1000 };
        var command = new Command("synth", "Write a synthetic .xrimu (for tests and demos without glasses).") { output, motion, seconds, rate };
        command.SetAction(async (parse, ct) =>
        {
            var options = new SyntheticImuOptions { Motion = parse.GetValue(motion), DurationSeconds = parse.GetValue(seconds), RateHz = parse.GetValue(rate) };
            var metadata = new Dictionary<string, string> { ["source"] = "xrs-synth", ["motion"] = options.Motion.ToString(), ["rate"] = options.RateHz.ToString(CultureInfo.InvariantCulture) };
            await XrimuFile.WriteAsync(parse.GetValue(output)!, new SyntheticImuSource(options).ReadAsync(ct), metadata, ct).ConfigureAwait(false);
            Console.WriteLine($"wrote {parse.GetValue(output)}");
            return 0;
        });
        return command;
    }

    private static async Task<int> RunTrackerAsync(IImuSource source, TimeSpan duration, TimeSpan printEvery, CancellationToken ct, bool useDeviceClock = false)
    {
        var tracker = new HeadTracker();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (duration != Timeout.InfiniteTimeSpan)
        {
            cts.CancelAfter(duration);
        }

        if (!Console.IsInputRedirected)
        {
            _ = Task.Run(() =>
            {
                while (!cts.IsCancellationRequested && Console.ReadLine() is { } line)
                {
                    if (line.Trim().Equals("r", StringComparison.OrdinalIgnoreCase))
                    {
                        tracker.RequestRecenter();
                    }
                }
            }, cts.Token);
        }

        Console.WriteLine($"Source: {source.Name}");
        long nextPrint = 0;
        var wall = Stopwatch.StartNew();
        try
        {
            await foreach (var sample in source.ReadAsync(cts.Token).ConfigureAwait(false))
            {
                var pose = tracker.Update(sample);
                long now = useDeviceClock ? sample.TimestampNs / 100 : wall.Elapsed.Ticks;
                if (now >= nextPrint)
                {
                    nextPrint = now + printEvery.Ticks;
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"t={sample.TimestampNs / 1e9,9:F3}s yaw={pose.YawDegrees,7:F1} pitch={pose.PitchDegrees,7:F1} roll={pose.RollDegrees,7:F1} bias={tracker.GyroBias}"));
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        Console.WriteLine($"samples={tracker.SampleCount}");
        return 0;
    }
}

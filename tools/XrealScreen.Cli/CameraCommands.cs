using System.CommandLine;
using System.Diagnostics;
using XrealScreen.Device.XrealOne;

namespace XrealScreen.Cli;

/// <summary>XREAL Eye grayscale feed (camera port 52997), M9 E0. Recordings show the room: keep them out of the repo.</summary>
internal static class CameraCommands
{
    public static Command Create()
    {
        var camera = new Command("camera", "XREAL Eye camera feed (grayscale, 60 fps): snapshot, record, live statistics.");
        camera.Subcommands.Add(CreateSnapshot());
        camera.Subcommands.Add(CreateRecord());
        camera.Subcommands.Add(CreateLive());
        return camera;
    }

    private static Option<string> HostOption() => new("--host") { Description = "Glasses IP.", DefaultValueFactory = _ => OneEndpoints.Default.Host };

    private static XrealEyeCameraSource CreateSource(string host) =>
        new(new XrealEyeCameraOptions { Endpoints = OneEndpoints.Default with { Host = host } });

    private static Command CreateSnapshot()
    {
        var output = new Option<string>("--out") { Description = "PNG path (default: Pictures\\XrealScreen\\Eye <time>.png)." };
        var host = HostOption();
        var command = new Command("snapshot", "Save one camera frame as a PNG.") { output, host };
        command.SetAction(async (parse, ct) =>
        {
            string path = parse.GetValue(output) ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "XrealScreen", $"Eye {DateTime.Now:yyyy-MM-dd HHmmss}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await foreach (var frame in CreateSource(parse.GetValue(host)!).ReadAsync(cts.Token).ConfigureAwait(false))
                {
                    using (frame)
                    {
                        GrayPng.Write(path, frame.Width, frame.Height, frame.ToPackedArray());
                        Console.WriteLine($"{frame.Width}x{frame.Height} frame (device {frame.TimestampNs / 1e9:F3} s) → {path}");
                        return 0;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                Console.WriteLine($"No camera frame: {ex.Message}. Is the XREAL Eye attached?");
            }

            return 2;
        });
        return command;
    }

    private static Command CreateRecord()
    {
        var seconds = new Option<double>("--seconds") { Description = "Recording length.", DefaultValueFactory = _ => 10 };
        var output = new Option<string>("--out") { Description = "Output .xrcam path.", Required = true };
        var every = new Option<int>("--every") { Description = "Keep every Nth frame (1 = 60 fps, 2 = 30 fps). ~11 MB/s at 1.", DefaultValueFactory = _ => 2 };
        var note = new Option<string>("--note") { Description = "Free text stored in metadata.", DefaultValueFactory = _ => "" };
        var host = HostOption();
        var command = new Command("record", "Record camera frames to an .xrcam file (shows your surroundings: keep it private).") { seconds, output, every, note, host };
        command.SetAction(async (parse, ct) =>
        {
            string path = parse.GetValue(output)!;
            int keep = Math.Max(1, parse.GetValue(every));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string meta = $"created={DateTime.UtcNow:O} every={keep} machine={Environment.MachineName} note={parse.GetValue(note)}";
            await using var writer = await XrcamWriter.CreateAsync(path, meta, ct).ConfigureAwait(false);
            var source = CreateSource(parse.GetValue(host)!);
            Console.WriteLine($"Recording {parse.GetValue(seconds)} s (every {keep}. frame) → {path}");
            var stats = await RunAsync(source, parse.GetValue(seconds), async (frame, index) =>
            {
                if (index % keep == 0)
                {
                    await writer.WriteAsync(frame, DateTime.UtcNow, ct).ConfigureAwait(false);
                }
            }, ct).ConfigureAwait(false);
            Console.WriteLine($"wrote {writer.FramesWritten} frames");
            return stats.Print(source);
        });
        return command;
    }

    private static Command CreateLive()
    {
        var seconds = new Option<double>("--seconds") { Description = "How long to measure (300 = the E0 five-minute check).", DefaultValueFactory = _ => 10 };
        var host = HostOption();
        var command = new Command("live", "Receive the feed and report frame rate, gaps and brightness once per second.") { seconds, host };
        command.SetAction(async (parse, ct) =>
        {
            var source = CreateSource(parse.GetValue(host)!);
            long lastPrint = 0;
            var sw = Stopwatch.StartNew();
            var stats = await RunAsync(source, parse.GetValue(seconds), (frame, index) =>
            {
                if (sw.ElapsedMilliseconds - lastPrint >= 1000)
                {
                    lastPrint = sw.ElapsedMilliseconds;
                    long sum = 0;
                    foreach (byte b in frame.Pixels)
                    {
                        sum += b;
                    }

                    Console.WriteLine($"  {sw.Elapsed.TotalSeconds,6:F1} s  frames={index + 1,6}  {frame.Width}x{frame.Height}  mean={sum / (double)frame.Pixels.Length,5:F1}");
                }

                return ValueTask.CompletedTask;
            }, ct).ConfigureAwait(false);
            return stats.Print(source);
        });
        return command;
    }

    private sealed class FeedStats
    {
        public long Frames;
        public ulong FirstNs;
        public ulong LastNs;
        public string? Error;

        /// <summary>Device-clock intervals between frames, ms.</summary>
        public List<double> IntervalsMs { get; } = [];

        public int Print(XrealEyeCameraSource source)
        {
            double span = (LastNs - FirstNs) / 1e9;
            double fps = span > 0 ? (Frames - 1) / span : 0;
            // The glasses change the frame rate themselves (60 fps with Gesture Control on, 15 fps off), so a gap is an
            // interval clearly longer than the typical one, not a fixed threshold.
            double median = IntervalsMs.Count > 0 ? IntervalsMs.Order().ElementAt(IntervalsMs.Count / 2) : 0;
            long gaps = IntervalsMs.Count(ms => ms > median * 1.5);
            Console.WriteLine($"framer: {source.Framer.Stats} readerDrops={source.DroppedFrames}");
            Console.WriteLine($"frames={Frames} deviceSpan={span:F1}s fps={fps:F2} typicalInterval={median:F1}ms gaps(>1.5x)={gaps} maxInterval={(IntervalsMs.Count > 0 ? IntervalsMs.Max() : 0):F1}ms");
            if (Error is not null)
            {
                Console.WriteLine($"stream error: {Error}");
            }

            bool ok = Frames > 0 && gaps == 0 && source.DroppedFrames == 0 && Error is null;
            Console.WriteLine(ok ? $"RESULT: OK, no dropped frames at {fps:F0} fps." : Frames == 0 ? "RESULT: no frames. Is the XREAL Eye attached?" : "RESULT: frames were dropped (see gaps).");
            if (ok && fps < 50)
            {
                Console.WriteLine("        The glasses sent fewer than 60 fps: they send 15 fps while their Gesture Control is off.");
            }

            return ok ? 0 : 3;
        }
    }

    private static async Task<FeedStats> RunAsync(XrealEyeCameraSource source, double seconds, Func<EyeFrame, long, ValueTask> onFrame, CancellationToken ct)
    {
        var stats = new FeedStats();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            await foreach (var frame in source.ReadAsync(cts.Token).ConfigureAwait(false))
            {
                using (frame)
                {
                    if (stats.Frames == 0)
                    {
                        stats.FirstNs = frame.TimestampNs;
                    }
                    else
                    {
                        stats.IntervalsMs.Add((frame.TimestampNs - stats.LastNs) / 1e6);
                    }

                    stats.LastNs = frame.TimestampNs;
                    await onFrame(frame, stats.Frames).ConfigureAwait(false);
                    stats.Frames++;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
        catch (IOException ex)
        {
            stats.Error = ex.Message;
        }

        return stats;
    }
}

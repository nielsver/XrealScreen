using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using XrealScreen.Device.XrealOne;
using XrealScreen.Vision.Hands;

namespace XrealScreen.Cli;

/// <summary>Hand tracking on the XREAL Eye feed (M9 E2, ADR-0016): evaluate recordings, run live with an optional preview.</summary>
internal static class HandsCommands
{
    public static Command Create()
    {
        var hands = new Command("hands", "Hand tracking on the XREAL Eye grayscale feed: eval <file.xrcam> | live | control | targets.");
        hands.Subcommands.Add(CreateEval());
        hands.Subcommands.Add(CreateLive());
        hands.Subcommands.Add(HandControlCommands.CreateControl());
        hands.Subcommands.Add(HandControlCommands.CreateTargets());
        return hands;
    }

    internal static Option<string> ModelsOption() => new("--models")
    {
        Description = "Folder with palm.onnx + landmarks.onnx (default: XREALSCREEN_HAND_MODELS or artifacts\\models\\hands).",
    };

    internal static string? ResolveModels(string? option)
    {
        foreach (var candidate in new[] { option, Environment.GetEnvironmentVariable("XREALSCREEN_HAND_MODELS") })
        {
            if (!string.IsNullOrEmpty(candidate))
            {
                return candidate;
            }
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "artifacts", "models", "hands");
            if (HandTracker.ModelsExist(path))
            {
                return path;
            }
        }

        return null;
    }

    private static Option<int> ThreadsOption() => new("--threads") { Description = "CPU threads per model run.", DefaultValueFactory = _ => 2 };

    private static HandTracker? CreateTracker(string? modelsOption, int threads)
    {
        string? models = ResolveModels(modelsOption);
        if (models is null || !HandTracker.ModelsExist(models))
        {
            Console.WriteLine("Hand models not found. Run .\\tools\\Convert-HandModels.ps1 (or pass --models).");
            return null;
        }

        return new HandTracker(new HandTrackerOptions { ModelDirectory = models, Threads = Math.Max(1, threads) });
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed record Phase(string Name, ulong StartNs, ulong EndNs);

    private static Command CreateEval()
    {
        var file = new Argument<string>("xrcam") { Description = "Recording made with `xrs camera record` (shows a room: keep it private)." };
        var step = new Option<int>("--step") { Description = "Use every Nth frame (recordings keep 30 fps; 2 = 15 fps like the glasses with Gesture Control off).", DefaultValueFactory = _ => 1 };
        var phases = new Option<string>("--phases") { Description = "JSON [{name, startNs, endNs}] to report per phase (device clock)." };
        var models = ModelsOption();
        var threads = ThreadsOption();
        var command = new Command("eval", "Run hand tracking on a recording; report hand-found rate, pinches and time per frame.") { file, step, phases, models, threads };
        command.SetAction(async (parse, ct) =>
        {
            using var tracker = CreateTracker(parse.GetValue(models), parse.GetValue(threads));
            if (tracker is null)
            {
                return 2;
            }

            var phaseList = parse.GetValue(phases) is { } p
                ? JsonSerializer.Deserialize<List<Phase>>(await File.ReadAllTextAsync(p, ct).ConfigureAwait(false), WebJson)!
                : [];
            int keep = Math.Max(1, parse.GetValue(step));
            var results = new List<(ulong Ns, HandResult? Hand, double Ms)>();
            var timings = new List<HandTiming>();
            long index = 0;
            await foreach (var frame in XrcamReader.ReadAsync(parse.GetValue(file)!, ct).ConfigureAwait(false))
            {
                if (index++ % keep != 0)
                {
                    continue;
                }

                long t0 = Stopwatch.GetTimestamp();
                var hand = tracker.Process(frame.Pixels, frame.Width, frame.Height, frame.Width, frame.TimestampNs);
                results.Add((frame.TimestampNs, hand, Stopwatch.GetElapsedTime(t0).TotalMilliseconds));
                timings.Add(tracker.LastTiming);
            }

            if (results.Count == 0)
            {
                Console.WriteLine("No frames.");
                return 3;
            }

            var ms = results.Skip(5).Select(r => r.Ms).Order().ToArray();   // skip model warm-up
            Console.WriteLine($"frames {results.Count} (every {keep}.)  palm detector runs {tracker.PalmRuns}  time per frame median {Percentile(ms, 0.5):F1} ms, p95 {Percentile(ms, 0.95):F1} ms, max {ms[^1]:F1} ms");
            var tracked = timings.Skip(5).Where(t => t.PalmMs == 0).ToList();
            var detected = timings.Skip(5).Where(t => t.PalmMs > 0).ToList();
            Console.WriteLine($"  split: blur {timings.Skip(5).Average(t => t.BlurMs):F1} ms; tracked frames {tracked.Count}: landmarks {(tracked.Count > 0 ? tracked.Average(t => t.LandmarksMs) : 0):F1} ms; palm-detector frames {detected.Count}: palm {(detected.Count > 0 ? detected.Average(t => t.PalmMs) : 0):F1} ms");
            Report("all", results);
            foreach (var phase in phaseList)
            {
                Report(phase.Name, [.. results.Where(r => r.Ns >= phase.StartNs && r.Ns <= phase.EndNs)]);
            }

            return 0;
        });
        return command;
    }

    private static void Report(string name, List<(ulong Ns, HandResult? Hand, double Ms)> frames)
    {
        if (frames.Count == 0)
        {
            return;
        }

        var found = frames.Where(f => f.Hand is not null).Select(f => f.Hand!).ToList();
        var pinch = new PinchDetector();
        int presses = 0;
        foreach (var f in frames)
        {
            if (pinch.Update(f.Hand?.PinchRatio ?? float.NaN, f.Ns / 1e9) == PinchEvent.Pressed)
            {
                presses++;
            }
        }

        string line = $"{name,-10} frames {frames.Count,5}  hand found {100.0 * found.Count / frames.Count,5:F1} %  pinches {presses,3}";
        if (found.Count > 0)
        {
            var ratios = found.Select(h => h.PinchRatio).Order().ToArray();
            line += $"  hand size {Percentile([.. found.Select(h => (double)h.Scale).Order()], 0.5),5:F1} px  pinch ratio min {ratios[0]:F2} median {ratios[ratios.Length / 2]:F2}";
        }

        Console.WriteLine(line);
    }

    private static double Percentile(double[] sorted, double p) => sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))];

    private static Command CreateLive()
    {
        var seconds = new Option<double>("--seconds") { Description = "How long to run (0 = until Ctrl+C).", DefaultValueFactory = _ => 0 };
        var window = new Option<bool>("--window") { Description = "Show the camera image with the hand skeleton in a window." };
        var host = new Option<string>("--host") { Description = "Glasses IP.", DefaultValueFactory = _ => OneEndpoints.Default.Host };
        var models = ModelsOption();
        var threads = ThreadsOption();
        var log = new Option<string>("--log") { Description = "Write one CSV line per frame (time, found, pinch ratio, position; numbers only, no image)." };
        var command = new Command("live", "Track the hand live from the XREAL Eye; prints hand/pinch state every second and pinch events.") { seconds, window, host, models, threads, log };
        command.SetAction(async (parse, ct) =>
        {
            using var tracker = CreateTracker(parse.GetValue(models), parse.GetValue(threads));
            if (tracker is null)
            {
                return 2;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (parse.GetValue(seconds) > 0)
            {
                cts.CancelAfter(TimeSpan.FromSeconds(parse.GetValue(seconds)));
            }

            using var preview = parse.GetValue(window) ? HandPreviewWindow.Start(cts.Cancel) : null;
            var source = new XrealEyeCameraSource(new XrealEyeCameraOptions { Endpoints = OneEndpoints.Default with { Host = parse.GetValue(host)! } });
            var pinch = new PinchDetector();
            await using var csv = parse.GetValue(log) is { } logPath ? new StreamWriter(logPath) : null;
            if (csv is not null)
            {
                await csv.WriteLineAsync("unixSeconds,deviceSeconds,found,pinchRatio,x,y,scale,presence,pressed,event".AsMemory(), ct).ConfigureAwait(false);
            }

            var sw = Stopwatch.StartNew();
            long lastPrint = 0, frames = 0, foundFrames = 0;
            double msSum = 0;
            Console.WriteLine("Tracking. Hold a hand in front of the glasses; pinch thumb and index finger. Ctrl+C stops.");
            try
            {
                await foreach (var frame in source.ReadAsync(cts.Token).ConfigureAwait(false))
                {
                    using (frame)
                    {
                        long t0 = Stopwatch.GetTimestamp();
                        var hand = tracker.Process(frame.Pixels, frame.Width, frame.Height, frame.Stride, frame.TimestampNs);
                        msSum += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                        frames++;
                        foundFrames += hand is null ? 0 : 1;
                        var ev = pinch.Update(hand?.PinchRatio ?? float.NaN, frame.TimestampNs / 1e9);
                        if (csv is not null)
                        {
                            string line = string.Create(CultureInfo.InvariantCulture,
                                $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0:F3},{frame.TimestampNs / 1e9:F3},{(hand is null ? 0 : 1)},{hand?.PinchRatio ?? float.NaN:F3},{hand?.PinchPoint.X ?? float.NaN:F1},{hand?.PinchPoint.Y ?? float.NaN:F1},{hand?.Scale ?? float.NaN:F1},{hand?.Presence ?? 0:F2},{(pinch.IsPressed ? 1 : 0)},{ev}");
                            await csv.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
                        }

                        if (ev != PinchEvent.None)
                        {
                            Console.WriteLine($"  {sw.Elapsed.TotalSeconds,6:F1} s  PINCH {ev.ToString().ToUpperInvariant()}");
                        }

                        preview?.Show(frame, hand, pinch.IsPressed);
                        if (sw.ElapsedMilliseconds - lastPrint >= 1000)
                        {
                            lastPrint = sw.ElapsedMilliseconds;
                            string state = hand is null ? "no hand" : $"hand at {hand.PinchPoint.X,5:F0},{hand.PinchPoint.Y,4:F0}  size {hand.Scale,4:F0} px  pinch ratio {hand.PinchRatio:F2}{(pinch.IsPressed ? "  (pressed)" : "")}";
                            Console.WriteLine($"  {sw.Elapsed.TotalSeconds,6:F1} s  {frames,3} frames  found {100.0 * foundFrames / frames,5:F1} %  {msSum / frames,4:F1} ms/frame  {state}");
                            frames = foundFrames = 0;
                            msSum = 0;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Camera stream ended: {ex.Message}. Is the XREAL Eye attached?");
                return 3;
            }

            return 0;
        });
        return command;
    }
}

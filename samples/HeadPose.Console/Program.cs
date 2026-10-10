// Prints the head pose from XREAL One / One Pro / 1S glasses 10 times per second.
//   dotnet run --project samples/HeadPose.Console              real glasses (USB-C)
//   dotnet run --project samples/HeadPose.Console -- --simulate  no glasses: a simulated head
// Press R + Enter to recenter, Ctrl+C to quit.
// The XrealScreen app must not run a workspace or head look at the same time (one reader per glasses).
using XrealScreen.Device.Simulated;
using XrealScreen.Sdk;

var glasses = args.Contains("--simulate")
    ? new XrealGlasses(new SyntheticImuSource(new SyntheticImuOptions { Motion = SyntheticMotion.YawSweep, DurationSeconds = 3600, RealTime = true }))
    : new XrealGlasses();

glasses.ConnectionChanged += (_, e) =>
    Console.WriteLine(e.IsConnected ? "Connected." : $"Not connected: {e.Error ?? "stopped"} (retrying…)");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};
_ = Task.Run(() =>
{
    while (Console.ReadLine() is { } line)
    {
        if (line.Trim().Equals("r", StringComparison.OrdinalIgnoreCase))
        {
            glasses.Recenter();
        }
    }
});

Console.WriteLine($"Reading {glasses.SourceName}. R + Enter recenters, Ctrl+C quits.");
var next = DateTime.UtcNow;
await foreach (var pose in glasses.ReadPosesAsync(cts.Token))
{
    // ~1000 poses per second arrive; print 10 of them.
    if (DateTime.UtcNow < next)
    {
        continue;
    }

    next = DateTime.UtcNow.AddMilliseconds(100);
    Console.WriteLine($"yaw {pose.Yaw,7:F1}°  pitch {pose.Pitch,7:F1}°  roll {pose.Roll,7:F1}°  turning {pose.AngularVelocity.Z,7:F1}°/s");
}

Console.WriteLine($"{glasses.SampleCount} samples.");

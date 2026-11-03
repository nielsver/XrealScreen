using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;

namespace XrealScreen.Vision.Hands;

/// <summary>Where the time of the last <see cref="HandTracker.Process"/> call went, milliseconds.</summary>
public readonly record struct HandTiming(double BlurMs, double PalmMs, double LandmarksMs)
{
    public double TotalMs => BlurMs + PalmMs + LandmarksMs;
}

public sealed record HandTrackerOptions
{
    /// <summary>Folder with <c>palm.onnx</c> and <c>landmarks.onnx</c> (made by <c>tools/Convert-HandModels.ps1</c>).</summary>
    public required string ModelDirectory { get; init; }

    /// <summary>Palm detector score needed to start tracking (MediaPipe default 0.5).</summary>
    public float MinPalmScore { get; init; } = 0.5f;

    /// <summary>Landmark presence needed to keep tracking; 0.3 measured best on the Eye at 15 fps (2026-10-31).</summary>
    public float MinPresence { get; init; } = 0.3f;

    /// <summary>Smooth the returned landmarks with a One Euro filter (tracking itself uses the raw landmarks).</summary>
    public bool Smooth { get; init; } = true;

    public float SmoothMinCutoffHz { get; init; } = 1.5f;

    public float SmoothBeta { get; init; } = 0.02f;

    /// <summary>CPU threads per model run; 1–2 keeps the rest of the PC responsive.</summary>
    public int Threads { get; init; } = 2;
}

/// <summary>
/// Single-hand tracker on 8-bit grayscale camera frames (ADR-0016): blur → palm detection (only when no hand is
/// tracked) → 21 landmarks in the hand region → region for the next frame. Not thread-safe: one caller at a time.
/// </summary>
public sealed class HandTracker : IDisposable
{
    public const string PalmModelFile = "palm.onnx";
    public const string LandmarkModelFile = "landmarks.onnx";
    private const int LandmarkInputSize = 224;

    private readonly HandTrackerOptions _options;
    private readonly InferenceSession _palm;
    private readonly InferenceSession _landmarks;
    private readonly RunOptions _runOptions = new();
    private readonly float[] _palmInput = new float[PalmDecoder.InputSize * PalmDecoder.InputSize * 3];
    private readonly float[] _landmarkInput = new float[LandmarkInputSize * LandmarkInputSize * 3];
    private readonly OneEuroFilter[] _filters;
    private float[] _blurred = [];
    private RotatedRect? _region;

    public HandTracker(HandTrackerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        using var sessionOptions = new SessionOptions { IntraOpNumThreads = options.Threads, InterOpNumThreads = 1 };
        _palm = new InferenceSession(ModelPath(options.ModelDirectory, PalmModelFile), sessionOptions);
        _landmarks = new InferenceSession(ModelPath(options.ModelDirectory, LandmarkModelFile), sessionOptions);
        _filters = [.. Enumerable.Range(0, HandJoint.Count * 2).Select(_ => new OneEuroFilter(options.SmoothMinCutoffHz, options.SmoothBeta))];
    }

    /// <summary>True when both model files exist in <paramref name="directory"/>.</summary>
    public static bool ModelsExist(string directory) =>
        File.Exists(Path.Combine(directory, PalmModelFile)) && File.Exists(Path.Combine(directory, LandmarkModelFile));

    /// <summary>Time split of the last frame.</summary>
    public HandTiming LastTiming { get; private set; }

    /// <summary>Palm detector runs since creation (each one costs about as much as a landmark run).</summary>
    public int PalmRuns { get; private set; }

    /// <summary>Tracks the hand in one frame; returns null when no hand is found.</summary>
    public HandResult? Process(ReadOnlySpan<byte> pixels, int width, int height, int stride, ulong timestampNs)
    {
        if (_blurred.Length != width * height)
        {
            _blurred = new float[width * height];
        }

        long t0 = Stopwatch.GetTimestamp();
        ImageOps.Blur3x3(pixels, width, height, stride, _blurred);
        double blurMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        double palmMs = 0;
        bool fromDetection = false;
        if (_region is null)
        {
            fromDetection = true;
            long t1 = Stopwatch.GetTimestamp();
            _region = DetectPalm(width, height);
            palmMs = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;
            if (_region is null)
            {
                LastTiming = new HandTiming(blurMs, palmMs, 0);
                ResetFilters();
                return null;
            }
        }

        long t2 = Stopwatch.GetTimestamp();
        var (raw, presence, handedness) = RunLandmarks(width, height, _region.Value);
        LastTiming = new HandTiming(blurMs, palmMs, Stopwatch.GetElapsedTime(t2).TotalMilliseconds);
        if (presence < _options.MinPresence)
        {
            _region = null;
            ResetFilters();
            return null;
        }

        var region = _region.Value;
        _region = HandRoi.FromLandmarks(raw);
        return new HandResult(_options.Smooth ? Smooth(raw, timestampNs) : raw, presence, handedness, region, fromDetection, timestampNs);
    }

    /// <summary>Forgets the tracked hand (next frame runs the palm detector).</summary>
    public void Reset()
    {
        _region = null;
        ResetFilters();
    }

    private RotatedRect? DetectPalm(int width, int height)
    {
        PalmRuns++;
        var letterbox = HandRoi.Letterbox(width, height);
        ImageOps.SampleToTensor(_blurred, width, height, letterbox, PalmDecoder.InputSize, _palmInput);
        using var input = OrtValue.CreateTensorValueFromMemory(_palmInput, [1, PalmDecoder.InputSize, PalmDecoder.InputSize, 3]);
        using var outputs = _palm.Run(_runOptions, [_palm.InputNames[0]], [input], _palm.OutputNames);
        // outputs: regressors [1, 2016, 18], score logits [1, 2016, 1]
        var palm = PalmDecoder.Decode(outputs[0].GetTensorDataAsSpan<float>(), outputs[1].GetTensorDataAsSpan<float>(), _options.MinPalmScore);
        return palm is null ? null : HandRoi.FromPalm(palm, letterbox);
    }

    private (HandPoint[] Landmarks, float Presence, float Handedness) RunLandmarks(int width, int height, RotatedRect region)
    {
        ImageOps.SampleToTensor(_blurred, width, height, region, LandmarkInputSize, _landmarkInput);
        using var input = OrtValue.CreateTensorValueFromMemory(_landmarkInput, [1, LandmarkInputSize, LandmarkInputSize, 3]);
        using var outputs = _landmarks.Run(_runOptions, [_landmarks.InputNames[0]], [input], _landmarks.OutputNames);
        // outputs: landmarks [1, 63] in crop pixels, presence [1, 1] (probability), handedness [1, 1], world landmarks [1, 63]
        var points = outputs[0].GetTensorDataAsSpan<float>();
        float presence = outputs[1].GetTensorDataAsSpan<float>()[0];
        float handedness = outputs[2].GetTensorDataAsSpan<float>()[0];
        var landmarks = new HandPoint[HandJoint.Count];
        for (int j = 0; j < HandJoint.Count; j++)
        {
            var (x, y) = region.ToImage(points[j * 3] / LandmarkInputSize, points[j * 3 + 1] / LandmarkInputSize);
            landmarks[j] = new HandPoint(x, y, points[j * 3 + 2] / LandmarkInputSize * region.Size);
        }

        return (landmarks, presence, handedness);
    }

    private HandPoint[] Smooth(HandPoint[] raw, ulong timestampNs)
    {
        double t = timestampNs / 1e9;
        var smoothed = new HandPoint[raw.Length];
        for (int j = 0; j < raw.Length; j++)
        {
            smoothed[j] = new HandPoint(_filters[2 * j].Filter(raw[j].X, t), _filters[2 * j + 1].Filter(raw[j].Y, t), raw[j].Z);
        }

        return smoothed;
    }

    private void ResetFilters()
    {
        foreach (var f in _filters)
        {
            f.Reset();
        }
    }

    private static string ModelPath(string directory, string file)
    {
        string path = Path.Combine(directory, file);
        return File.Exists(path) ? path : throw new FileNotFoundException($"Hand model missing: {path}. Run tools/Convert-HandModels.ps1.", path);
    }

    public void Dispose()
    {
        _palm.Dispose();
        _landmarks.Dispose();
        _runOptions.Dispose();
    }
}

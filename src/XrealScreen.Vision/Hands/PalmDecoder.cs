// Palm detector anchors and decoding follow MediaPipe's hand detection graph (Apache-2.0, Google LLC):
// SSD anchors for a 192×192 input with strides 8/16/16/16 and fixed-size anchors, 18 values per anchor
// (box centre/size + 7 keypoints, scaled by 192), sigmoid scores, weighted non-maximum suppression (IoU 0.3).
// See THIRD-PARTY-NOTICES.md and docs/legal/SOURCES.md.

namespace XrealScreen.Vision.Hands;

/// <summary>A palm found by the detector, in coordinates of the detector's square input (0..1).</summary>
/// <param name="Keypoints">7 keypoints; 0 = wrist, 2 = middle-finger base.</param>
public sealed record PalmDetection(float Score, float CenterX, float CenterY, float Width, float Height, (float X, float Y)[] Keypoints);

internal static class PalmDecoder
{
    public const int InputSize = 192;
    public const int AnchorCount = 2016;
    public const int ValuesPerAnchor = 18;
    private const int KeypointCount = 7;
    private const float NmsIoU = 0.3f;

    /// <summary>Anchor centres (0..1): a 24×24 grid with 2 anchors per cell, then a 12×12 grid with 6.</summary>
    public static (float X, float Y)[] Anchors { get; } = CreateAnchors();

    private static (float X, float Y)[] CreateAnchors()
    {
        var anchors = new List<(float X, float Y)>(AnchorCount);
        foreach (var (stride, perCell) in new[] { (8, 2), (16, 6) })
        {
            int grid = InputSize / stride;
            for (int y = 0; y < grid; y++)
            {
                for (int x = 0; x < grid; x++)
                {
                    for (int k = 0; k < perCell; k++)
                    {
                        anchors.Add(((x + 0.5f) / grid, (y + 0.5f) / grid));
                    }
                }
            }
        }

        return [.. anchors];
    }

    /// <summary>Returns the strongest palm (merged with overlapping candidates), or null if none scores above <paramref name="minScore"/>.</summary>
    public static PalmDetection? Decode(ReadOnlySpan<float> regressors, ReadOnlySpan<float> scoreLogits, float minScore)
    {
        if (regressors.Length < AnchorCount * ValuesPerAnchor || scoreLogits.Length < AnchorCount)
        {
            throw new ArgumentException("Unexpected palm detector output size.");
        }

        var candidates = new List<PalmDetection>();
        for (int i = 0; i < AnchorCount; i++)
        {
            float score = Sigmoid(Math.Clamp(scoreLogits[i], -100f, 100f));
            if (score < minScore)
            {
                continue;
            }

            var r = regressors.Slice(i * ValuesPerAnchor, ValuesPerAnchor);
            var (ax, ay) = Anchors[i];
            var keypoints = new (float X, float Y)[KeypointCount];
            for (int k = 0; k < KeypointCount; k++)
            {
                keypoints[k] = (r[4 + 2 * k] / InputSize + ax, r[5 + 2 * k] / InputSize + ay);
            }

            candidates.Add(new PalmDetection(score, r[0] / InputSize + ax, r[1] / InputSize + ay, r[2] / InputSize, r[3] / InputSize, keypoints));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates.MaxBy(c => c.Score)!;
        var group = candidates.Where(c => IoU(best, c) > NmsIoU).ToList();
        float total = group.Sum(c => c.Score);
        float W(Func<PalmDetection, float> f) => group.Sum(c => c.Score * f(c)) / total;
        var merged = new (float X, float Y)[KeypointCount];
        for (int k = 0; k < KeypointCount; k++)
        {
            int kk = k;
            merged[k] = (W(c => c.Keypoints[kk].X), W(c => c.Keypoints[kk].Y));
        }

        return new PalmDetection(best.Score, W(c => c.CenterX), W(c => c.CenterY), W(c => c.Width), W(c => c.Height), merged);
    }

    internal static float IoU(PalmDetection a, PalmDetection b)
    {
        float iw = Math.Max(0, Math.Min(a.CenterX + a.Width / 2, b.CenterX + b.Width / 2) - Math.Max(a.CenterX - a.Width / 2, b.CenterX - b.Width / 2));
        float ih = Math.Max(0, Math.Min(a.CenterY + a.Height / 2, b.CenterY + b.Height / 2) - Math.Max(a.CenterY - a.Height / 2, b.CenterY - b.Height / 2));
        float inter = iw * ih;
        float union = a.Width * a.Height + b.Width * b.Height - inter;
        return union > 0 ? inter / union : 0;
    }

    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));
}

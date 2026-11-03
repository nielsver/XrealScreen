using XrealScreen.Vision.Hands;

namespace XrealScreen.Vision.Tests;

public class PalmDecoderTests
{
    [Fact]
    public void Anchors_AreTwoGridsWith2016Centres()
    {
        var anchors = PalmDecoder.Anchors;

        Assert.Equal(2016, anchors.Length);
        Assert.Equal((0.5f / 24, 0.5f / 24), anchors[0]);
        Assert.Equal(anchors[0], anchors[1]);
        Assert.Equal((1.5f / 24, 0.5f / 24), anchors[2]);
        Assert.Equal((0.5f / 12, 0.5f / 12), anchors[1152]);
        Assert.Equal(anchors[1152], anchors[1157]);
        Assert.Equal((11.5f / 12, 11.5f / 12), anchors[^1]);
    }

    [Fact]
    public void Decode_NoScoreAboveThreshold_ReturnsNull()
    {
        var (regressors, scores) = Empty();

        Assert.Null(PalmDecoder.Decode(regressors, scores, 0.5f));
    }

    [Fact]
    public void Decode_OneAnchor_AddsOffsetsScaledBy192()
    {
        var (regressors, scores) = Empty();
        Set(regressors, scores, anchor: 1152, logit: 5, dx: 19.2f, dy: -9.6f, w: 48, h: 96, keypointDx: 1.92f);

        var palm = PalmDecoder.Decode(regressors, scores, 0.5f)!;

        var (ax, ay) = PalmDecoder.Anchors[1152];
        Assert.Equal(ax + 0.1f, palm.CenterX, 5);
        Assert.Equal(ay - 0.05f, palm.CenterY, 5);
        Assert.Equal(0.25f, palm.Width, 5);
        Assert.Equal(0.5f, palm.Height, 5);
        Assert.Equal(ax + 0.01f, palm.Keypoints[0].X, 5);
        Assert.Equal(7, palm.Keypoints.Length);
        Assert.True(palm.Score > 0.99f);
    }

    [Fact]
    public void Decode_OverlappingCandidates_AreMergedByScore()
    {
        var (regressors, scores) = Empty();
        // two anchors of the same cell (same centre): boxes 0.2 and 0.3 wide, equal scores -> 0.25
        Set(regressors, scores, anchor: 1152, logit: 3, dx: 0, dy: 0, w: 0.2f * 192, h: 0.2f * 192, keypointDx: 0);
        Set(regressors, scores, anchor: 1153, logit: 3, dx: 0, dy: 0, w: 0.3f * 192, h: 0.3f * 192, keypointDx: 0);
        // a far-away weaker palm is not merged
        Set(regressors, scores, anchor: 2015, logit: 1, dx: 0, dy: 0, w: 0.2f * 192, h: 0.2f * 192, keypointDx: 0);

        var palm = PalmDecoder.Decode(regressors, scores, 0.5f)!;

        Assert.Equal(0.25f, palm.Width, 4);
        Assert.Equal(PalmDecoder.Anchors[1152].X, palm.CenterX, 5);
    }

    private static (float[] Regressors, float[] Scores) Empty()
    {
        var scores = new float[PalmDecoder.AnchorCount];
        Array.Fill(scores, -10f);
        return (new float[PalmDecoder.AnchorCount * PalmDecoder.ValuesPerAnchor], scores);
    }

    private static void Set(float[] regressors, float[] scores, int anchor, float logit, float dx, float dy, float w, float h, float keypointDx)
    {
        scores[anchor] = logit;
        var r = regressors.AsSpan(anchor * PalmDecoder.ValuesPerAnchor, PalmDecoder.ValuesPerAnchor);
        r[0] = dx;
        r[1] = dy;
        r[2] = w;
        r[3] = h;
        for (int k = 0; k < 7; k++)
        {
            r[4 + 2 * k] = keypointDx;
        }
    }
}

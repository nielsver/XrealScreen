using XrealScreen.Vision.Hands;

namespace XrealScreen.Vision.Tests;

public class HandRoiTests
{
    [Theory]
    [InlineData(0, -1, 0)]              // fingers up in the image: no rotation
    [InlineData(1, 0, MathF.PI / 2)]    // fingers to the right
    [InlineData(-1, 0, -MathF.PI / 2)]  // fingers to the left
    public void AngleUp_RotatesFingerDirectionToUp(float dx, float dy, float expected)
    {
        Assert.Equal(expected, HandRoi.AngleUp((100, 100), (100 + dx, 100 + dy)), 4);
    }

    [Fact]
    public void Transform_ShiftsTowardsTheFingersAndSquares()
    {
        var rect = HandRoi.Transform(100, 100, width: 20, height: 10, rotation: 0, scale: 2, shiftY: -0.5f);

        Assert.Equal(100, rect.CenterX, 4);
        Assert.Equal(95, rect.CenterY, 4);
        Assert.Equal(40, rect.Size, 4);
    }

    [Fact]
    public void Transform_RotatedHand_ShiftsAlongTheHand()
    {
        // fingers to the right (rotation +90°): "up" in the crop is +x in the image
        var rect = HandRoi.Transform(100, 100, width: 10, height: 10, rotation: MathF.PI / 2, scale: 1, shiftY: -0.5f);

        Assert.Equal(105, rect.CenterX, 4);
        Assert.Equal(100, rect.CenterY, 4);
    }

    [Fact]
    public void ToImage_MapsCropCornersThroughTheRotation()
    {
        var rect = new RotatedRect(50, 60, 20, MathF.PI / 2);

        var center = rect.ToImage(0.5f, 0.5f);
        var right = rect.ToImage(1f, 0.5f);

        Assert.Equal((50f, 60f), (MathF.Round(center.X, 4), MathF.Round(center.Y, 4)));
        Assert.Equal(50, right.X, 4);
        Assert.Equal(70, right.Y, 4);
    }

    [Fact]
    public void FromLandmarks_UprightHand_HasNoRotationAndCoversTheHand()
    {
        var hand = UprightHand(cx: 200, wristY: 300, scale: 50);

        var rect = HandRoi.FromLandmarks(hand);

        Assert.Equal(0, rect.Rotation, 3);
        Assert.Equal(200, rect.CenterX, 2);
        Assert.InRange(rect.CenterY, 230, 300);           // shifted from the joint box towards the fingers
        Assert.InRange(rect.Size, 2 * 50, 2 * 50 * 1.6f); // 2× the joint box
    }

    [Fact]
    public void FromPalm_MapsLetterboxCoordinatesToImagePixels()
    {
        var letterbox = HandRoi.Letterbox(512, 378);
        var keypoints = new (float X, float Y)[7];
        keypoints[0] = (0.5f, 0.6f);  // wrist
        keypoints[2] = (0.5f, 0.4f);  // middle-finger base above it
        var palm = new PalmDetection(0.9f, 0.5f, 0.5f, 0.1f, 0.1f, keypoints);

        var rect = HandRoi.FromPalm(palm, letterbox);

        Assert.Equal(0, rect.Rotation, 4);
        Assert.Equal(256, rect.CenterX, 2);
        Assert.Equal(189 - 0.5f * 0.1f * 512, rect.CenterY, 2);
        Assert.Equal(0.1f * 512 * 2.6f, rect.Size, 2);
    }

    internal static HandPoint[] UprightHand(float cx, float wristY, float scale)
    {
        var p = new HandPoint[HandJoint.Count];
        p[0] = new(cx, wristY, 0);
        for (int finger = 0; finger < 5; finger++)
        {
            float x = cx + (finger - 2) * scale * 0.25f;
            for (int j = 1; j <= 4; j++)
            {
                p[finger * 4 + j] = new(x, wristY - scale * (0.6f + 0.2f * j), 0);
            }
        }

        p[HandJoint.MiddleBase] = new(cx, wristY - scale, 0);
        return p;
    }
}

using System.Numerics;
using XrealScreen.Core.Gaming;
using XrealScreen.Core.Tracking;

namespace XrealScreen.Core.Tests;

public class HeadMouseMapperTests
{
    private const long MsNs = 1_000_000;

    private static float Rad(float deg) => deg * MathF.PI / 180f;

    /// <summary>Pose looking <paramref name="yawDeg"/> left and <paramref name="pitchDeg"/> up, turning at <paramref name="rateDegPerSec"/>.</summary>
    private static HeadPose Pose(long timestampNs, float yawDeg, float pitchDeg = 0f, float rateDegPerSec = 50f)
    {
        var world = QuaternionMath.Multiply(QuaternionMath.FromYaw(Rad(yawDeg)), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -Rad(pitchDeg)));
        return new HeadPose(timestampNs, world, world, new Vector3(0, 0, Rad(rateDegPerSec)));
    }

    private static MouseDelta Sweep(HeadMouseMapper mapper, int steps, Func<int, HeadPose> pose)
    {
        int x = 0, y = 0;
        for (int i = 0; i <= steps; i++)
        {
            var d = mapper.Update(pose(i));
            x += d.X;
            y += d.Y;
        }

        return new MouseDelta(x, y);
    }

    [Fact]
    public void Look_TurnLeft10Degrees_MovesMouseLeftByCountsPerDegree()
    {
        var mapper = new HeadMouseMapper { Settings = new() { CountsPerDegree = 20 } };

        var total = Sweep(mapper, 100, i => Pose(i * MsNs, i * 0.1f));

        Assert.InRange(total.X, -201, -199);
        Assert.Equal(0, total.Y);
    }

    [Fact]
    public void Look_SubCountSteps_AreAccumulated()
    {
        // 0.01° per sample × 20 counts/° = 0.2 counts per sample: nothing may be lost.
        var mapper = new HeadMouseMapper { Settings = new() { CountsPerDegree = 20 } };

        var total = Sweep(mapper, 1000, i => Pose(i * MsNs, -i * 0.01f));

        Assert.InRange(total.X, 199, 201);
    }

    [Fact]
    public void Look_BelowDeadband_DoesNotMove()
    {
        var mapper = new HeadMouseMapper { Settings = new() { DeadbandDegreesPerSecond = 0.5f } };

        var total = Sweep(mapper, 1000, i => Pose(i * MsNs, i * 0.0003f, rateDegPerSec: 0.3f));

        Assert.True(total.IsZero);
    }

    [Fact]
    public void Look_CrossingYawWrap_MovesTheShortWay()
    {
        var mapper = new HeadMouseMapper { Settings = new() { CountsPerDegree = 10 } };

        var total = Sweep(mapper, 20, i => Pose(i * MsNs, 175f + i * 0.5f));

        Assert.InRange(total.X, -101, -99);
    }

    [Theory]
    [InlineData(false, -1)]
    [InlineData(true, 1)]
    public void Look_LookUp_MovesMouseUpUnlessInverted(bool invertY, int sign)
    {
        var mapper = new HeadMouseMapper { Settings = new() { CountsPerDegree = 10, InvertY = invertY } };

        var total = Sweep(mapper, 100, i => Pose(i * MsNs, 0f, i * 0.1f));

        Assert.InRange(total.Y * sign, 99, 101);
        Assert.InRange(total.X, -1, 1);
    }

    [Fact]
    public void Stick_InsideDeadZone_DoesNotMove()
    {
        var mapper = new HeadMouseMapper { Settings = new() { Mode = HeadMouseMode.Stick, StickDeadZoneDegrees = 5 } };

        var total = Sweep(mapper, 1000, i => Pose(i * MsNs, i == 0 ? 0f : 4f));

        Assert.True(total.IsZero);
    }

    [Fact]
    public void Stick_FullDeflectionRight_TurnsAtMaxSpeed()
    {
        var mapper = new HeadMouseMapper
        {
            Settings = new() { Mode = HeadMouseMode.Stick, StickDeadZoneDegrees = 5, StickFullDegrees = 25, StickMaxCountsPerSecond = 1000 },
        };

        // Head held 30° to the right for one second.
        var total = Sweep(mapper, 1000, i => Pose(i * MsNs, i == 0 ? 0f : -30f));

        Assert.InRange(total.X, 999, 1001);
    }

    [Fact]
    public void Stick_Reset_RecentersOnNextPose()
    {
        var mapper = new HeadMouseMapper { Settings = new() { Mode = HeadMouseMode.Stick } };
        mapper.Update(Pose(0, 0f));
        mapper.Reset();

        var total = Sweep(mapper, 500, i => Pose((1000 + i) * MsNs, 40f));

        Assert.True(total.IsZero);
    }
}

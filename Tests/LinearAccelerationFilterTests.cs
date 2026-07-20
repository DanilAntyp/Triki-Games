using TrikiPong.TrikiSpace;
using Xunit;

namespace TrikiSpace.Tests;

public class LinearAccelerationFilterTests
{
    private static GameImuSample Rest(double t) => new(t, 0, 0, 1.0, 0, 0, 0, false);

    [Fact]
    public void Stabilizes_OnStationaryDevice()
    {
        var f = new LinearAccelerationFilter();
        for (var i = 0; i < 100; i++)
        {
            f.Process(Rest(i * 0.01));
        }

        Assert.True(f.IsReady);
        Assert.True(System.Math.Abs(f.GravityZ - 1.0) < 0.01, $"gravityZ={f.GravityZ}");
        Assert.True(System.Math.Abs(f.GravityX) < 0.01);
        Assert.True(f.Current.Magnitude < 0.02, $"linear={f.Current.Magnitude}");
    }

    [Fact]
    public void SubtractsGravity_LeavingLinearAcceleration()
    {
        var f = new LinearAccelerationFilter();
        for (var i = 0; i < 100; i++)
        {
            f.Process(Rest(i * 0.01)); // settle gravity on Z
        }

        // A sudden lateral spike should show up as linear acceleration on X, not gravity.
        f.Process(new GameImuSample(1.0, 2.0, 0, 1.0, 0, 0, 0, false));
        Assert.True(f.Current.X > 1.5, $"linearX={f.Current.X}");
        Assert.True(f.GravityX < 0.5, $"gravityX leaked={f.GravityX}");
    }

    [Fact]
    public void SlowChange_IsAbsorbedIntoGravity()
    {
        var f = new LinearAccelerationFilter();
        for (var i = 0; i < 400; i++)
        {
            f.Process(new GameImuSample(i * 0.01, i * 0.002, 0, 1.0, 0, 0, 0, false));
        }

        // A slow ramp is tracked by the gravity estimate, so linear acceleration stays small.
        Assert.True(f.Current.Magnitude < 0.1, $"linear={f.Current.Magnitude}");
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var f = new LinearAccelerationFilter();
        for (var i = 0; i < 50; i++)
        {
            f.Process(Rest(i * 0.01));
        }

        f.Reset();
        Assert.False(f.IsReady);
        Assert.Equal(0, f.GravityZ);
    }
}

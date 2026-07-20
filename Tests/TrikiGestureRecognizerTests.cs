using System;
using TrikiPong.TrikiSpace;
using Xunit;

namespace TrikiSpace.Tests;

public class TrikiGestureRecognizerTests
{
    [Fact]
    public void StationaryDevice_RaisesNoGestures()
    {
        var h = new GestureHarness();
        h.Rest(2.0);
        Assert.Equal(0, h.Shakes);
        Assert.Equal(0, h.Impacts);
    }

    [Fact]
    public void SensorNoise_RaisesNoGestures()
    {
        var h = new GestureHarness();
        var rng = new Random(1234);
        for (var i = 0; i < 300; i++)
        {
            h.FeedRaw((rng.NextDouble() - 0.5) * 0.1, (rng.NextDouble() - 0.5) * 0.1, 1.0 + ((rng.NextDouble() - 0.5) * 0.1));
        }

        Assert.Equal(0, h.Shakes);
        Assert.Equal(0, h.Impacts);
    }

    [Fact]
    public void SlowTilt_IsNeitherShakeNorImpact()
    {
        var h = new GestureHarness();
        h.Rest(0.2);
        for (var i = 0; i < 120; i++)
        {
            h.FeedRaw(i * 0.006, 0, 1.0); // gently rotate gravity onto X over ~1.2 s
        }

        h.Rest(0.3);
        Assert.Equal(0, h.Shakes);
        Assert.Equal(0, h.Impacts);
    }

    [Fact]
    public void SingleSharpImpulse_RaisesExactlyOneImpact_AndNoShake()
    {
        var h = new GestureHarness();
        h.Rest(0.3);
        h.SpikeX(1.8);
        h.Rest(0.35); // beyond the confirmation delay

        Assert.Equal(1, h.Impacts);
        Assert.Equal(0, h.Shakes);
    }

    [Fact]
    public void AlternatingImpulses_RaiseExactlyOneShake_AndNoImpact()
    {
        var h = new GestureHarness();
        h.Rest(0.3);
        h.ShakeBurst();
        h.Rest(0.3);

        Assert.Equal(1, h.Shakes);
        Assert.Equal(0, h.Impacts);
    }

    [Fact]
    public void TinyAmplitudeSpike_DoesNotRaiseImpact()
    {
        var h = new GestureHarness();
        h.Rest(0.3);
        h.SpikeX(0.5); // below the impulse threshold even though its jerk is high
        h.Rest(0.35);

        Assert.Equal(0, h.Impacts);
        Assert.Equal(0, h.Shakes);
    }

    [Fact]
    public void SustainedPush_IsNotClassifiedAsShake()
    {
        var h = new GestureHarness();
        h.Rest(0.3);
        for (var i = 0; i < 14; i++)
        {
            h.FeedRaw(1.5, 0, 1.0); // one long push, not a reversing series
        }

        h.Rest(0.35);
        Assert.Equal(0, h.Shakes);
    }

    [Fact]
    public void Impact_HonorsCooldown()
    {
        var h = new GestureHarness();
        h.Rest(0.3);
        h.SpikeX(1.8);
        h.Rest(0.35); // impact 1
        h.SpikeX(1.8);
        h.Rest(0.35); // still inside the 0.8 s cooldown -> blocked

        Assert.Equal(1, h.Impacts);
    }

    [Fact]
    public void Impact_CanRefireAfterCooldown()
    {
        var h = new GestureHarness();
        h.Rest(0.3);
        h.SpikeX(1.8);
        h.Rest(0.35); // impact 1
        h.Rest(1.0);  // wait out the cooldown
        h.SpikeX(1.8);
        h.Rest(0.35); // impact 2

        Assert.Equal(2, h.Impacts);
    }

    [Fact]
    public void Shake_HonorsCooldown()
    {
        var h = new GestureHarness();
        h.Rest(0.3);
        h.ShakeBurst(); // shake 1
        h.ShakeBurst(); // inside the 1.5 s cooldown -> blocked

        Assert.Equal(1, h.Shakes);
    }

    [Fact]
    public void Shake_CanRefireAfterCooldown()
    {
        var h = new GestureHarness();
        h.Rest(0.3);
        h.ShakeBurst();  // shake 1
        h.Rest(1.7);     // wait out the cooldown
        h.ShakeBurst();  // shake 2

        Assert.Equal(2, h.Shakes);
    }

    [Fact]
    public void ImpactAndShake_AreMutuallyExclusiveForTheSameBurst()
    {
        // The shake burst starts with an impact-strength first impulse; it must still resolve to a
        // single shake and never also fire an impact.
        var h = new GestureHarness();
        h.Rest(0.3);
        h.ShakeBurst(2.0);
        h.Rest(0.4);

        Assert.Equal(1, h.Shakes);
        Assert.Equal(0, h.Impacts);
    }
}

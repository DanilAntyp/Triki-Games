using TrikiPong.TrikiSpace;

namespace TrikiSpace.Tests;

/// <summary>
/// Drives <see cref="TrikiGestureRecognizer"/> with synthetic samples at a fixed 100 Hz. Time is
/// advanced by the harness (never Thread.Sleep) so tests are deterministic. Accel is in g with
/// gravity resting on +Z; "spikes" add a one-sample linear acceleration on top of gravity.
/// </summary>
public sealed class GestureHarness
{
    public const double Dt = 0.01;

    private readonly TrikiGestureRecognizer _recognizer;

    public GestureHarness(GestureSettings? settings = null)
    {
        _recognizer = new TrikiGestureRecognizer(settings ?? new GestureSettings());
        _recognizer.ShakeDetected += (_, _) => Shakes++;
        _recognizer.ImpactDetected += (_, _) => Impacts++;
    }

    public int Shakes { get; private set; }
    public int Impacts { get; private set; }
    public double Time { get; private set; }
    public TrikiGestureRecognizer Recognizer => _recognizer;

    public void FeedRaw(double ax, double ay, double az)
    {
        _recognizer.ProcessSample(new GameImuSample(Time, ax, ay, az, 0, 0, 0, false));
        Time += Dt;
    }

    public void Rest(double seconds)
    {
        var n = (int)System.Math.Round(seconds / Dt);
        for (var i = 0; i < n; i++)
        {
            FeedRaw(0, 0, 1.0);
        }
    }

    /// <summary>One-sample linear spike on X (signed) added onto resting gravity.</summary>
    public void SpikeX(double amplitude) => FeedRaw(amplitude, 0, 1.0);

    /// <summary>Three alternating-direction impulses — a canonical shake.</summary>
    public void ShakeBurst(double amplitude = 1.8)
    {
        SpikeX(amplitude);
        Rest(0.09);
        SpikeX(-amplitude);
        Rest(0.09);
        SpikeX(amplitude);
        Rest(0.09);
    }
}

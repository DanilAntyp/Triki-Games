using System.Collections.Generic;
using TrikiPong.TrikiSpace;
using Xunit;

namespace TrikiSpace.Tests;

public class ImuRecordingTests
{
    private static ImuRecordingModel ThreeSamples()
    {
        var m = new ImuRecordingModel();
        m.Samples.Add(new GameImuSample(0.00, 0.1, -0.2, 1.0, 5, -6, 7, false));
        m.Samples.Add(new GameImuSample(0.01, 0.2, -0.3, 0.9, 8, -9, 10, true));
        m.Samples.Add(new GameImuSample(0.02, 0.3, -0.4, 1.1, 11, -12, 13, false));
        return m;
    }

    [Fact]
    public void Csv_RoundTrip_PreservesSamples()
    {
        var original = ThreeSamples();
        var restored = ImuRecordingModel.FromCsv(original.ToCsv());

        Assert.Equal(original.Samples.Count, restored.Samples.Count);
        for (var i = 0; i < original.Samples.Count; i++)
        {
            var a = original.Samples[i];
            var b = restored.Samples[i];
            Assert.Equal(a.TimestampSeconds, b.TimestampSeconds, 4);
            Assert.Equal(a.AccelX, b.AccelX, 5);
            Assert.Equal(a.GyroZ, b.GyroZ, 5);
            Assert.Equal(a.Button, b.Button);
        }
    }

    [Fact]
    public void Recorder_StoresRelativeTimestamps()
    {
        var recorder = new ImuRecorder();
        recorder.Start(10.0);
        recorder.Record(new GameImuSample(10.0, 0, 0, 1, 0, 0, 0, false));
        recorder.Record(new GameImuSample(10.02, 0, 0, 1, 0, 0, 0, false));
        var model = recorder.Stop();

        Assert.NotNull(model);
        Assert.Equal(2, model!.Samples.Count);
        Assert.Equal(0.0, model.Samples[0].TimestampSeconds, 4);
        Assert.Equal(0.02, model.Samples[1].TimestampSeconds, 4);
    }

    [Fact]
    public void Playback_EmitsAllSamplesInOrder_ThenFinishes()
    {
        var playback = new ImuPlaybackSource(ThreeSamples());
        var emitted = new List<GameImuSample>();
        for (var i = 0; i < 5 && !playback.Finished; i++)
        {
            emitted.AddRange(playback.Advance(0.02));
        }

        Assert.Equal(3, emitted.Count);
        Assert.True(playback.Finished);
        for (var i = 1; i < emitted.Count; i++)
        {
            Assert.True(emitted[i].TimestampSeconds > emitted[i - 1].TimestampSeconds);
        }
    }

    [Fact]
    public void Playback_Loops_WithMonotonicTimestamps()
    {
        var playback = new ImuPlaybackSource(ThreeSamples(), speed: 1.0, loop: true);

        var first = new List<GameImuSample>(playback.Advance(0.05));
        var second = new List<GameImuSample>(playback.Advance(0.05));

        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        Assert.False(playback.Finished); // looping never finishes
        // Every looped timestamp keeps increasing past the first pass.
        Assert.True(second[0].TimestampSeconds > first[^1].TimestampSeconds);
    }
}

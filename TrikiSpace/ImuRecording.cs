using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TrikiPong.TrikiSpace;

/// <summary>An in-memory IMU capture: samples with timestamps relative to the recording start.</summary>
public sealed class ImuRecordingModel
{
    public List<GameImuSample> Samples { get; } = new();

    public double DurationSeconds => Samples.Count == 0 ? 0 : Samples[^1].TimestampSeconds;

    public string ToCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("timestampMs,accelX,accelY,accelZ,gyroX,gyroY,gyroZ,button");
        foreach (var s in Samples)
        {
            sb.Append((s.TimestampSeconds * 1000).ToString("F1", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(s.AccelX.ToString("G9", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(s.AccelY.ToString("G9", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(s.AccelZ.ToString("G9", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(s.GyroX.ToString("G9", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(s.GyroY.ToString("G9", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(s.GyroZ.ToString("G9", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(s.Button ? "true" : "false").Append('\n');
        }

        return sb.ToString();
    }

    public static ImuRecordingModel FromCsv(string csv)
    {
        var model = new ImuRecordingModel();
        var lines = csv.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || (i == 0 && line.StartsWith("timestamp", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var p = line.Split(',');
            if (p.Length < 8)
            {
                continue;
            }

            if (!TryD(p[0], out var ms) || !TryD(p[1], out var ax) || !TryD(p[2], out var ay) ||
                !TryD(p[3], out var az) || !TryD(p[4], out var gx) || !TryD(p[5], out var gy) ||
                !TryD(p[6], out var gz))
            {
                continue;
            }

            var button = p[7].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            model.Samples.Add(new GameImuSample(ms / 1000.0, ax, ay, az, gx, gy, gz, button));
        }

        return model;
    }

    private static bool TryD(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

/// <summary>Captures the live IMU stream into an <see cref="ImuRecordingModel"/>.</summary>
public sealed class ImuRecorder
{
    private double _startTime;
    public ImuRecordingModel? Current { get; private set; }
    public bool IsRecording { get; private set; }
    public int Count => Current?.Samples.Count ?? 0;

    public void Start(double nowSeconds)
    {
        Current = new ImuRecordingModel();
        _startTime = nowSeconds;
        IsRecording = true;
    }

    public void Record(GameImuSample sample)
    {
        if (!IsRecording || Current is null)
        {
            return;
        }

        Current.Samples.Add(sample with { TimestampSeconds = sample.TimestampSeconds - _startTime });
    }

    public ImuRecordingModel? Stop()
    {
        IsRecording = false;
        return Current;
    }
}

/// <summary>
/// Replays a recording as a monotonic sample stream so the gesture pipeline runs identically to
/// live hardware. Driven from the game loop via <see cref="Advance"/> (no threads). Emitted
/// timestamps always increase, even across loops, so detectors see valid deltas.
/// </summary>
public sealed class ImuPlaybackSource
{
    private readonly ImuRecordingModel _recording;
    private double _playhead;
    private int _index;
    private double _loopOffset;

    public ImuPlaybackSource(ImuRecordingModel recording, double speed = 1.0, bool loop = false)
    {
        _recording = recording;
        Speed = speed <= 0 ? 1.0 : speed;
        Loop = loop;
    }

    public double Speed { get; set; }
    public bool Loop { get; set; }
    public bool Finished { get; private set; }

    public IReadOnlyList<GameImuSample> Advance(double dtSeconds)
    {
        var output = new List<GameImuSample>();
        if (Finished || _recording.Samples.Count == 0)
        {
            Finished = true;
            return output;
        }

        _playhead += dtSeconds * Speed;

        while (_index < _recording.Samples.Count)
        {
            var s = _recording.Samples[_index];
            if (s.TimestampSeconds <= _playhead)
            {
                output.Add(s with { TimestampSeconds = s.TimestampSeconds + _loopOffset });
                _index++;
            }
            else
            {
                break;
            }
        }

        if (_index >= _recording.Samples.Count)
        {
            if (Loop)
            {
                var dur = _recording.DurationSeconds + 0.02;
                _loopOffset += dur;
                _playhead -= dur;
                _index = 0;
            }
            else
            {
                Finished = true;
            }
        }

        return output;
    }
}

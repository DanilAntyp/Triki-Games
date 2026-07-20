using System;
using System.Collections.Generic;

namespace TrikiPong.TrikiSpace;

/// <summary>
/// One IMU reading as the game consumes it. Timestamps are monotonic seconds (from the game
/// clock, not wall time) so gesture detectors get stable deltas and tests can drive time directly.
/// Accel is in g, gyro in deg/s (matching TrikiReader's scaling). Button is currently always
/// false on real hardware — the Triki BLE frame carries no button field yet.
/// </summary>
public readonly record struct GameImuSample(
    double TimestampSeconds,
    double AccelX, double AccelY, double AccelZ,
    double GyroX, double GyroY, double GyroZ,
    bool Button);

public readonly record struct LinearAccelerationSample(
    double TimestampSeconds, double X, double Y, double Z, double Magnitude);

/// <summary>
/// Low-pass estimates gravity so we can subtract it and get linear (motion) acceleration, which is
/// what shake/impact detection needs. Steering, by contrast, reads the gravity estimate directly.
/// </summary>
public sealed class LinearAccelerationFilter
{
    private bool _initialized;
    private int _sampleCount;

    public double Alpha { get; set; } = 0.85;

    public double GravityX { get; private set; }
    public double GravityY { get; private set; }
    public double GravityZ { get; private set; }
    public LinearAccelerationSample Current { get; private set; }
    public double SmoothedMagnitude { get; private set; }

    /// <summary>True once the gravity estimate has had time to settle after (re)starting.</summary>
    public bool IsReady => _sampleCount > 15;

    public void Process(GameImuSample s)
    {
        if (!_initialized)
        {
            GravityX = s.AccelX;
            GravityY = s.AccelY;
            GravityZ = s.AccelZ;
            _initialized = true;
        }
        else
        {
            GravityX = (Alpha * GravityX) + ((1 - Alpha) * s.AccelX);
            GravityY = (Alpha * GravityY) + ((1 - Alpha) * s.AccelY);
            GravityZ = (Alpha * GravityZ) + ((1 - Alpha) * s.AccelZ);
        }

        var lx = s.AccelX - GravityX;
        var ly = s.AccelY - GravityY;
        var lz = s.AccelZ - GravityZ;
        var mag = Math.Sqrt((lx * lx) + (ly * ly) + (lz * lz));
        Current = new LinearAccelerationSample(s.TimestampSeconds, lx, ly, lz, mag);
        SmoothedMagnitude += (mag - SmoothedMagnitude) * 0.3;
        _sampleCount++;
    }

    public void Reset()
    {
        _initialized = false;
        _sampleCount = 0;
        GravityX = GravityY = GravityZ = 0;
        SmoothedMagnitude = 0;
        Current = default;
    }
}

/// <summary>Tunable gesture thresholds. Defaults from the spec; will need calibration on real
/// hardware (accel in g). Exposed for a future dev panel / persistence.</summary>
public sealed class GestureSettings
{
    public double GravityFilterAlpha { get; set; } = 0.85;

    public double ShakeImpulseThreshold { get; set; } = 0.65;
    public int ShakeRequiredImpulseCount { get; set; } = 3;
    public double ShakeWindowSeconds { get; set; } = 0.65;
    public int ShakeMinimumDirectionChanges { get; set; } = 2;
    public double ShakeMinimumImpulseIntervalSeconds { get; set; } = 0.07;
    public double ShakeCooldownSeconds { get; set; } = 1.5;

    public double ImpactAccelerationThreshold { get; set; } = 1.15;
    public double ImpactJerkThreshold { get; set; } = 7.0;
    public double ImpactConfirmationDelaySeconds { get; set; } = 0.22;
    public double ImpactCooldownSeconds { get; set; } = 0.8;
}

public sealed class ShakeEventArgs : EventArgs
{
    public double Strength { get; init; }
    public int ImpulseCount { get; init; }
    public int DirectionChanges { get; init; }
    public double MaxAmplitude { get; init; }
}

public sealed class ImpactEventArgs : EventArgs
{
    public double Strength { get; init; }
    public double PeakAcceleration { get; init; }
    public double PeakJerk { get; init; }
}

public enum TrikiGestureType
{
    None,
    ImpactCandidate,
    Impact,
    Shake,
}

/// <summary>
/// Single source of truth for shake vs. impact. The same impulses can never raise both events:
/// a strong sharp first impulse becomes an <em>impact candidate</em>; if more impulses with
/// direction reversals arrive inside the window it is reclassified as a shake, otherwise after a
/// short confirmation delay it fires as an impact.
/// </summary>
public sealed class TrikiGestureRecognizer
{
    private readonly GestureSettings _settings;
    private readonly LinearAccelerationFilter _filter = new();
    private readonly List<Impulse> _impulses = new();

    private double _lastImpulseTime = double.NegativeInfinity;
    private double _shakeCooldownUntil = double.NegativeInfinity;
    private double _impactCooldownUntil = double.NegativeInfinity;
    private bool _above;
    private bool _hasPrev;
    private double _prevMag;
    private double _prevTime;

    private bool _impactCandidate;
    private double _candidateTime;
    private double _candidateAmp;
    private double _candidateJerk;

    public TrikiGestureRecognizer(GestureSettings settings)
    {
        _settings = settings;
        _filter.Alpha = settings.GravityFilterAlpha;
    }

    public event EventHandler<ShakeEventArgs>? ShakeDetected;
    public event EventHandler<ImpactEventArgs>? ImpactDetected;

    // Live diagnostics for the dev panel.
    public TrikiGestureType CurrentGesture { get; private set; } = TrikiGestureType.None;
    public int WindowImpulseCount => _impulses.Count;
    public int WindowDirectionChanges { get; private set; }
    public LinearAccelerationFilter Filter => _filter;
    public double LastJerk { get; private set; }
    public double ShakeCooldownUntil => _shakeCooldownUntil;
    public double ImpactCooldownUntil => _impactCooldownUntil;

    public void ProcessSample(GameImuSample sample)
    {
        _filter.Alpha = _settings.GravityFilterAlpha;
        _filter.Process(sample);
        var lin = _filter.Current;
        var t = sample.TimestampSeconds;
        var mag = lin.Magnitude;

        var dt = _hasPrev ? t - _prevTime : 0;
        var jerk = dt > 1e-4 ? (mag - _prevMag) / dt : 0;
        LastJerk = jerk;

        // Rising-edge impulse detection with hysteresis + minimum spacing.
        if (mag >= _settings.ShakeImpulseThreshold && !_above)
        {
            _above = true;
            if (t - _lastImpulseTime >= _settings.ShakeMinimumImpulseIntervalSeconds)
            {
                RegisterImpulse(t, lin, mag, jerk);
                _lastImpulseTime = t;
            }
        }
        else if (mag < _settings.ShakeImpulseThreshold * 0.7)
        {
            _above = false;
        }

        PruneWindow(t);
        WindowDirectionChanges = CountDirectionChanges();

        // Shake wins as soon as enough reversing impulses accumulate.
        if (_impulses.Count >= _settings.ShakeRequiredImpulseCount &&
            WindowDirectionChanges >= _settings.ShakeMinimumDirectionChanges &&
            t >= _shakeCooldownUntil)
        {
            var maxAmp = 0.0;
            foreach (var i in _impulses)
            {
                maxAmp = Math.Max(maxAmp, i.Amplitude);
            }

            var strength = Math.Clamp((_impulses.Count / (double)(_settings.ShakeRequiredImpulseCount * 2)) +
                                      (maxAmp / (_settings.ShakeImpulseThreshold * 4)), 0, 1);
            CurrentGesture = TrikiGestureType.Shake;
            ShakeDetected?.Invoke(this, new ShakeEventArgs
            {
                Strength = strength,
                ImpulseCount = _impulses.Count,
                DirectionChanges = WindowDirectionChanges,
                MaxAmplitude = maxAmp,
            });

            _shakeCooldownUntil = t + _settings.ShakeCooldownSeconds;
            _impactCooldownUntil = t + _settings.ImpactCooldownSeconds; // don't also fire impact
            _impactCandidate = false;
            _impulses.Clear();
            Finish(t);
            return;
        }

        // Impact confirmation: candidate that did NOT turn into a shake.
        if (_impactCandidate && t - _candidateTime >= _settings.ImpactConfirmationDelaySeconds)
        {
            var becameShake = _impulses.Count >= _settings.ShakeRequiredImpulseCount &&
                              WindowDirectionChanges >= _settings.ShakeMinimumDirectionChanges;
            if (!becameShake && t >= _impactCooldownUntil)
            {
                var strength = Math.Clamp(((_candidateAmp - _settings.ImpactAccelerationThreshold) /
                                           Math.Max(0.001, _settings.ImpactAccelerationThreshold)) * 0.6 +
                                          (_candidateJerk / (_settings.ImpactJerkThreshold * 3)) * 0.4, 0, 1);
                CurrentGesture = TrikiGestureType.Impact;
                ImpactDetected?.Invoke(this, new ImpactEventArgs
                {
                    Strength = strength,
                    PeakAcceleration = _candidateAmp,
                    PeakJerk = _candidateJerk,
                });
                _impactCooldownUntil = t + _settings.ImpactCooldownSeconds;
                _impulses.Clear();
            }

            _impactCandidate = false;
            Finish(t);
        }

        _prevMag = mag;
        _prevTime = t;
        _hasPrev = true;

        if (!_impactCandidate && CurrentGesture is TrikiGestureType.Impact or TrikiGestureType.Shake && _impulses.Count == 0)
        {
            CurrentGesture = TrikiGestureType.None;
        }
    }

    public void Reset()
    {
        _filter.Reset();
        _impulses.Clear();
        _lastImpulseTime = double.NegativeInfinity;
        _shakeCooldownUntil = double.NegativeInfinity;
        _impactCooldownUntil = double.NegativeInfinity;
        _above = false;
        _hasPrev = false;
        _impactCandidate = false;
        CurrentGesture = TrikiGestureType.None;
        WindowDirectionChanges = 0;
    }

    private void RegisterImpulse(double t, LinearAccelerationSample lin, double amp, double jerk)
    {
        var inv = amp > 1e-6 ? 1.0 / amp : 0.0;
        _impulses.Add(new Impulse(t, lin.X * inv, lin.Y * inv, lin.Z * inv, amp));

        if (!_impactCandidate &&
            amp >= _settings.ImpactAccelerationThreshold &&
            jerk >= _settings.ImpactJerkThreshold &&
            t >= _impactCooldownUntil)
        {
            _impactCandidate = true;
            _candidateTime = t;
            _candidateAmp = amp;
            _candidateJerk = jerk;
            CurrentGesture = TrikiGestureType.ImpactCandidate;
        }
    }

    private void PruneWindow(double t)
    {
        var cutoff = t - _settings.ShakeWindowSeconds;
        _impulses.RemoveAll(i => i.Time < cutoff);
    }

    private int CountDirectionChanges()
    {
        var changes = 0;
        for (var i = 1; i < _impulses.Count; i++)
        {
            var a = _impulses[i - 1];
            var b = _impulses[i];
            var dot = (a.DirX * b.DirX) + (a.DirY * b.DirY) + (a.DirZ * b.DirZ);
            if (dot < -0.15)
            {
                changes++;
            }
        }

        return changes;
    }

    private void Finish(double t)
    {
        _prevMag = 0;
        _prevTime = t;
        _hasPrev = true;
    }

    private readonly record struct Impulse(double Time, double DirX, double DirY, double DirZ, double Amplitude);
}

/// <summary>Turns IMU into ship steering (from tilt, relative to a calibrated neutral) and an aim
/// angle (from integrating gyro-Z, which drifts slowly — hence the reset).</summary>
public sealed class OrientationController
{
    private double _smoothX;
    private double _smoothY;
    private bool _calibrating;
    private double _calibEndTime;
    private double _accX, _accY, _accZ;
    private int _calibCount;

    public double NeutralGravityX { get; private set; }
    public double NeutralGravityY { get; private set; }
    public bool IsCalibrated { get; private set; }
    public bool IsCalibrating => _calibrating;

    public double AimAngleRadians { get; private set; } = -Math.PI / 2; // start aiming up
    public double SteerX { get; private set; }
    public double SteerY { get; private set; }

    // Tunables (a subset of GameSettings, kept simple for v1).
    public double DeadZone { get; set; } = 0.08;
    public double TiltSensitivity { get; set; } = 2.2;
    public double TiltSmoothing { get; set; } = 0.2;
    public bool InvertHorizontal { get; set; }
    public bool InvertVertical { get; set; }
    public double GyroDeadZoneDegPerSec { get; set; } = 1.0;
    public double GyroSensitivity { get; set; } = 1.0;

    public void BeginCalibration(double nowSeconds, double durationSeconds = 0.75)
    {
        _calibrating = true;
        _calibEndTime = nowSeconds + durationSeconds;
        _accX = _accY = _accZ = 0;
        _calibCount = 0;
    }

    public void ResetAim() => AimAngleRadians = -Math.PI / 2;

    public void Process(GameImuSample sample, LinearAccelerationFilter filter, double dt)
    {
        var t = sample.TimestampSeconds;

        if (_calibrating)
        {
            _accX += filter.GravityX;
            _accY += filter.GravityY;
            _accZ += filter.GravityZ;
            _calibCount++;
            if (t >= _calibEndTime && _calibCount > 0)
            {
                NeutralGravityX = _accX / _calibCount;
                NeutralGravityY = _accY / _calibCount;
                IsCalibrated = true;
                _calibrating = false;
            }
        }

        // Steering from tilt relative to neutral gravity direction.
        var rawX = filter.GravityX - NeutralGravityX;
        var rawY = filter.GravityY - NeutralGravityY;
        rawX = ApplyDeadZone(rawX, DeadZone) * TiltSensitivity;
        rawY = ApplyDeadZone(rawY, DeadZone) * TiltSensitivity;
        if (InvertHorizontal) rawX = -rawX;
        if (InvertVertical) rawY = -rawY;

        _smoothX += (Math.Clamp(rawX, -1, 1) - _smoothX) * TiltSmoothing;
        _smoothY += (Math.Clamp(rawY, -1, 1) - _smoothY) * TiltSmoothing;
        SteerX = _smoothX;
        SteerY = _smoothY;

        // Aim from gyro-Z integration.
        var gz = sample.GyroZ;
        if (Math.Abs(gz) < GyroDeadZoneDegPerSec)
        {
            gz = 0;
        }

        AimAngleRadians += gz * (Math.PI / 180.0) * GyroSensitivity * dt;
        AimAngleRadians = NormalizeAngle(AimAngleRadians);
    }

    public void NudgeAim(double radians)
    {
        AimAngleRadians = NormalizeAngle(AimAngleRadians + radians);
    }

    private static double ApplyDeadZone(double value, double deadZone)
    {
        if (Math.Abs(value) <= deadZone)
        {
            return 0;
        }

        var sign = Math.Sign(value);
        var normalized = (Math.Abs(value) - deadZone) / Math.Max(0.0001, 1.0 - deadZone);
        return sign * Math.Clamp(normalized, 0.0, 1.0);
    }

    private static double NormalizeAngle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }
}

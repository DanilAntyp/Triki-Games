using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using TrikiPong.TrikiSpace;
using TrikiReader;

namespace TrikiPong;

public partial class TrikiSpaceWindow : Window, IDeveloperHost
{
    private const int MaxShotVisuals = 140;
    private const int MaxSuperVisuals = 4;
    private const int MaxMoverVisuals = 40;
    private const int StarCount = 70;
    private const double SampleTimeoutSeconds = 0.4;

    // Roll-rate steering, matching the Pong paddle: gyro-Z rate -> horizontal velocity.
    private const double PixelsPerDegreePerSecond = 6.0;
    private const double KeyboardRollEquivalentDegPerSec = 150.0;

    private readonly SpaceGameEngine _engine = new();
    private readonly GestureSettings _gestureSettings = new();
    private readonly TrikiGestureRecognizer _recognizer;
    private readonly OrientationController _orientation = new();

    private readonly ControllerInput _controller;
    private readonly CancellationTokenSource _controllerCts;
    private readonly System.Action _onBackToMenu;

    private readonly Stopwatch _clock = new();
    private readonly ConcurrentQueue<GameImuSample> _sampleQueue = new();
    private readonly HashSet<Key> _pressed = new();

    private readonly List<(Ellipse Visual, double Speed)> _stars = new();
    private readonly List<Ellipse> _shotVisuals = new();
    private readonly List<Ellipse> _superVisuals = new();
    private readonly List<Polygon> _moverVisuals = new();
    private Polygon _ship = null!;
    private Polygon _boostTrail = null!;
    private Line _aimLine = null!;
    private Ellipse _reticle = null!;

    private double _lastElapsed;
    private double _lastSampleTime = -1;
    private bool _returningToMenu;
    private bool _started;
    private volatile string _rawMonitorText = "Waiting for packets...";
    private byte[]? _prevRaw;
    private double _lastSampleWallTime;

    private readonly GameSettings _settings = SettingsService.Load();
    private readonly ImuRecorder _recorder = new();
    private ImuRecordingModel? _lastRecording;
    private ImuPlaybackSource? _playback;
    private DeveloperPanel? _devPanel;
    private GameImuSample _lastSample;
    private double _packetHz;
    private volatile bool _playing;
    private string _manualToast = string.Empty;
    private double _manualToastUntil;

    public TrikiSpaceWindow(ControllerInput controller, CancellationTokenSource controllerCts, System.Action onBackToMenu)
    {
        InitializeComponent();

        _controller = controller;
        _controllerCts = controllerCts;
        _onBackToMenu = onBackToMenu;
        _recognizer = new TrikiGestureRecognizer(_gestureSettings);
        _settings.ApplyTo(_gestureSettings, _orientation);

        _clock.Start();

        _recognizer.ShakeDetected += OnShake;
        _recognizer.ImpactDetected += OnImpact;
        _controller.ImuSampleReceived += OnImuSample;
        _controller.RawNotificationReceived += OnRawNotification;
        _controller.StatusChanged += OnControllerStatus;
    }

    // ---- Input intake (BLE thread) ----

    private void OnImuSample(object? sender, ImuSample e)
    {
        var t = _clock.Elapsed.TotalSeconds;
        var dt = t - _lastSampleWallTime;
        _lastSampleWallTime = t;
        if (dt > 1e-4)
        {
            _packetHz = (_packetHz * 0.9) + ((1.0 / dt) * 0.1);
        }

        var sample = new GameImuSample(t, e.AccelX, e.AccelY, e.AccelZ, e.GyroX, e.GyroY, e.GyroZ, false);
        _recorder.Record(sample);
        if (!_playing)
        {
            _sampleQueue.Enqueue(sample);
        }
    }

    private void OnRawNotification(object? sender, byte[] bytes)
    {
        var sb = new StringBuilder();
        sb.Append("len ").Append(bytes.Length).Append("  ");
        for (var i = 0; i < bytes.Length && i < 20; i++)
        {
            sb.Append(bytes[i].ToString("X2"));
            sb.Append(' ');
        }

        if (_prevRaw is { } prev && prev.Length == bytes.Length)
        {
            sb.Append("\nchanged idx: ");
            var any = false;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != prev[i])
                {
                    sb.Append(i).Append(' ');
                    any = true;
                }
            }

            if (!any)
            {
                sb.Append("(none)");
            }
        }

        _prevRaw = (byte[])bytes.Clone();
        _rawMonitorText = sb.ToString();
    }

    private void OnControllerStatus(string message) => StatusText.Text = message;

    // ---- Gesture results (UI thread; recognizer runs in the game loop) ----

    private void OnShake(object? sender, ShakeEventArgs e) => _engine.ActivateBoost(e.Strength);

    private void OnImpact(object? sender, ImpactEventArgs e) => _engine.TryFireSuper(e.Strength);

    // ---- Lifecycle ----

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        BuildVisuals();
        _lastElapsed = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnRendering;
        GuideOverlay.Visibility = Visibility.Visible; // the game waits on the how-to-play screen
        Focus();
    }

    private void StartGame()
    {
        if (_started)
        {
            return;
        }

        GuideOverlay.Visibility = Visibility.Collapsed;
        _engine.Reset();
        _recognizer.Reset();
        _started = true;
        Focus();
    }

    private void OnStartClick(object sender, RoutedEventArgs e) => StartGame();

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _devPanel?.Close();
        _devPanel = null;
        _recognizer.ShakeDetected -= OnShake;
        _recognizer.ImpactDetected -= OnImpact;
        _controller.ImuSampleReceived -= OnImuSample;
        _controller.RawNotificationReceived -= OnRawNotification;
        _controller.StatusChanged -= OnControllerStatus;

        if (!_returningToMenu)
        {
            _controllerCts.Cancel();
        }
    }

    private void BuildVisuals()
    {
        for (var i = 0; i < StarCount; i++)
        {
            var size = 1 + (_rngNext() * 2.2);
            var star = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(120 + (_rngNext() * 135)), 255, 255, 255)),
            };
            Canvas.SetLeft(star, _rngNext() * SpaceGameEngine.FieldWidth);
            Canvas.SetTop(star, _rngNext() * SpaceGameEngine.FieldHeight);
            Field.Children.Add(star);
            _stars.Add((star, 20 + (size * 22)));
        }

        _boostTrail = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0xAA, 0x39, 0xD8, 0xFF)), Visibility = Visibility.Collapsed };
        Field.Children.Add(_boostTrail);

        _aimLine = new Line { Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xC8, 0x5C)), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 3 } };
        Field.Children.Add(_aimLine);

        _reticle = new Ellipse { Width = 10, Height = 10, Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x5C)), StrokeThickness = 2 };
        Field.Children.Add(_reticle);

        _ship = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromRgb(0x5C, 0xC8, 0xFF)),
            Points = new PointCollection { new Point(0, -16), new Point(12, 12), new Point(0, 6), new Point(-12, 12) },
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x5C, 0xC8, 0xFF), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9 },
        };
        Field.Children.Add(_ship);

        for (var i = 0; i < MaxMoverVisuals; i++)
        {
            var m = new Polygon
            {
                Visibility = Visibility.Collapsed,
                StrokeThickness = 1.6,
                StrokeLineJoin = PenLineJoin.Round,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 12, Opacity = 0.75 },
            };
            _moverVisuals.Add(m);
            Field.Children.Add(m);
        }

        for (var i = 0; i < MaxShotVisuals; i++)
        {
            var s = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(0xEA, 0xF6, 0xFF)), Visibility = Visibility.Collapsed };
            _shotVisuals.Add(s);
            Field.Children.Add(s);
        }

        for (var i = 0; i < MaxSuperVisuals; i++)
        {
            var s = new Ellipse
            {
                Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0xD0)),
                Visibility = Visibility.Collapsed,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0xFF, 0x4D, 0x8D), BlurRadius = 28, ShadowDepth = 0, Opacity = 0.9 },
            };
            _superVisuals.Add(s);
            Field.Children.Add(s);
        }
    }

    // ---- Main loop ----

    private void OnRendering(object? sender, System.EventArgs e)
    {
        var elapsed = _clock.Elapsed.TotalSeconds;
        var dt = elapsed - _lastElapsed;
        _lastElapsed = elapsed;
        if (dt <= 0 || dt > 0.1)
        {
            dt = 1.0 / 60.0;
        }

        AdvancePlayback(dt);
        DrainSamples();

        if (_started)
        {
            var rollRate = _controller.RollRateDegPerSec + (KeyboardMoveDirection() * KeyboardRollEquivalentDegPerSec);
            var moveVelocityX = rollRate * PixelsPerDegreePerSecond;
            _engine.Update(dt, moveVelocityX, fire: true);
        }

        Render(dt);
        _devPanel?.UpdateDiagnostics(BuildDiagnostics());
    }

    private void AdvancePlayback(double dt)
    {
        if (_playback is null)
        {
            return;
        }

        foreach (var s in _playback.Advance(dt))
        {
            _sampleQueue.Enqueue(s);
        }

        if (_playback.Finished)
        {
            _playback = null;
            _playing = false;
        }
    }

    private void DrainSamples()
    {
        var processed = 0;
        while (processed < 40 && _sampleQueue.TryDequeue(out var s))
        {
            processed++;
            var sampleDt = _lastSampleTime < 0 ? 1.0 / 60.0 : System.Math.Clamp(s.TimestampSeconds - _lastSampleTime, 0, 0.1);
            _lastSampleTime = s.TimestampSeconds;
            _lastSample = s;
            _recognizer.ProcessSample(s);
            _orientation.Process(s, _recognizer.Filter, sampleDt);
        }
    }

    private int KeyboardMoveDirection()
    {
        var dir = 0;
        if (_pressed.Contains(Key.A) || _pressed.Contains(Key.Left)) dir -= 1;
        if (_pressed.Contains(Key.D) || _pressed.Contains(Key.Right)) dir += 1;
        return dir;
    }

    // ---- Rendering ----

    private void Render(double dt)
    {
        // Stars scroll downward with a subtle parallax.
        foreach (var (visual, speed) in _stars)
        {
            var y = Canvas.GetTop(visual) + (speed * dt);
            if (y > SpaceGameEngine.FieldHeight)
            {
                y -= SpaceGameEngine.FieldHeight;
                Canvas.SetLeft(visual, _rngNext() * SpaceGameEngine.FieldWidth);
            }

            Canvas.SetTop(visual, y);
        }

        // Ship + aim
        var px = _engine.PlayerX;
        var py = _engine.PlayerY;
        _ship.RenderTransform = new TranslateTransform(px, py);
        _ship.Opacity = _engine.IsInvulnerable ? 0.5 : 1.0;

        var ax = System.Math.Cos(_engine.AimAngle);
        var ay = System.Math.Sin(_engine.AimAngle);
        _aimLine.X1 = px;
        _aimLine.Y1 = py;
        _aimLine.X2 = px + (ax * 70);
        _aimLine.Y2 = py + (ay * 70);
        Canvas.SetLeft(_reticle, px + (ax * 76) - 5);
        Canvas.SetTop(_reticle, py + (ay * 76) - 5);

        if (_engine.BoostActive)
        {
            _boostTrail.Visibility = Visibility.Visible;
            _boostTrail.Points = new PointCollection
            {
                new Point(px - 8, py + 8),
                new Point(px + 8, py + 8),
                new Point(px, py + 34 + (_rngNext() * 10)),
            };
        }
        else
        {
            _boostTrail.Visibility = Visibility.Collapsed;
        }

        RenderMovers();
        RenderShots();
        RenderHud();
        RenderOverlays();
    }

    private void RenderMovers()
    {
        var movers = _engine.Movers;
        for (var i = 0; i < _moverVisuals.Count; i++)
        {
            var v = _moverVisuals[i];
            if (i >= movers.Count)
            {
                v.Visibility = Visibility.Collapsed;
                continue;
            }

            var m = movers[i];
            v.Visibility = Visibility.Visible;
            v.Points = BuildMoverPoints(m.Kind, m.Radius);

            Color glow;
            switch (m.Kind)
            {
                case EntityKind.Asteroid:
                    v.Fill = AsteroidFill;
                    v.Stroke = AsteroidStroke;
                    glow = Color.FromRgb(0x6A, 0x72, 0x88);
                    break;
                case EntityKind.EnemyBasic:
                    v.Fill = BasicFill;
                    v.Stroke = BasicStroke;
                    glow = Color.FromRgb(0xFF, 0x4D, 0x4D);
                    break;
                default:
                    v.Fill = ToughFill;
                    v.Stroke = ToughStroke;
                    glow = Color.FromRgb(0xC9, 0x5C, 0xFF);
                    break;
            }

            if (v.Effect is System.Windows.Media.Effects.DropShadowEffect fx)
            {
                fx.Color = glow;
            }

            // Points are centred on the origin, so rotate (asteroids tumble) then translate into place.
            var tg = new TransformGroup();
            if (m.Kind == EntityKind.Asteroid)
            {
                tg.Children.Add(new RotateTransform(m.Angle));
            }

            tg.Children.Add(new TranslateTransform(m.X, m.Y));
            v.RenderTransform = tg;
        }
    }

    // Silhouettes centred on (0,0), scaled to the mover radius. Enemies face downward (they descend).
    private static PointCollection BuildMoverPoints(EntityKind kind, double r)
    {
        double[,] unit = kind switch
        {
            EntityKind.EnemyBasic => new[,]
            {
                { 0.00, 1.00 }, { 0.55, 0.15 }, { 1.00, 0.35 }, { 0.50, -0.20 },
                { 0.60, -0.75 }, { 0.20, -0.45 }, { 0.00, -0.65 }, { -0.20, -0.45 },
                { -0.60, -0.75 }, { -0.50, -0.20 }, { -1.00, 0.35 }, { -0.55, 0.15 },
            },
            EntityKind.EnemyTough => new[,]
            {
                { 0.00, 1.00 }, { 0.70, 0.55 }, { 0.98, -0.10 }, { 0.55, -0.55 },
                { 0.28, -0.35 }, { 0.00, -0.95 }, { -0.28, -0.35 }, { -0.55, -0.55 },
                { -0.98, -0.10 }, { -0.70, 0.55 },
            },
            _ => new[,] // jagged asteroid
            {
                { 0.00, -1.00 }, { 0.55, -0.78 }, { 0.95, -0.30 }, { 0.72, 0.20 },
                { 1.00, 0.62 }, { 0.42, 0.86 }, { -0.10, 1.00 }, { -0.62, 0.74 },
                { -0.98, 0.28 }, { -0.74, -0.28 }, { -0.95, -0.66 }, { -0.40, -0.82 },
            },
        };

        var pts = new PointCollection(unit.GetLength(0));
        for (var i = 0; i < unit.GetLength(0); i++)
        {
            pts.Add(new Point(unit[i, 0] * r, unit[i, 1] * r));
        }

        return pts;
    }

    private void RenderShots()
    {
        int normal = 0, super = 0;
        foreach (var s in _engine.Shots)
        {
            if (s.Piercing)
            {
                if (super >= _superVisuals.Count)
                {
                    continue;
                }

                var v = _superVisuals[super++];
                v.Visibility = Visibility.Visible;
                v.Width = s.Radius * 2;
                v.Height = s.Radius * 2;
                Canvas.SetLeft(v, s.X - s.Radius);
                Canvas.SetTop(v, s.Y - s.Radius);
            }
            else
            {
                if (normal >= _shotVisuals.Count)
                {
                    continue;
                }

                var v = _shotVisuals[normal++];
                v.Visibility = Visibility.Visible;
                v.Fill = s.Hostile ? EnemyShotFill : PlayerShotFill;
                var r = s.Radius;
                v.Width = r * 2;
                v.Height = r * 2;
                Canvas.SetLeft(v, s.X - r);
                Canvas.SetTop(v, s.Y - r);
            }
        }

        for (var i = normal; i < _shotVisuals.Count; i++)
        {
            _shotVisuals[i].Visibility = Visibility.Collapsed;
        }

        for (var i = super; i < _superVisuals.Count; i++)
        {
            _superVisuals[i].Visibility = Visibility.Collapsed;
        }
    }

    private void RenderHud()
    {
        ScoreText.Text = _engine.Score.ToString();
        WaveText.Text = _engine.Wave.ToString();
        HealthBar.Width = 200 * System.Math.Clamp(_engine.Health / _engine.MaxHealth, 0, 1);
        BoostBar.Width = 200 * System.Math.Clamp(_engine.BoostEnergy / 100.0, 0, 1);
        SuperBar.Width = 200 * System.Math.Clamp(_engine.SuperCharge / _engine.SuperChargeMax, 0, 1);

        ConnStatusText.Text = _controller.IsConnected ? "Triki connected" : "Keyboard mode";
        ConnStatusText.Foreground = _controller.IsConnected ? ConnectedBrush : MutedBrushRef;

        DamageOverlay.BorderThickness = new Thickness(_engine.DamageFlash * 10);
    }

    private void RenderOverlays()
    {
        string? toast = null;
        if (_clock.Elapsed.TotalSeconds < _manualToastUntil)
        {
            toast = _manualToast;
        }
        else if (_orientation.IsCalibrating)
        {
            toast = "Calibrating — hold Triki still…";
        }
        else if (_engine.SuperFlash > 0.05)
        {
            toast = "SUPER!";
        }
        else if (_engine.NoChargeFlash > 0.05)
        {
            toast = "Not enough charge";
        }
        else if (_engine.BoostActive)
        {
            toast = "BOOST";
        }

        if (toast is null)
        {
            ToastText.Visibility = Visibility.Collapsed;
        }
        else
        {
            ToastText.Text = toast;
            ToastText.Visibility = Visibility.Visible;
        }

        PauseOverlay.Visibility = _engine.Paused && !_engine.GameOver ? Visibility.Visible : Visibility.Collapsed;

        if (_engine.GameOver)
        {
            if (GameOverOverlay.Visibility != Visibility.Visible)
            {
                FinalScoreText.Text = $"Score {_engine.Score}  ·  reached wave {_engine.Wave}";
                GameOverOverlay.Visibility = Visibility.Visible;
            }
        }
        else
        {
            GameOverOverlay.Visibility = Visibility.Collapsed;
        }

        if (RawMonitor.Visibility == Visibility.Visible)
        {
            RawMonitorText.Text = _rawMonitorText;
        }
    }

    // ---- Keyboard ----

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_started)
        {
            StartGame(); // any key dismisses the how-to-play screen and begins
            return;
        }

        _pressed.Add(e.Key);
        if (e.IsRepeat)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.LeftShift:
            case Key.RightShift:
                _engine.ActivateBoost(0.8);
                break;
            case Key.Enter:
            case Key.F:
                _engine.TryFireSuper(1.0);
                break;
            case Key.C:
                _orientation.BeginCalibration(_clock.Elapsed.TotalSeconds);
                break;
            case Key.Escape:
                if (!_engine.GameOver)
                {
                    _engine.Paused = !_engine.Paused;
                }

                break;
            case Key.R:
                if (_engine.GameOver)
                {
                    RestartGame();
                }

                break;
            case Key.F2:
                RawMonitor.Visibility = RawMonitor.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                break;
            case Key.F1:
                ToggleDevPanel();
                break;
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e) => _pressed.Remove(e.Key);

    // ---- Buttons ----

    private void OnRestartClick(object sender, RoutedEventArgs e) => RestartGame();

    private void RestartGame()
    {
        _engine.Reset();
        _recognizer.Reset();
        GameOverOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnBackToMenuClick(object sender, RoutedEventArgs e)
    {
        _returningToMenu = true;
        _onBackToMenu();
        Close();
    }

    // ---- Dev panel + diagnostics ----

    private void ToggleDevPanel()
    {
        if (_devPanel is not null)
        {
            _devPanel.Close();
            _devPanel = null;
            return;
        }

        _devPanel = new DeveloperPanel(this) { Owner = this };
        _devPanel.Closed += (_, _) => _devPanel = null;
        _devPanel.Left = Left + Width;
        _devPanel.Top = Top;
        _devPanel.Show();
        Focus();
    }

    private DevDiagnostics BuildDiagnostics()
    {
        var f = _recognizer.Filter;
        var s = _lastSample;
        var rawMag = System.Math.Sqrt((s.AccelX * s.AccelX) + (s.AccelY * s.AccelY) + (s.AccelZ * s.AccelZ));
        var now = _clock.Elapsed.TotalSeconds;
        return new DevDiagnostics(
            _controller.IsConnected, _packetHz, s.Button,
            s.AccelX, s.AccelY, s.AccelZ, s.GyroX, s.GyroY, s.GyroZ,
            rawMag, f.GravityX, f.GravityY, f.GravityZ,
            f.Current.X, f.Current.Y, f.Current.Z, f.Current.Magnitude, f.SmoothedMagnitude, _recognizer.LastJerk,
            _orientation.AimAngleRadians * 180 / System.Math.PI, _orientation.IsCalibrated, _orientation.IsCalibrating,
            _orientation.NeutralGravityX, _orientation.NeutralGravityY,
            _recognizer.WindowImpulseCount, _recognizer.WindowDirectionChanges, _recognizer.CurrentGesture.ToString(),
            System.Math.Max(0, _recognizer.ShakeCooldownUntil - now), System.Math.Max(0, _recognizer.ImpactCooldownUntil - now),
            _engine.BoostActive, _engine.BoostEnergy, _engine.SuperCharge, _engine.Heat, _engine.Overheated,
            _sampleQueue.Count, _recorder.IsRecording, _recorder.Count, _playing);
    }

    private void Toast(string message)
    {
        _manualToast = message;
        _manualToastUntil = _clock.Elapsed.TotalSeconds + 1.8;
    }

    // ---- IDeveloperHost ----

    public GestureSettings GestureSettings => _gestureSettings;

    public OrientationController Orientation => _orientation;

    public bool IsRecording => _recorder.IsRecording;

    public bool IsPlaying => _playing;

    public int RecordedSampleCount => _recorder.Count;

    public void Calibrate() => _orientation.BeginCalibration(_clock.Elapsed.TotalSeconds);

    public void ResetAim() => _orientation.ResetAim();

    public void StartRecording()
    {
        _recorder.Start(_clock.Elapsed.TotalSeconds);
        Toast("Recording…");
    }

    public void StopRecording()
    {
        var r = _recorder.Stop();
        if (r is { Samples.Count: > 0 })
        {
            _lastRecording = r;
            Toast($"Recorded {r.Samples.Count} samples");
        }
    }

    public void SaveRecording()
    {
        var rec = _lastRecording ?? _recorder.Current;
        if (rec is null || rec.Samples.Count == 0)
        {
            Toast("No recording to save");
            return;
        }

        var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "triki_imu.csv" };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dlg.FileName, rec.ToCsv());
                Toast("Recording saved");
            }
            catch (Exception ex)
            {
                Toast("Save failed: " + ex.Message);
            }
        }
    }

    public void LoadRecording()
    {
        var dlg = new OpenFileDialog { Filter = "CSV (*.csv)|*.csv" };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                _lastRecording = ImuRecordingModel.FromCsv(File.ReadAllText(dlg.FileName));
                Toast($"Loaded {_lastRecording.Samples.Count} samples");
            }
            catch (Exception ex)
            {
                Toast("Load failed: " + ex.Message);
            }
        }
    }

    public void StartPlayback()
    {
        var rec = _lastRecording ?? _recorder.Current;
        if (rec is null || rec.Samples.Count == 0)
        {
            Toast("No recording to play");
            return;
        }

        _recognizer.Reset();
        _orientation.ResetAim();
        _playback = new ImuPlaybackSource(rec, 1.0, loop: true);
        _playing = true;
        Toast("Playing recording (loop)");
    }

    public void StopPlayback()
    {
        _playback = null;
        _playing = false;
    }

    public void SimulateShake() => _engine.DevBoost();

    public void SimulateImpact() => _engine.DevSuper();

    public void SaveSettings()
    {
        _settings.CaptureFrom(_gestureSettings, _orientation);
        SettingsService.Save(_settings);
        Toast("Settings saved");
    }

    public void ResetSettings()
    {
        new GameSettings().ApplyTo(_gestureSettings, _orientation);
        Toast("Settings reset to defaults");
    }

    // ---- Cached brushes ----

    private static readonly Brush AsteroidFill = new SolidColorBrush(Color.FromRgb(0x5A, 0x60, 0x74));
    private static readonly Brush AsteroidStroke = new SolidColorBrush(Color.FromRgb(0x8A, 0x92, 0xAA));
    private static readonly Brush BasicFill = new SolidColorBrush(Color.FromRgb(0xFF, 0x6E, 0x6E));
    private static readonly Brush BasicStroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0xB0));
    private static readonly Brush ToughFill = new SolidColorBrush(Color.FromRgb(0xC9, 0x5C, 0xFF));
    private static readonly Brush ToughStroke = new SolidColorBrush(Color.FromRgb(0xE7, 0xB0, 0xFF));
    private static readonly Brush PlayerShotFill = new SolidColorBrush(Color.FromRgb(0xEA, 0xF6, 0xFF));
    private static readonly Brush EnemyShotFill = new SolidColorBrush(Color.FromRgb(0xFF, 0x6E, 0x6E));
    private static readonly Brush ConnectedBrush = new SolidColorBrush(Color.FromRgb(0x39, 0xD8, 0xFF));
    private static readonly Brush MutedBrushRef = new SolidColorBrush(Color.FromRgb(0x92, 0x98, 0xB4));

    private readonly System.Random _rng = new();
    private double _rngNext() => _rng.NextDouble();
}

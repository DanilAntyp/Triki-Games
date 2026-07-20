using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrikiPong.TrikiSpace;

namespace TrikiPong;

public partial class DeveloperPanel : Window
{
    private readonly IDeveloperHost _host;
    private readonly List<Action> _sliderRefreshers = new();

    public DeveloperPanel(IDeveloperHost host)
    {
        InitializeComponent();
        _host = host;
        BuildSliders();
        BuildButtons();
    }

    public void UpdateDiagnostics(DevDiagnostics d)
    {
        DiagnosticsText.Text =
            $"Triki: {(d.Connected ? "connected" : "keyboard")}   {d.PacketHz:F0} Hz   btn:{(d.Button ? "DOWN" : "up")}   queue:{d.QueueSize}\n" +
            $"accel g   x{d.Ax,7:F3} y{d.Ay,7:F3} z{d.Az,7:F3}\n" +
            $"gyro °/s  x{d.Gx,7:F1} y{d.Gy,7:F1} z{d.Gz,7:F1}   |a|{d.RawMag:F3}\n" +
            $"gravity   x{d.GravX,7:F3} y{d.GravY,7:F3} z{d.GravZ,7:F3}\n" +
            $"linear    x{d.LinX,7:F3} y{d.LinY,7:F3} z{d.LinZ,7:F3}\n" +
            $"|lin|{d.LinMag:F3}  smooth{d.SmoothMag:F3}  jerk{d.Jerk,8:F1}\n" +
            $"aim {d.AimDeg,6:F0}°   calib:{(d.Calibrating ? "RUNNING" : d.Calibrated ? "yes" : "no")}  n({d.NeutralX:F2},{d.NeutralY:F2})\n" +
            $"gesture: {d.GestureState}   impulses:{d.ImpulseCount}  dirChg:{d.DirChanges}\n" +
            $"cooldown shake:{d.ShakeCooldown:F2}s impact:{d.ImpactCooldown:F2}s\n" +
            $"boost:{(d.BoostActive ? "ON " : "off")} {d.BoostEnergy:F0}%  super:{d.SuperCharge:F0}%  heat:{d.Heat:F0}%{(d.Overheated ? " OVERHEAT" : "")}\n" +
            $"rec:{(d.Recording ? $"REC {d.RecordedCount}" : "idle")}  play:{(d.Playing ? "PLAYING" : "off")}";
    }

    public void RefreshSliders()
    {
        foreach (var refresh in _sliderRefreshers)
        {
            refresh();
        }
    }

    private void BuildSliders()
    {
        var g = _host.GestureSettings;
        var o = _host.Orientation;

        Section("Steering");
        AddSlider("Dead zone", 0, 0.4, false, () => o.DeadZone, v => o.DeadZone = v);
        AddSlider("Tilt sensitivity", 0.2, 6, false, () => o.TiltSensitivity, v => o.TiltSensitivity = v);
        AddSlider("Tilt smoothing", 0.02, 1, false, () => o.TiltSmoothing, v => o.TiltSmoothing = v);
        AddSlider("Gyro dead zone °/s", 0, 20, false, () => o.GyroDeadZoneDegPerSec, v => o.GyroDeadZoneDegPerSec = v);
        AddSlider("Gyro sensitivity", 0.1, 4, false, () => o.GyroSensitivity, v => o.GyroSensitivity = v);

        Section("Shake");
        AddSlider("Gravity filter alpha", 0.5, 0.98, false, () => g.GravityFilterAlpha, v => g.GravityFilterAlpha = v);
        AddSlider("Impulse threshold g", 0.1, 2.0, false, () => g.ShakeImpulseThreshold, v => g.ShakeImpulseThreshold = v);
        AddSlider("Required impulses", 2, 8, true, () => g.ShakeRequiredImpulseCount, v => g.ShakeRequiredImpulseCount = (int)v);
        AddSlider("Window ms", 200, 1500, true, () => g.ShakeWindowSeconds * 1000, v => g.ShakeWindowSeconds = v / 1000);
        AddSlider("Min direction changes", 1, 6, true, () => g.ShakeMinimumDirectionChanges, v => g.ShakeMinimumDirectionChanges = (int)v);
        AddSlider("Min impulse interval ms", 20, 250, true, () => g.ShakeMinimumImpulseIntervalSeconds * 1000, v => g.ShakeMinimumImpulseIntervalSeconds = v / 1000);
        AddSlider("Cooldown ms", 200, 3000, true, () => g.ShakeCooldownSeconds * 1000, v => g.ShakeCooldownSeconds = v / 1000);

        Section("Impact");
        AddSlider("Accel threshold g", 0.4, 3.0, false, () => g.ImpactAccelerationThreshold, v => g.ImpactAccelerationThreshold = v);
        AddSlider("Jerk threshold g/s", 1, 40, false, () => g.ImpactJerkThreshold, v => g.ImpactJerkThreshold = v);
        AddSlider("Confirmation delay ms", 80, 500, true, () => g.ImpactConfirmationDelaySeconds * 1000, v => g.ImpactConfirmationDelaySeconds = v / 1000);
        AddSlider("Cooldown ms", 200, 2000, true, () => g.ImpactCooldownSeconds * 1000, v => g.ImpactCooldownSeconds = v / 1000);
    }

    private void Section(string title)
    {
        SlidersHost.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x39, 0xD8, 0xFF)),
            Margin = new Thickness(0, 12, 0, 4),
        });
    }

    private void AddSlider(string label, double min, double max, bool integer, Func<double> get, Action<double> set)
    {
        var header = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x98, 0xB4)),
        };
        var value = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(value, 1);
        header.Children.Add(name);
        header.Children.Add(value);

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(get(), min, max),
            SmallChange = integer ? 1 : (max - min) / 100,
            IsSnapToTickEnabled = integer,
            TickFrequency = integer ? 1 : 0,
            Margin = new Thickness(0, 0, 0, 2),
        };

        void Sync() => value.Text = integer ? ((int)get()).ToString() : get().ToString("0.###", CultureInfo.InvariantCulture);

        slider.ValueChanged += (_, e) =>
        {
            set(integer ? Math.Round(e.NewValue) : e.NewValue);
            Sync();
        };
        Sync();

        _sliderRefreshers.Add(() =>
        {
            slider.Value = Math.Clamp(get(), min, max);
            Sync();
        });

        SlidersHost.Children.Add(header);
        SlidersHost.Children.Add(slider);
    }

    private void BuildButtons()
    {
        AddButton("Calibrate", _host.Calibrate);
        AddButton("Reset aim", _host.ResetAim);
        AddButton("Start rec", _host.StartRecording);
        AddButton("Stop rec", _host.StopRecording);
        AddButton("Save rec", _host.SaveRecording);
        AddButton("Load rec", _host.LoadRecording);
        AddButton("Play", _host.StartPlayback);
        AddButton("Stop play", _host.StopPlayback);
        AddButton("Sim shake", _host.SimulateShake);
        AddButton("Sim impact", _host.SimulateImpact);
        AddButton("Save settings", _host.SaveSettings);
        AddButton("Reset settings", () => { _host.ResetSettings(); RefreshSliders(); });
    }

    private void AddButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 4, 6, 0),
            Padding = new Thickness(10, 5, 10, 5),
            FontSize = 12,
        };
        button.Click += (_, _) => action();
        ButtonsHost.Children.Add(button);
    }
}

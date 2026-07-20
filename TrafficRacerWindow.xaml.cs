using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TrikiPong;

public partial class TrafficRacerWindow : Window
{
    private const double KeyboardSteerEquivalentDegPerSec = 70.0;

    // Perspective projection constants (view-only; the engine itself has no notion of pixels).
    private const double HorizonY = 150.0;
    private const double CameraDistance = 30.0;
    private const double RoadScreenScale = 3.8; // pixels per world unit at z = 0
    private const double DashSpacingWorld = 22.0;
    private const int DashCount = 10;
    private const int MaxCarVisuals = 10;
    private const double ShoulderWorld = 12.0;
    private const double EdgeStripeWorld = 5.0;

    private readonly TrafficRacerEngine _engine = new();
    private readonly ControllerInput _controller;
    private readonly CancellationTokenSource _controllerCts;
    private readonly Action _onBackToMenu;
    private readonly Stopwatch _clock = new();

    private readonly Polygon _shoulders = new();
    private readonly Polygon _road = new();
    private readonly Polygon _leftStripe = new();
    private readonly Polygon _rightStripe = new();
    private readonly List<Rectangle> _dashVisuals = new();
    private readonly List<Rectangle> _carVisuals = new();

    private double _lastElapsedSeconds;
    private double _worldScroll;
    private int _keyboardDirection;
    private bool _returningToMenu;

    public TrafficRacerWindow(ControllerInput controller, CancellationTokenSource controllerCts, Action onBackToMenu)
    {
        InitializeComponent();

        _controller = controller;
        _controllerCts = controllerCts;
        _onBackToMenu = onBackToMenu;
        _controller.StatusChanged += OnControllerStatusChanged;
        _engine.Crashed += OnCrashed;

        BuildScene();
    }

    /// <summary>
    /// Adds the scene layers to the canvas back-to-front. The sky is the canvas gradient (XAML);
    /// everything below the horizon (setting sun, ground, road, stripes, traffic) is layered here.
    /// </summary>
    private void BuildScene()
    {
        // Setting sun + halo, tucked behind the ground so only its upper half shows over the horizon.
        var sunGlow = new Ellipse
        {
            Width = 340,
            Height = 240,
            Fill = new RadialGradientBrush
            {
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0xB0, 0xFF, 0x8A, 0x5B), 0.0),
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0x6E, 0x9A), 1.0),
                },
            },
        };
        Canvas.SetLeft(sunGlow, 400 - 170);
        Canvas.SetTop(sunGlow, HorizonY - 150);
        Court.Children.Add(sunGlow);

        var sun = new Ellipse
        {
            Width = 150,
            Height = 150,
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0xFF, 0xE0, 0x6B), 0.0),
                    new GradientStop(Color.FromRgb(0xFF, 0x8A, 0x5B), 0.55),
                    new GradientStop(Color.FromRgb(0xFF, 0x4D, 0x8D), 1.0),
                },
            },
        };
        Canvas.SetLeft(sun, 400 - 75);
        Canvas.SetTop(sun, HorizonY - 75);
        Court.Children.Add(sun);

        // Ground: opaque terrain from the horizon down, hiding the lower half of the sun.
        var ground = new Rectangle
        {
            Width = Court.Width,
            Height = Court.Height - HorizonY,
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0x14, 0x2A, 0x3E), 0.0),
                    new GradientStop(Color.FromRgb(0x0A, 0x15, 0x24), 1.0),
                },
            },
        };
        Canvas.SetLeft(ground, 0);
        Canvas.SetTop(ground, HorizonY);
        Court.Children.Add(ground);

        _shoulders.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0x28, 0x40));
        Court.Children.Add(_shoulders);

        _road.Fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(0x24, 0x26, 0x33), 0.0),
                new GradientStop(Color.FromRgb(0x3A, 0x3D, 0x4C), 1.0),
            },
        };
        Court.Children.Add(_road);

        _leftStripe.Fill = new SolidColorBrush(Color.FromRgb(0x39, 0xD8, 0xFF));
        _leftStripe.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(0x39, 0xD8, 0xFF), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.7,
        };
        Court.Children.Add(_leftStripe);

        _rightStripe.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x8D));
        _rightStripe.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(0xFF, 0x4D, 0x8D), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.7,
        };
        Court.Children.Add(_rightStripe);

        for (var i = 0; i < DashCount; i++)
        {
            var dash = new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0xED, 0xE8, 0xC8)) };
            _dashVisuals.Add(dash);
            Court.Children.Add(dash);
        }

        for (var i = 0; i < MaxCarVisuals; i++)
        {
            var color = CarColor(i);
            var car = new Rectangle
            {
                Fill = new SolidColorBrush(color),
                RadiusX = 4,
                RadiusY = 4,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = color, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.75,
                },
            };
            _carVisuals.Add(car);
            Court.Children.Add(car);
        }
    }

    private static readonly Color[] CarPalette =
    {
        Color.FromRgb(0xFF, 0x5C, 0x5C),
        Color.FromRgb(0x5C, 0xC8, 0xFF),
        Color.FromRgb(0xFF, 0xC8, 0x5C),
        Color.FromRgb(0x8C, 0xFF, 0x8C),
    };

    private static Color CarColor(int index) => CarPalette[index % CarPalette.Length];

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        _clock.Start();
        CompositionTarget.Rendering += OnRendering;
        Render();
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _controller.StatusChanged -= OnControllerStatusChanged;

        if (!_returningToMenu)
        {
            _controllerCts.Cancel();
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var elapsed = _clock.Elapsed.TotalSeconds;
        var dt = elapsed - _lastElapsedSeconds;
        _lastElapsedSeconds = elapsed;

        if (dt <= 0 || dt > 0.25)
        {
            dt = 1.0 / 60.0;
        }

        var rollRate = _controller.RollRateDegPerSec + (_keyboardDirection * KeyboardSteerEquivalentDegPerSec);
        if (!_engine.IsCrashed)
        {
            _engine.Tick(dt, rollRate);
            _worldScroll += _engine.WorldSpeed * dt;
        }

        Render();
    }

    private void Render()
    {
        var playerX = _engine.PlayerX;
        const double near = 0.0;
        var far = TrafficRacerEngine.FarClip;
        var road = TrafficRacerEngine.RoadHalfWidth;

        _shoulders.Points = Trapezoid(road + ShoulderWorld, playerX, near, far);
        _road.Points = Trapezoid(road, playerX, near, far);
        _leftStripe.Points = EdgeStripe(-road, playerX, near, far);
        _rightStripe.Points = EdgeStripe(road, playerX, near, far);

        for (var i = 0; i < _dashVisuals.Count; i++)
        {
            var baseZ = i * DashSpacingWorld;
            var z = Mod(baseZ - _worldScroll, TrafficRacerEngine.FarClip);
            var dash = _dashVisuals[i];

            if (z <= 0 || z >= TrafficRacerEngine.FarClip - 1)
            {
                dash.Visibility = Visibility.Collapsed;
                continue;
            }

            var scale = Scale(z);
            var width = Math.Max(1.0, 3.0 * scale * RoadScreenScale);
            var height = Math.Max(1.0, 10.0 * scale);
            dash.Visibility = Visibility.Visible;
            dash.Width = width;
            dash.Height = height;
            Canvas.SetLeft(dash, ScreenX(0, z, playerX) - (width / 2));
            Canvas.SetTop(dash, ScreenY(z) - height);
        }

        for (var i = 0; i < _carVisuals.Count; i++)
        {
            var visual = _carVisuals[i];
            if (i >= _engine.Cars.Count)
            {
                visual.Visibility = Visibility.Collapsed;
                continue;
            }

            var car = _engine.Cars[i];
            var scale = Scale(car.Z);
            var width = TrafficRacerEngine.CarHalfWidth * 2 * scale * RoadScreenScale;
            var height = width / 1.5;

            visual.Visibility = Visibility.Visible;
            visual.Width = width;
            visual.Height = height;
            Canvas.SetLeft(visual, ScreenX(car.X, car.Z, playerX) - (width / 2));
            Canvas.SetTop(visual, ScreenY(car.Z) - height);
            Panel.SetZIndex(visual, (int)(1000 - car.Z));
        }

        DistanceText.Text = ((int)_engine.DistanceMeters).ToString();
        SpeedText.Text = ((int)_engine.WorldSpeed).ToString();
        DodgedText.Text = _engine.CarsDodged.ToString();
    }

    private PointCollection Trapezoid(double halfWidth, double playerX, double nearZ, double farZ) => new()
    {
        new Point(ScreenX(-halfWidth, nearZ, playerX), ScreenY(nearZ)),
        new Point(ScreenX(halfWidth, nearZ, playerX), ScreenY(nearZ)),
        new Point(ScreenX(halfWidth, farZ, playerX), ScreenY(farZ)),
        new Point(ScreenX(-halfWidth, farZ, playerX), ScreenY(farZ)),
    };

    private PointCollection EdgeStripe(double edgeX, double playerX, double nearZ, double farZ)
    {
        var inner = edgeX > 0 ? edgeX - EdgeStripeWorld : edgeX + EdgeStripeWorld;
        return new PointCollection
        {
            new Point(ScreenX(edgeX, nearZ, playerX), ScreenY(nearZ)),
            new Point(ScreenX(inner, nearZ, playerX), ScreenY(nearZ)),
            new Point(ScreenX(inner, farZ, playerX), ScreenY(farZ)),
            new Point(ScreenX(edgeX, farZ, playerX), ScreenY(farZ)),
        };
    }

    private static double Scale(double z) => CameraDistance / (CameraDistance + z);

    private double ScreenY(double z) => HorizonY + ((Court.Height - HorizonY) * Scale(z));

    private double ScreenX(double worldX, double z, double playerX) =>
        (Court.Width / 2.0) + ((worldX - playerX) * Scale(z) * RoadScreenScale);

    private static double Mod(double value, double modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private void OnCrashed()
    {
        Dispatcher.BeginInvoke(() =>
        {
            FinalScoreText.Text = $"{(int)_engine.DistanceMeters} m traveled  ·  {_engine.CarsDodged} cars dodged";
            CrashOverlay.Visibility = Visibility.Visible;
        });
    }

    private void OnRestartClick(object sender, RoutedEventArgs e)
    {
        CrashOverlay.Visibility = Visibility.Collapsed;
        _worldScroll = 0;
        _engine.Reset();
    }

    private void OnControllerStatusChanged(string message)
    {
        StatusText.Text = message;
    }

    private void OnBackToMenuClick(object sender, RoutedEventArgs e)
    {
        _returningToMenu = true;
        _onBackToMenu();
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left)
        {
            _keyboardDirection = -1;
        }
        else if (e.Key == Key.Right)
        {
            _keyboardDirection = 1;
        }
        else if (e.Key == Key.R && _engine.IsCrashed)
        {
            OnRestartClick(sender, e);
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left && _keyboardDirection == -1)
        {
            _keyboardDirection = 0;
        }
        else if (e.Key == Key.Right && _keyboardDirection == 1)
        {
            _keyboardDirection = 0;
        }
    }
}

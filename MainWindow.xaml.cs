using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TrikiPong;

public partial class MainWindow : Window
{
    private const double KeyboardRollEquivalentDegPerSec = 90.0;

    private readonly GameEngine _engine;
    private readonly ControllerInput _controller;
    private readonly CancellationTokenSource _controllerCts;
    private readonly Action _onBackToMenu;
    private readonly Stopwatch _clock = new();
    private double _lastElapsedSeconds;
    private int _keyboardDirection;
    private bool _returningToMenu;

    /// <summary>
    /// Takes over an already-connecting <see cref="ControllerInput"/> started from the
    /// connect screen, so switching to the game window doesn't restart the BLE scan.
    /// </summary>
    public MainWindow(ControllerInput controller, CancellationTokenSource controllerCts, Action onBackToMenu)
    {
        InitializeComponent();

        _engine = new GameEngine(Court.Width, Court.Height);
        _controller = controller;
        _controllerCts = controllerCts;
        _onBackToMenu = onBackToMenu;
        _controller.StatusChanged += OnControllerStatusChanged;
        _engine.Scored += OnScored;
    }

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

    private void OnBackToMenuClick(object sender, RoutedEventArgs e)
    {
        _returningToMenu = true;
        _onBackToMenu();
        Close();
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

        var rollRate = _controller.RollRateDegPerSec + (_keyboardDirection * KeyboardRollEquivalentDegPerSec);
        _engine.Tick(dt, rollRate);
        Render();
    }

    private void Render()
    {
        Canvas.SetLeft(AiPaddle, GameEngine.PaddleMargin);
        Canvas.SetTop(AiPaddle, _engine.AiPaddleY - (_engine.PaddleHeight / 2));

        Canvas.SetLeft(PlayerPaddle, Court.Width - GameEngine.PaddleMargin - _engine.PaddleWidth);
        Canvas.SetTop(PlayerPaddle, _engine.PlayerPaddleY - (_engine.PaddleHeight / 2));

        Canvas.SetLeft(Ball, _engine.BallX - (_engine.BallSize / 2));
        Canvas.SetTop(Ball, _engine.BallY - (_engine.BallSize / 2));

        AiScoreText.Text = _engine.AiScore.ToString();
        PlayerScoreText.Text = _engine.PlayerScore.ToString();
    }

    private void OnScored(bool playerScored)
    {
        // Score text is refreshed every frame in Render(); nothing extra needed per point.
    }

    private void OnControllerStatusChanged(string message)
    {
        StatusText.Text = message;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up)
        {
            _keyboardDirection = -1;
        }
        else if (e.Key == Key.Down)
        {
            _keyboardDirection = 1;
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up && _keyboardDirection == -1)
        {
            _keyboardDirection = 0;
        }
        else if (e.Key == Key.Down && _keyboardDirection == 1)
        {
            _keyboardDirection = 0;
        }
    }
}

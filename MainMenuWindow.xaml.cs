using System.ComponentModel;
using System.Threading;
using System.Windows;
using TrikiReader;

namespace TrikiPong;

public partial class MainMenuWindow : Window
{
    private readonly ControllerInput _controller;
    private readonly CancellationTokenSource _controllerCts;
    private bool _transitioningToGame;

    public MainMenuWindow(ControllerInput controller, CancellationTokenSource controllerCts)
    {
        InitializeComponent();

        _controller = controller;
        _controllerCts = controllerCts;
        _controller.StatusChanged += OnStatusChanged;
        _controller.DeviceConnected += OnDeviceConnected;
    }

    private void OnStatusChanged(string message)
    {
        StatusText.Text = message;
    }

    private void OnDeviceConnected(TrikiDeviceInfo info)
    {
        var battery = info.BatteryLevelPercent is null ? "" : $" - battery {info.BatteryLevelPercent}%";
        DeviceInfoText.Text = $"{info.DeviceName}{battery}";
    }

    private void OnPongClick(object sender, RoutedEventArgs e)
    {
        GoToGame(() => new MainWindow(_controller, _controllerCts, BackToMenu));
    }

    private void OnRacerClick(object sender, RoutedEventArgs e)
    {
        GoToGame(() => new TrafficRacerWindow(_controller, _controllerCts, BackToMenu));
    }

    private void OnSpaceClick(object sender, RoutedEventArgs e)
    {
        GoToGame(() => new TrikiSpaceWindow(_controller, _controllerCts, BackToMenu));
    }

    private void GoToGame(Func<Window> createGameWindow)
    {
        _transitioningToGame = true;
        var gameWindow = createGameWindow();
        gameWindow.Show();
        Close();
    }

    private void BackToMenu()
    {
        var menu = new MainMenuWindow(_controller, _controllerCts);
        menu.Show();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow { Owner = this };
        settings.ShowDialog();
    }

    private void OnWindowClosing(object sender, CancelEventArgs e)
    {
        _controller.StatusChanged -= OnStatusChanged;
        _controller.DeviceConnected -= OnDeviceConnected;

        if (!_transitioningToGame)
        {
            _controllerCts.Cancel();
        }
    }
}

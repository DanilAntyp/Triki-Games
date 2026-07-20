using System.ComponentModel;
using System.Threading;
using System.Windows;
using TrikiReader;

namespace TrikiPong;

public partial class ConnectWindow : Window
{
    private readonly ControllerInput _controller;
    private readonly CancellationTokenSource _controllerCts = new();
    private bool _connectStarted;
    private bool _transitioningToGame;

    public ConnectWindow()
    {
        InitializeComponent();

        _controller = new ControllerInput(Dispatcher);
        _controller.StatusChanged += OnStatusChanged;
        _controller.DeviceConnected += OnDeviceConnected;
    }

    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (_connectStarted)
        {
            return;
        }

        _connectStarted = true;
        ConnectButton.Content = "Connecting...";
        ConnectButton.IsEnabled = false;
        _ = _controller.RunAsync(_controllerCts.Token);
    }

    private void OnStatusChanged(string message)
    {
        StatusText.Text = message;
    }

    private void OnDeviceConnected(TrikiDeviceInfo info)
    {
        var battery = info.BatteryLevelPercent is null ? "" : $" • battery {info.BatteryLevelPercent}%";
        DeviceInfoText.Text = $"{info.DeviceName}{battery}";
        ConnectButton.Content = "Connected";
        StartGameButton.IsEnabled = true;
    }

    private void OnStartGameClick(object sender, RoutedEventArgs e)
    {
        GoToMenu();
    }

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        GoToMenu();
    }

    private void GoToMenu()
    {
        _transitioningToGame = true;
        var menuWindow = new MainMenuWindow(_controller, _controllerCts);
        menuWindow.Show();
        Close();
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

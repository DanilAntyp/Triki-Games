using System.Threading;
using System.Windows.Threading;
using TrikiReader;

namespace TrikiPong;

/// <summary>
/// Wraps <see cref="TrikiBleReader"/> with an auto-reconnect loop and exposes the controller's
/// roll rate (deg/s) for the game loop to read every frame. "Roll" here is the raw gyro Z-axis
/// rate, confirmed against real Triki hardware. Flip <see cref="RollSign"/> below if the paddle
/// ever needs to move opposite to the physical roll.
/// </summary>
public sealed class ControllerInput
{
    private const double RollSign = 1.0;
    private const double DeadzoneDegPerSec = 3.0;

    private readonly Dispatcher _dispatcher;
    private readonly UiUpdateGate _statusGate = new(TimeSpan.FromMilliseconds(150));
    private double _rollRateDegPerSec;
    private string _latestLogMessage = string.Empty;

    public ControllerInput(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public event Action<string>? StatusChanged;
    public event Action<TrikiDeviceInfo>? DeviceConnected;

    /// <summary>Raised on the BLE thread with each full IMU sample. Used by Triki Space, which
    /// needs the whole accel+gyro vector rather than just roll. Subscribers must be thread-safe
    /// and must not touch the UI directly.</summary>
    public event EventHandler<ImuSample>? ImuSampleReceived;

    /// <summary>Raised on the BLE thread with raw notification bytes (for button investigation).</summary>
    public event EventHandler<byte[]>? RawNotificationReceived;

    public bool IsConnected { get; private set; }

    /// <summary>
    /// Raw roll rate scaled by the user's configured <see cref="AppSettings.Sensitivity"/>, read
    /// live so changes made in the settings screen apply immediately without reconnecting.
    /// </summary>
    public double RollRateDegPerSec => Volatile.Read(ref _rollRateDegPerSec) * AppSettings.Sensitivity;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var options = AppOptions.Default;

        while (!cancellationToken.IsCancellationRequested)
        {
            var reader = new TrikiBleReader(options);
            reader.SampleReceived += OnSampleReceived;
            reader.LogMessage += OnLogMessage;
            reader.DeviceInfoReceived += OnDeviceInfoReceived;
            reader.ConnectionLost += OnConnectionLost;
            reader.RawNotificationReceived += OnRawNotification;

            try
            {
                RaiseStatusImmediate("Searching for Triki controller...");
                await reader.RunAsync(cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                RaiseStatusImmediate($"Controller error: {ex.Message}");
            }
            finally
            {
                reader.SampleReceived -= OnSampleReceived;
                reader.LogMessage -= OnLogMessage;
                reader.DeviceInfoReceived -= OnDeviceInfoReceived;
                reader.ConnectionLost -= OnConnectionLost;
                reader.RawNotificationReceived -= OnRawNotification;
            }

            IsConnected = false;
            Volatile.Write(ref _rollRateDegPerSec, 0.0);

            if (!cancellationToken.IsCancellationRequested)
            {
                RaiseStatusImmediate("Controller disconnected. Retrying in 3s... (arrow keys still work)");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }

    private void OnSampleReceived(object? sender, ImuSample sample)
    {
        var rate = sample.GyroZ * RollSign;
        if (Math.Abs(rate) < DeadzoneDegPerSec)
        {
            rate = 0.0;
        }

        Volatile.Write(ref _rollRateDegPerSec, rate);

        ImuSampleReceived?.Invoke(this, sample);
    }

    private void OnRawNotification(object? sender, byte[] bytes)
    {
        RawNotificationReceived?.Invoke(this, bytes);
    }

    private void OnDeviceInfoReceived(object? sender, TrikiDeviceInfo info)
    {
        IsConnected = true;
        var battery = info.BatteryLevelPercent is null ? "" : $" (battery {info.BatteryLevelPercent}%)";
        RaiseStatusImmediate($"Connected: {info.DeviceName}{battery}");
        _dispatcher.BeginInvoke(() => DeviceConnected?.Invoke(info));
    }

    private void OnConnectionLost(object? sender, EventArgs e)
    {
        IsConnected = false;
        RaiseStatusImmediate("Controller disconnected.");
    }

    private void OnLogMessage(object? sender, string message)
    {
        _latestLogMessage = message;
        if (!_statusGate.TryBeginSchedule())
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            StatusChanged?.Invoke(_latestLogMessage);
            _statusGate.Complete();
        });
    }

    private void RaiseStatusImmediate(string message)
    {
        _dispatcher.BeginInvoke(() => StatusChanged?.Invoke(message));
    }
}

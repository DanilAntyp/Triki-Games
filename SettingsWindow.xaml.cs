using System.ComponentModel;
using System.Globalization;
using System.Windows;

namespace TrikiPong;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        // Read before InitializeComponent(): setting the slider's Minimum/Maximum in XAML
        // coerces its default Value into range, which fires OnSensitivityChanged and would
        // otherwise stomp the stored setting before we get a chance to apply it below.
        var currentSensitivity = AppSettings.Sensitivity;
        InitializeComponent();
        SensitivitySlider.Value = currentSensitivity;
        UpdateValueText(currentSensitivity);
    }

    private void OnSensitivityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        AppSettings.Sensitivity = e.NewValue;
        UpdateValueText(e.NewValue);
    }

    private void UpdateValueText(double value)
    {
        if (SensitivityValueText is not null)
        {
            SensitivityValueText.Text = value.ToString("0.0", CultureInfo.InvariantCulture) + "x";
        }
    }

    private void OnDoneClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowClosing(object sender, CancelEventArgs e)
    {
        AppSettings.Save();
    }
}

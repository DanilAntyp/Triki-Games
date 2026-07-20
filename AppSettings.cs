using System.IO;
using System.Text.Json;

namespace TrikiPong;

/// <summary>
/// Small persisted user-preference store. Currently only holds controller sensitivity, but is
/// the natural place to add future options without touching the game windows.
/// </summary>
public static class AppSettings
{
    public const double MinSensitivity = 0.4;
    public const double MaxSensitivity = 2.5;
    public const double DefaultSensitivity = 1.0;

    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TrikiPong",
        "settings.json");

    private static double _sensitivity = DefaultSensitivity;
    private static bool _loaded;

    /// <summary>Multiplier applied to the controller's raw roll rate. 1.0 = unmodified.</summary>
    public static double Sensitivity
    {
        get
        {
            EnsureLoaded();
            return _sensitivity;
        }
        set
        {
            _loaded = true;
            _sensitivity = Math.Clamp(value, MinSensitivity, MaxSensitivity);
        }
    }

    public static void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(new StoredSettings(_sensitivity)));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return;
            }

            var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(SettingsFilePath));
            if (stored is not null)
            {
                _sensitivity = Math.Clamp(stored.Sensitivity, MinSensitivity, MaxSensitivity);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }
    }

    private sealed record StoredSettings(double Sensitivity);
}

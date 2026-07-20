using System.IO;
using System.Text.Json;

namespace TrikiPong.TrikiSpace;

/// <summary>
/// Serializable snapshot of every tunable that the dev panel exposes. Kept separate from the live
/// <see cref="GestureSettings"/> / <see cref="OrientationController"/> objects so we can load/save
/// without the game holding a settings reference in its hot path.
/// </summary>
public sealed class GameSettings
{
    // Orientation / steering
    public double DeadZone { get; set; } = 0.08;
    public double TiltSensitivity { get; set; } = 2.2;
    public double TiltSmoothing { get; set; } = 0.2;
    public bool InvertHorizontal { get; set; }
    public bool InvertVertical { get; set; }
    public double GyroDeadZoneDegPerSec { get; set; } = 1.0;
    public double GyroSensitivity { get; set; } = 1.0;

    // Gesture detection
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

    public void ApplyTo(GestureSettings gestures, OrientationController orientation)
    {
        gestures.GravityFilterAlpha = GravityFilterAlpha;
        gestures.ShakeImpulseThreshold = ShakeImpulseThreshold;
        gestures.ShakeRequiredImpulseCount = ShakeRequiredImpulseCount;
        gestures.ShakeWindowSeconds = ShakeWindowSeconds;
        gestures.ShakeMinimumDirectionChanges = ShakeMinimumDirectionChanges;
        gestures.ShakeMinimumImpulseIntervalSeconds = ShakeMinimumImpulseIntervalSeconds;
        gestures.ShakeCooldownSeconds = ShakeCooldownSeconds;
        gestures.ImpactAccelerationThreshold = ImpactAccelerationThreshold;
        gestures.ImpactJerkThreshold = ImpactJerkThreshold;
        gestures.ImpactConfirmationDelaySeconds = ImpactConfirmationDelaySeconds;
        gestures.ImpactCooldownSeconds = ImpactCooldownSeconds;

        orientation.DeadZone = DeadZone;
        orientation.TiltSensitivity = TiltSensitivity;
        orientation.TiltSmoothing = TiltSmoothing;
        orientation.InvertHorizontal = InvertHorizontal;
        orientation.InvertVertical = InvertVertical;
        orientation.GyroDeadZoneDegPerSec = GyroDeadZoneDegPerSec;
        orientation.GyroSensitivity = GyroSensitivity;
    }

    public void CaptureFrom(GestureSettings gestures, OrientationController orientation)
    {
        GravityFilterAlpha = gestures.GravityFilterAlpha;
        ShakeImpulseThreshold = gestures.ShakeImpulseThreshold;
        ShakeRequiredImpulseCount = gestures.ShakeRequiredImpulseCount;
        ShakeWindowSeconds = gestures.ShakeWindowSeconds;
        ShakeMinimumDirectionChanges = gestures.ShakeMinimumDirectionChanges;
        ShakeMinimumImpulseIntervalSeconds = gestures.ShakeMinimumImpulseIntervalSeconds;
        ShakeCooldownSeconds = gestures.ShakeCooldownSeconds;
        ImpactAccelerationThreshold = gestures.ImpactAccelerationThreshold;
        ImpactJerkThreshold = gestures.ImpactJerkThreshold;
        ImpactConfirmationDelaySeconds = gestures.ImpactConfirmationDelaySeconds;
        ImpactCooldownSeconds = gestures.ImpactCooldownSeconds;

        DeadZone = orientation.DeadZone;
        TiltSensitivity = orientation.TiltSensitivity;
        TiltSmoothing = orientation.TiltSmoothing;
        InvertHorizontal = orientation.InvertHorizontal;
        InvertVertical = orientation.InvertVertical;
        GyroDeadZoneDegPerSec = orientation.GyroDeadZoneDegPerSec;
        GyroSensitivity = orientation.GyroSensitivity;
    }
}

public static class SettingsService
{
    private static readonly string FilePath = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "TrikiPong", "space_settings.json");

    public static GameSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(FilePath));
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }

        return new GameSettings();
    }

    public static void Save(GameSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

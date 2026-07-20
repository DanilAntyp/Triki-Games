namespace TrikiPong.TrikiSpace;

/// <summary>Everything the dev panel displays each frame. A plain snapshot so the panel never
/// reaches into game internals.</summary>
public readonly record struct DevDiagnostics(
    bool Connected, double PacketHz, bool Button,
    double Ax, double Ay, double Az, double Gx, double Gy, double Gz,
    double RawMag, double GravX, double GravY, double GravZ,
    double LinX, double LinY, double LinZ, double LinMag, double SmoothMag, double Jerk,
    double AimDeg, bool Calibrated, bool Calibrating, double NeutralX, double NeutralY,
    int ImpulseCount, int DirChanges, string GestureState,
    double ShakeCooldown, double ImpactCooldown,
    bool BoostActive, double BoostEnergy, double SuperCharge, double Heat, bool Overheated,
    int QueueSize, bool Recording, int RecordedCount, bool Playing);

/// <summary>Actions and shared objects the dev panel drives. Implemented by the game window.</summary>
public interface IDeveloperHost
{
    GestureSettings GestureSettings { get; }
    OrientationController Orientation { get; }

    void Calibrate();
    void ResetAim();
    void StartRecording();
    void StopRecording();
    void SaveRecording();
    void LoadRecording();
    void StartPlayback();
    void StopPlayback();
    void SimulateShake();
    void SimulateImpact();
    void SaveSettings();
    void ResetSettings();

    bool IsRecording { get; }
    bool IsPlaying { get; }
    int RecordedSampleCount { get; }
}

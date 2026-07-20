namespace TrikiPong;

public readonly record struct TrafficCar(double X, double Z);

/// <summary>
/// Self-contained endless first-person traffic racer physics/state. No WPF types here, mirroring
/// <see cref="GameEngine"/>, so the rendering layer just projects world coordinates to the screen.
///
/// World space is a straight road of half-width <see cref="RoadHalfWidth"/> centered on X = 0,
/// running away from the camera along Z (0 = at the camera, <see cref="FarClip"/> = horizon).
/// Traffic cars spawn at the far clip and travel toward the camera at <see cref="WorldSpeed"/>;
/// the player only ever moves laterally (steering), which is what makes it "endless".
/// </summary>
public sealed class TrafficRacerEngine
{
    public const double RoadHalfWidth = 100.0;
    public const double FarClip = 220.0;
    public const double CarHalfWidth = 14.0;

    private const double CollisionZ = 10.0;
    private const double SteerUnitsPerDegreePerSecond = 2.6;
    private const double InitialWorldSpeed = 60.0;
    private const double MaxWorldSpeed = 260.0;
    private const double SpeedRampPerSecond = 1.6;
    private const double MinSpawnGapSeconds = 0.55;
    private const double MaxSpawnGapSeconds = 1.35;
    private const double MetersPerWorldUnit = 0.1;

    private readonly Random _random = new();
    private readonly List<TrafficCar> _cars = new();
    private double _spawnTimer;

    public TrafficRacerEngine()
    {
        Reset();
    }

    public double PlayerX { get; private set; }
    public double WorldSpeed { get; private set; }
    public double DistanceMeters { get; private set; }
    public int CarsDodged { get; private set; }
    public bool IsCrashed { get; private set; }
    public IReadOnlyList<TrafficCar> Cars => _cars;

    /// <summary>Raised once, the instant a collision ends the run.</summary>
    public event Action? Crashed;

    public void Reset()
    {
        _cars.Clear();
        PlayerX = 0;
        WorldSpeed = InitialWorldSpeed;
        DistanceMeters = 0;
        CarsDodged = 0;
        IsCrashed = false;
        _spawnTimer = 1.0;
    }

    /// <param name="dt">Elapsed seconds since the last tick.</param>
    /// <param name="playerRollRateDegPerSec">Positive steers right, negative steers left.</param>
    public void Tick(double dt, double playerRollRateDegPerSec)
    {
        if (IsCrashed)
        {
            return;
        }

        var maxOffset = RoadHalfWidth - CarHalfWidth;
        PlayerX = Math.Clamp(PlayerX + (playerRollRateDegPerSec * SteerUnitsPerDegreePerSecond * dt), -maxOffset, maxOffset);

        WorldSpeed = Math.Min(MaxWorldSpeed, WorldSpeed + (SpeedRampPerSecond * dt));
        DistanceMeters += WorldSpeed * dt * MetersPerWorldUnit;

        for (var i = _cars.Count - 1; i >= 0; i--)
        {
            var car = _cars[i] with { Z = _cars[i].Z - (WorldSpeed * dt) };
            _cars[i] = car;

            if (car.Z <= CollisionZ)
            {
                if (Math.Abs(car.X - PlayerX) < CarHalfWidth * 2)
                {
                    IsCrashed = true;
                    Crashed?.Invoke();
                    return;
                }

                _cars.RemoveAt(i);
                CarsDodged++;
            }
        }

        _spawnTimer -= dt;
        if (_spawnTimer <= 0)
        {
            SpawnCar();
            var difficulty = Math.Clamp(WorldSpeed / MaxWorldSpeed, 0.0, 1.0);
            var gap = MaxSpawnGapSeconds - ((MaxSpawnGapSeconds - MinSpawnGapSeconds) * difficulty);
            _spawnTimer = gap + (_random.NextDouble() * 0.3);
        }
    }

    private void SpawnCar()
    {
        var maxOffset = RoadHalfWidth - CarHalfWidth;
        var x = ((_random.NextDouble() * 2.0) - 1.0) * maxOffset;
        _cars.Add(new TrafficCar(x, FarClip));
    }
}

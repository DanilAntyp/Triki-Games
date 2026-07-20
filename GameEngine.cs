namespace TrikiPong;

/// <summary>
/// Self-contained 2D Pong physics/state. No WPF types here so it can be unit-tested or reused
/// independently of the rendering layer.
/// </summary>
public sealed class GameEngine
{
    public const double PaddleMargin = 24;

    private const double MaxBallSpeed = 900;
    private const double InitialBallSpeed = 320;
    private const double AiSpeed = 260;
    private const double MaxBounceAngleRad = Math.PI / 3; // 60 degrees
    private const double PixelsPerDegreePerSecond = 6.0;

    private readonly Random _random = new();
    private double _ballVx;
    private double _ballVy;

    public GameEngine(double courtWidth, double courtHeight, double paddleWidth = 14, double paddleHeight = 90, double ballSize = 14)
    {
        CourtWidth = courtWidth;
        CourtHeight = courtHeight;
        PaddleWidth = paddleWidth;
        PaddleHeight = paddleHeight;
        BallSize = ballSize;
        Reset(serveToPlayer: true);
    }

    public double CourtWidth { get; }
    public double CourtHeight { get; }
    public double PaddleWidth { get; }
    public double PaddleHeight { get; }
    public double BallSize { get; }

    public double PlayerPaddleY { get; private set; }
    public double AiPaddleY { get; private set; }
    public double BallX { get; private set; }
    public double BallY { get; private set; }
    public int PlayerScore { get; private set; }
    public int AiScore { get; private set; }

    public event Action<bool>? Scored;

    public void Reset(bool serveToPlayer)
    {
        PlayerPaddleY = CourtHeight / 2;
        AiPaddleY = CourtHeight / 2;
        BallX = CourtWidth / 2;
        BallY = CourtHeight / 2;

        var angle = (_random.NextDouble() * 0.6) - 0.3;
        var dirX = serveToPlayer ? 1.0 : -1.0;
        _ballVx = Math.Cos(angle) * InitialBallSpeed * dirX;
        _ballVy = Math.Sin(angle) * InitialBallSpeed;
    }

    /// <param name="dt">Elapsed seconds since the last tick.</param>
    /// <param name="playerRollRateDegPerSec">Positive rolls the paddle down, negative rolls it up.</param>
    public void Tick(double dt, double playerRollRateDegPerSec)
    {
        var playerVy = playerRollRateDegPerSec * PixelsPerDegreePerSecond;
        PlayerPaddleY = Clamp(PlayerPaddleY + (playerVy * dt), PaddleHeight / 2, CourtHeight - (PaddleHeight / 2));

        var diff = BallY - AiPaddleY;
        var maxStep = AiSpeed * dt;
        AiPaddleY += Math.Clamp(diff, -maxStep, maxStep);
        AiPaddleY = Clamp(AiPaddleY, PaddleHeight / 2, CourtHeight - (PaddleHeight / 2));

        BallX += _ballVx * dt;
        BallY += _ballVy * dt;

        if (BallY - (BallSize / 2) <= 0 && _ballVy < 0)
        {
            BallY = BallSize / 2;
            _ballVy = -_ballVy;
        }
        else if (BallY + (BallSize / 2) >= CourtHeight && _ballVy > 0)
        {
            BallY = CourtHeight - (BallSize / 2);
            _ballVy = -_ballVy;
        }

        var aiPaddleRight = PaddleMargin + PaddleWidth;
        if (_ballVx < 0 && BallX - (BallSize / 2) <= aiPaddleRight)
        {
            if (Math.Abs(BallY - AiPaddleY) <= (PaddleHeight / 2) + (BallSize / 2))
            {
                BallX = aiPaddleRight + (BallSize / 2);
                Reflect(AiPaddleY, dirX: 1.0);
            }
        }

        var playerPaddleLeft = CourtWidth - PaddleMargin - PaddleWidth;
        if (_ballVx > 0 && BallX + (BallSize / 2) >= playerPaddleLeft)
        {
            if (Math.Abs(BallY - PlayerPaddleY) <= (PaddleHeight / 2) + (BallSize / 2))
            {
                BallX = playerPaddleLeft - (BallSize / 2);
                Reflect(PlayerPaddleY, dirX: -1.0);
            }
        }

        if (BallX < -BallSize)
        {
            AiScore++;
            Scored?.Invoke(false);
            Reset(serveToPlayer: false);
        }
        else if (BallX > CourtWidth + BallSize)
        {
            PlayerScore++;
            Scored?.Invoke(true);
            Reset(serveToPlayer: true);
        }
    }

    private void Reflect(double paddleCenterY, double dirX)
    {
        var relative = Math.Clamp((BallY - paddleCenterY) / (PaddleHeight / 2.0), -1.0, 1.0);
        var speed = Math.Min(Math.Sqrt((_ballVx * _ballVx) + (_ballVy * _ballVy)) * 1.05, MaxBallSpeed);
        var angle = relative * MaxBounceAngleRad;
        _ballVx = Math.Cos(angle) * speed * dirX;
        _ballVy = Math.Sin(angle) * speed;
    }

    private static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);
}

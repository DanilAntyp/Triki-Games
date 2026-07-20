using System;
using System.Collections.Generic;

namespace TrikiPong.TrikiSpace;

public enum EntityKind { Asteroid, EnemyBasic, EnemyTough }

public sealed class Mover
{
    public double X, Y, Vx, Vy, Radius, Health, MaxHealth;
    public EntityKind Kind;
    public double Spin;      // visual rotation rate (deg/s)
    public double Angle;     // visual rotation (deg)
    public double FireTimer; // seconds until this enemy fires again
    public bool Alive = true;
}

public sealed class Shot
{
    public double X, Y, Vx, Vy, Radius, Damage, Life;
    public bool Piercing;
    public bool Hostile;     // true = enemy shot travelling down at the player
    public bool Alive = true;
}

/// <summary>
/// Self-contained Triki Space simulation. No WPF types. The window feeds it steering, aim, a
/// fire flag and gesture triggers each frame, then reads entity lists to render.
/// </summary>
public sealed class SpaceGameEngine
{
    public const double FieldWidth = 900;
    public const double FieldHeight = 600;

    private const double PlayerRadius = 16;
    private const double PlayerFixedY = FieldHeight - 50; // ship is pinned to the bottom lane
    private const double ProjectileSpeed = 640;
    private const double EnemyShotSpeed = 300;
    private const double FireInterval = 0.2; // seconds between shots (continuous, no overheat)

    private readonly Random _rng = new();
    private readonly List<Shot> _shots = new();
    private readonly List<Mover> _movers = new();

    private double _fireTimer;
    private double _spawnTimer = 1.0;
    private double _elapsed;
    private double _superCooldownUntil;
    private double _now;

    public SpaceGameEngine() => Reset();

    // Player state
    public double PlayerX { get; private set; }
    public double PlayerY { get; private set; }
    public double PlayerVx { get; private set; }
    public double PlayerVy { get; private set; }
    public double AimAngle => -Math.PI / 2; // ship always fires straight up
    public double Health { get; private set; }
    public double MaxHealth { get; } = 100;
    public bool IsInvulnerable => _now < _invulnUntil;
    private double _invulnUntil;

    // Weapon / boost / super meters (0..100 where noted)
    public double Heat { get; private set; }
    public double MaxHeat { get; } = 100;
    public bool Overheated { get; private set; }
    public double BoostEnergy { get; private set; }
    public bool BoostActive { get; private set; }
    private double _boostUntil;
    private double _boostSpeedMult = 1;
    public double SuperCharge { get; private set; }
    public double SuperChargeMax { get; } = 100;

    public int Score { get; private set; }
    public int Wave => 1 + (int)(_elapsed / 25);
    public bool GameOver { get; private set; }
    public bool Paused { get; set; }

    public double DamageFlash { get; private set; }
    public double SuperFlash { get; private set; }
    public double NoChargeFlash { get; private set; }

    public IReadOnlyList<Shot> Shots => _shots;
    public IReadOnlyList<Mover> Movers => _movers;

    public event Action? PlayerHit;
    public event Action? PlayerDied;

    public void Reset()
    {
        _shots.Clear();
        _movers.Clear();
        PlayerX = FieldWidth / 2;
        PlayerY = PlayerFixedY;
        PlayerVx = PlayerVy = 0;
        Health = MaxHealth;
        Heat = 0;
        Overheated = false;
        BoostEnergy = 100;
        BoostActive = false;
        SuperCharge = 0;
        Score = 0;
        _elapsed = 0;
        _spawnTimer = 1.0;
        _fireTimer = 0;
        _invulnUntil = 0;
        _superCooldownUntil = 0;
        _boostUntil = 0;
        _boostSpeedMult = 1;
        GameOver = false;
        Paused = false;
        DamageFlash = SuperFlash = NoChargeFlash = 0;
    }

    /// <param name="moveVelocityX">Horizontal speed in px/s (from the controller roll rate, like the
    /// Pong paddle). The ship only slides left/right along the bottom lane.</param>
    /// <param name="fire">True while the weapon should auto-fire.</param>
    public void Update(double dt, double moveVelocityX, bool fire)
    {
        _now += dt;
        DamageFlash = Math.Max(0, DamageFlash - dt * 2.5);
        SuperFlash = Math.Max(0, SuperFlash - dt * 2.0);
        NoChargeFlash = Math.Max(0, NoChargeFlash - dt * 2.0);

        if (GameOver || Paused)
        {
            return;
        }

        _elapsed += dt;
        var difficulty = Math.Clamp(_elapsed / 120.0, 0, 1);

        UpdatePlayer(dt, moveVelocityX);
        UpdateWeapon(dt, fire);
        UpdateBoost(dt);

        BoostEnergy = Math.Min(100, BoostEnergy + (18 * dt));

        UpdateShots(dt);
        UpdateMovers(dt);
        UpdateSpawning(dt, difficulty);
        ResolveCollisions();

        if (Health <= 0 && !GameOver)
        {
            Health = 0;
            GameOver = true;
            PlayerDied?.Invoke();
        }
    }

    private void UpdatePlayer(double dt, double moveVelocityX)
    {
        // Direct rate control, exactly like the Pong paddle: roll rate -> horizontal velocity.
        PlayerVx = moveVelocityX * (BoostActive ? _boostSpeedMult : 1);
        PlayerX = Math.Clamp(PlayerX + (PlayerVx * dt), PlayerRadius, FieldWidth - PlayerRadius);

        // Pinned to the bottom lane: no vertical movement.
        PlayerVy = 0;
        PlayerY = PlayerFixedY;
    }

    private void UpdateWeapon(double dt, bool fire)
    {
        // Continuous auto-fire on a fixed cadence — no heat, no overheat cooldown.
        _fireTimer -= dt;
        if (fire && _fireTimer <= 0)
        {
            FireProjectile();
            _fireTimer = FireInterval;
        }
    }

    private void FireProjectile()
    {
        // Always straight up.
        _shots.Add(new Shot
        {
            X = PlayerX,
            Y = PlayerY - PlayerRadius,
            Vx = 0,
            Vy = -ProjectileSpeed,
            Radius = 4,
            Damage = 10,
            Life = 1.6,
        });
    }

    private void UpdateBoost(double dt)
    {
        if (BoostActive && _now >= _boostUntil)
        {
            BoostActive = false;
            _boostSpeedMult = 1;
        }
    }

    public void ActivateBoost(double strength)
    {
        const double cost = 35;
        if (BoostActive || BoostEnergy < cost)
        {
            if (!BoostActive)
            {
                NoChargeFlash = 1;
            }

            return;
        }

        strength = Math.Clamp(strength, 0, 1);
        BoostEnergy -= cost;
        BoostActive = true;
        _boostUntil = _now + (0.8 + (strength * 1.2));
        _boostSpeedMult = 1.5 + (strength * 1.0);
    }

    public bool TryFireSuper(double strength)
    {
        if (SuperCharge < SuperChargeMax || _now < _superCooldownUntil)
        {
            NoChargeFlash = 1;
            return false;
        }

        _shots.Add(new Shot
        {
            X = PlayerX,
            Y = PlayerY - PlayerRadius,
            Vx = 0,
            Vy = -ProjectileSpeed * 0.9,
            Radius = 22,
            Damage = 120,
            Life = 2.0,
            Piercing = true,
        });

        SuperCharge = 0;
        _superCooldownUntil = _now + 0.6;
        SuperFlash = 1;
        return true;
    }

    public void AddSuperCharge(double amount) => SuperCharge = Math.Clamp(SuperCharge + amount, 0, SuperChargeMax);

    /// <summary>Dev-panel helpers: guarantee the meter is full, then trigger the effect.</summary>
    public void DevBoost()
    {
        BoostEnergy = 100;
        ActivateBoost(0.9);
    }

    public void DevSuper()
    {
        SuperCharge = SuperChargeMax;
        TryFireSuper(1.0);
    }

    private void UpdateShots(double dt)
    {
        for (var i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            s.Life -= dt;
            if (!s.Alive || s.Life <= 0 || s.X < -40 || s.X > FieldWidth + 40 || s.Y < -40 || s.Y > FieldHeight + 40)
            {
                _shots.RemoveAt(i);
            }
        }
    }

    private void UpdateMovers(double dt)
    {
        for (var i = _movers.Count - 1; i >= 0; i--)
        {
            var m = _movers[i];
            if (m.Kind != EntityKind.Asteroid)
            {
                // Enemies descend from the top and drift horizontally to line up with the player.
                var descend = m.Kind == EntityKind.EnemyTough ? 70.0 : 100.0;
                var track = m.Kind == EntityKind.EnemyTough ? 60.0 : 90.0;
                var toPlayerX = Math.Clamp((PlayerX - m.X) / 40.0, -1, 1) * track;
                m.Vx = Approach(m.Vx, toPlayerX, 260 * dt);
                m.Vy = Approach(m.Vy, descend, 220 * dt);

                // Fire downward once the enemy is on screen.
                m.FireTimer -= dt;
                if (m.FireTimer <= 0 && m.Y > 0 && m.Y < FieldHeight * 0.85)
                {
                    EnemyFire(m);
                    m.FireTimer = m.Kind == EntityKind.EnemyTough ? 1.1 : 1.7;
                    m.FireTimer += _rng.NextDouble() * 0.6;
                }
            }

            m.X += m.Vx * dt;
            m.Y += m.Vy * dt;
            m.Angle += m.Spin * dt;

            var margin = m.Radius + 60;
            if (!m.Alive || m.X < -margin || m.X > FieldWidth + margin || m.Y < -margin || m.Y > FieldHeight + margin)
            {
                _movers.RemoveAt(i);
            }
        }
    }

    private void EnemyFire(Mover m)
    {
        // Aim roughly at the player but always with a downward component.
        var ang = Math.Atan2(PlayerY - m.Y, PlayerX - m.X);
        var dx = Math.Cos(ang);
        var dy = Math.Max(0.35, Math.Sin(ang)); // never fire upward
        var len = Math.Sqrt((dx * dx) + (dy * dy));
        dx /= len;
        dy /= len;
        _shots.Add(new Shot
        {
            X = m.X,
            Y = m.Y + m.Radius,
            Vx = dx * EnemyShotSpeed,
            Vy = dy * EnemyShotSpeed,
            Radius = 5,
            Damage = m.Kind == EntityKind.EnemyTough ? 16 : 10,
            Life = 3.0,
            Hostile = true,
        });
    }

    private void UpdateSpawning(double dt, double difficulty)
    {
        _spawnTimer -= dt;
        if (_spawnTimer > 0)
        {
            return;
        }

        _spawnTimer = Lerp(1.4, 0.5, difficulty) + (_rng.NextDouble() * 0.3);
        SpawnFromTop(difficulty);
    }

    private void SpawnFromTop(double difficulty)
    {
        // Everything now descends from above the top edge.
        var x = 40 + (_rng.NextDouble() * (FieldWidth - 80));
        var y = -30.0;

        var roll = _rng.NextDouble();
        EntityKind kind;
        if (roll < 0.5)
        {
            kind = EntityKind.Asteroid;
        }
        else if (roll < 0.5 + (0.35 * (1 - difficulty)))
        {
            kind = EntityKind.EnemyBasic;
        }
        else
        {
            kind = EntityKind.EnemyTough;
        }

        var m = new Mover { X = x, Y = y, Kind = kind, Angle = _rng.NextDouble() * 360 };
        switch (kind)
        {
            case EntityKind.Asteroid:
                m.Radius = 18 + (_rng.NextDouble() * 20);
                m.MaxHealth = m.Health = 20 + (m.Radius * 1.5);
                m.Spin = (_rng.NextDouble() - 0.5) * 120;
                // Drift straight down with a slight sideways lean.
                m.Vx = (_rng.NextDouble() - 0.5) * 60;
                m.Vy = 70 + (_rng.NextDouble() * 70);
                break;
            case EntityKind.EnemyBasic:
                m.Radius = 15;
                m.MaxHealth = m.Health = 30;
                m.Vy = 90;
                m.FireTimer = 0.6 + (_rng.NextDouble() * 1.0);
                break;
            case EntityKind.EnemyTough:
                m.Radius = 24;
                m.MaxHealth = m.Health = 90;
                m.Vy = 60;
                m.FireTimer = 0.6 + (_rng.NextDouble() * 1.0);
                break;
        }

        _movers.Add(m);
    }

    private void ResolveCollisions()
    {
        // Player (friendly) shots vs movers
        for (var si = _shots.Count - 1; si >= 0; si--)
        {
            var s = _shots[si];
            if (s.Hostile)
            {
                continue;
            }

            for (var mi = _movers.Count - 1; mi >= 0; mi--)
            {
                var m = _movers[mi];
                if (!m.Alive)
                {
                    continue;
                }

                var rr = s.Radius + m.Radius;
                var dx = s.X - m.X;
                var dy = s.Y - m.Y;
                if ((dx * dx) + (dy * dy) > rr * rr)
                {
                    continue;
                }

                m.Health -= s.Damage;
                if (!s.Piercing)
                {
                    s.Alive = false;
                }

                if (m.Health <= 0)
                {
                    KillMover(m);
                    _movers.RemoveAt(mi);
                }

                if (!s.Piercing)
                {
                    _shots.RemoveAt(si);
                    break;
                }
            }
        }

        // Hostile shots vs player
        if (!IsInvulnerable)
        {
            for (var si = _shots.Count - 1; si >= 0; si--)
            {
                var s = _shots[si];
                if (!s.Hostile)
                {
                    continue;
                }

                var rr = PlayerRadius + s.Radius;
                var dx = PlayerX - s.X;
                var dy = PlayerY - s.Y;
                if ((dx * dx) + (dy * dy) > rr * rr)
                {
                    continue;
                }

                Health -= s.Damage;
                _invulnUntil = _now + 0.6;
                DamageFlash = 1;
                PlayerHit?.Invoke();
                _shots.RemoveAt(si);
                break;
            }
        }

        // Player vs movers
        if (IsInvulnerable)
        {
            return;
        }

        foreach (var m in _movers)
        {
            if (!m.Alive)
            {
                continue;
            }

            var rr = PlayerRadius + m.Radius;
            var dx = PlayerX - m.X;
            var dy = PlayerY - m.Y;
            if ((dx * dx) + (dy * dy) > rr * rr)
            {
                continue;
            }

            double damage = m.Kind switch
            {
                EntityKind.EnemyTough => 26,
                EntityKind.EnemyBasic => 16,
                _ => 18,
            };
            if (BoostActive)
            {
                damage *= 0.4; // boost briefly reduces collision damage
            }

            Health -= damage;
            _invulnUntil = _now + 1.0;
            DamageFlash = 1;
            PlayerHit?.Invoke();

            // Knockback the attacker so it doesn't instantly re-hit.
            m.Health -= 15;
            var push = Math.Atan2(m.Y - PlayerY, m.X - PlayerX);
            m.Vx += Math.Cos(push) * 160;
            m.Vy += Math.Sin(push) * 160;
            break;
        }

        _movers.RemoveAll(m => !m.Alive || m.Health <= 0);
    }

    private void KillMover(Mover m)
    {
        m.Alive = false;
        Score += m.Kind switch
        {
            EntityKind.EnemyTough => 250,
            EntityKind.EnemyBasic => 100,
            _ => 60,
        };
        AddSuperCharge(m.Kind switch
        {
            EntityKind.EnemyTough => 20,
            EntityKind.EnemyBasic => 12,
            _ => 8,
        });
    }

    private static double Approach(double value, double target, double maxDelta)
    {
        if (value < target)
        {
            return Math.Min(value + maxDelta, target);
        }

        return Math.Max(value - maxDelta, target);
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}

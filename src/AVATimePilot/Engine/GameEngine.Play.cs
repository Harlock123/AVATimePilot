namespace AVATimePilot.Engine;

/// <summary>The pilot's plane: always in the middle of the screen, always flying, pointing one of sixteen ways.</summary>
public sealed class PlayerPlane
{
    public Vec Pos;
    public int Heading;
    public double TurnTimer;
    public double FireTimer;
    public double Invulnerable;
    public double Speed = 170;
    public Vec Dir => Vec.FromAngle(Field.HeadingAngle(Heading));
    public Vec Velocity => Dir * Speed;
}

public sealed partial class GameEngine
{
    public const double TurnInterval = 0.06;
    public const double FireInterval = 0.11;
    public const int MaxPlayerShots = 8;
    public const double ShotSpeed = 640;
    public const double EnemyShotSpeed = 245;
    public const double PlayerRadius = 10;
    public const double EnemyRadius = 20;
    public const int FirstExtraLife = 20000, ExtraLifeEvery = 60000;
    public const double DespawnDistance = 1050;

    public PlayerPlane Player { get; } = new();
    public List<Enemy> Enemies { get; } = [];
    public List<Shot> Shots { get; } = [];
    public List<Missile> Missiles { get; } = [];
    public List<Parachute> Parachutes { get; } = [];
    public Boss? Boss { get; private set; }

    /// <summary>The stage counts up forever; every five, the eras come round again, faster and fiercer.</summary>
    public int Stage { get; private set; } = 1;
    public Era Era => Era.Of(Stage);
    public int Loop => (Stage - 1) / Era.All.Length;
    public int Kills { get; private set; }
    public int NextExtraLife { get; private set; } = FirstExtraLife;
    /// <summary>Parachutists caught this era: each is worth more than the last.</summary>
    public int Rescued { get; private set; }
    /// <summary>What brought the plane down last: a shot, a missile, a collision or the great ship.</summary>
    public string? LastDeath { get; private set; }

    private double _spawnTimer, _formationTimer, _parachuteTimer, _bossTimer;
    private int _nextFormation;
    private readonly Dictionary<int, (int Left, int Shot)> _formations = [];

    /// <summary>Test hook: no enemy arrives unless a test puts it there.</summary>
    internal bool Calm { get; set; }

    private double Difficulty => 1 + Loop * 0.15;

    private void StartStage(int stage)
    {
        Stage = stage;
        Kills = 0;
        Rescued = 0;
        Boss = null;
        _bossTimer = 0;
        Enemies.Clear();
        Shots.Clear();
        Missiles.Clear();
        Parachutes.Clear();
        _formations.Clear();
        _formationTimer = 8;
        _parachuteTimer = 12;
        _spawnTimer = 0.5;
        Player.Invulnerable = 0;
        SetPhase(Phase.Ready);
        Say($"{Era.Year}", 2.5);
    }

    internal void AddScore(int points)
    {
        int before = Score;
        Score += points;
        while (Score >= NextExtraLife && before < NextExtraLife)
        {
            Lives++;
            Play(Sfx.ExtraLife);
            Popup(Player.Pos + new Vec(0, -40), "1UP", 1.5);
            NextExtraLife = NextExtraLife == FirstExtraLife ? ExtraLifeEvery : NextExtraLife + ExtraLifeEvery;
        }
    }

    private void Popup(Vec at, string text, double life = 1) =>
        Effects.Add(new Effect { Kind = EffectKind.Score, Pos = at, Life = life, Text = text });

    private void Explode(Vec at, bool big = false) =>
        Effects.Add(new Effect { Kind = big ? EffectKind.BigExplosion : EffectKind.Explosion, Pos = at, Life = big ? 1.4 : 0.6 });

    private double Rand(double lo, double hi) => lo + _rng.NextDouble() * (hi - lo);

    /// <summary>Where an enemy aims, relative to the plane: never quite at it.</summary>
    private Vec Offset() => Vec.FromAngle(Rand(0, Math.PI * 2)) * Rand(70, 170);

    /// <summary>One step of play.</summary>
    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Ready:
                FlyPlayer(dt, c, steer: true, fire: false);
                if (PhaseTime > 2.2)
                {
                    SetPhase(Phase.Playing);
                    Player.Invulnerable = 1.5;
                    Music(true);
                }
                MoveThings(dt);
                return;
            case Phase.Dying:
                MoveThings(dt);
                if (PhaseTime > 2.2)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    // Back in, with the sky around the plane cleared.
                    Shots.Clear();
                    Missiles.Clear();
                    Enemies.RemoveAll(e => (e.Pos - Player.Pos).Length < 450);
                    SetPhase(Phase.Ready);
                    Say("READY", 2);
                }
                return;
            case Phase.Warp:
                // The plane spins through time; the old era falls away.
                if ((int)(PhaseTime / 0.05) != (int)((PhaseTime - dt) / 0.05)) Player.Heading = (Player.Heading + 1) % Field.Headings;
                Player.Pos += Player.Velocity * (dt * (1 + PhaseTime * 3));
                if (PhaseTime > 3.2) StartStage(Stage + 1);
                return;
        }

        FlyPlayer(dt, c, steer: true, fire: true);
        if (Player.Invulnerable > 0) Player.Invulnerable -= dt;
        if (!Calm) Spawn(dt);
        MoveThings(dt);
        Collide();
    }

    /// <summary>Turns the plane a step at a time toward the stick, flies it, and fires.</summary>
    private void FlyPlayer(double dt, Controls c, bool steer, bool fire)
    {
        var p = Player;
        p.TurnTimer -= dt;
        if (steer && (c.Dx != 0 || c.Dy != 0) && p.TurnTimer <= 0)
        {
            int target = TargetHeading(c.Dx, c.Dy);
            int diff = ((target - p.Heading) % Field.Headings + Field.Headings) % Field.Headings;
            if (diff != 0)
            {
                p.Heading = (p.Heading + (diff <= Field.Headings / 2 ? 1 : -1) + Field.Headings) % Field.Headings;
                p.TurnTimer = TurnInterval;
            }
        }
        p.Pos += p.Velocity * dt;
        p.FireTimer -= dt;
        if (fire && c.Fire && p.FireTimer <= 0 && Shots.Count(s => !s.Hostile) < MaxPlayerShots)
        {
            Shots.Add(new Shot { Pos = p.Pos + p.Dir * 22, Vel = p.Dir * ShotSpeed + p.Velocity, Life = 0.8 });
            p.FireTimer = FireInterval;
            Play(Sfx.Shoot);
        }
    }

    /// <summary>The heading (0–15) the stick points at.</summary>
    public static int TargetHeading(int dx, int dy)
    {
        double a = new Vec(dx, dy).Angle;
        return ((int)Math.Round(a / (2 * Math.PI / Field.Headings)) % Field.Headings + Field.Headings) % Field.Headings;
    }

    // ───────────────────────────── arrivals ─────────────────────────────

    private void Spawn(double dt)
    {
        var era = Era;
        int wanted = Math.Min(4 + Era.All.ToList().IndexOf(era) + Loop * 2, 11);
        _spawnTimer -= dt;
        if (_spawnTimer <= 0 && Enemies.Count(e => e.Formation < 0) < wanted)
        {
            _spawnTimer = Rand(0.35, 0.8);
            // Mostly from ahead, where the plane is going.
            double a = Field.HeadingAngle(Player.Heading) + Rand(-1.9, 1.9);
            var pos = Player.Pos + Vec.FromAngle(a) * Rand(620, 700);
            Enemies.Add(new Enemy
            {
                Pos = pos, Craft = era.Craft, Speed = era.EnemySpeed * Difficulty * Rand(0.9, 1.1),
                Heading = (Player.Pos - pos).Angle + Rand(-0.6, 0.6),
                FireCooldown = Rand(1, 2.5), AimOffset = Offset(), Wobble = Rand(0, 6),
            });
        }

        _formationTimer -= dt;
        if (_formationTimer <= 0)
        {
            _formationTimer = Rand(9, 14);
            double a = Rand(0, Math.PI * 2);
            var start = Player.Pos + Vec.FromAngle(a) * 680;
            var through = Player.Pos + Vec.FromAngle(a + Math.PI / 2) * Rand(-140, 140);
            double heading = (through - start).Angle;
            int id = _nextFormation++;
            _formations[id] = (5, 0);
            for (int i = 0; i < 5; i++)
                Enemies.Add(new Enemy
                {
                    Pos = start - Vec.FromAngle(heading) * (i * 50), Craft = era.Craft, Speed = era.EnemySpeed * Difficulty * 1.15,
                    Heading = heading, Formation = id, FireCooldown = Rand(1.5, 3), Wobble = i * 0.4,
                });
        }

        _parachuteTimer -= dt;
        if (_parachuteTimer <= 0)
        {
            _parachuteTimer = Rand(12, 18);
            double a = Field.HeadingAngle(Player.Heading) + Rand(-1.2, 1.2);
            Parachutes.Add(new Parachute { Pos = Player.Pos + Vec.FromAngle(a) * Rand(380, 520) });
        }

        if (Kills >= Era.KillsToBoss && Boss is null)
        {
            _bossTimer -= dt;
            if (_bossTimer <= 0) SpawnBoss();
        }
    }

    private void SpawnBoss()
    {
        var era = Era;
        double a = Field.HeadingAngle(Player.Heading) + Rand(-1.0, 1.0);
        var pos = Player.Pos + Vec.FromAngle(a) * 720;
        // It crosses the pilot's path, not straight at him.
        var past = Player.Pos + Vec.FromAngle(a + Math.PI / 2) * Rand(-180, 180) + Player.Velocity * 2;
        Boss = new Boss
        {
            Pos = pos, Kind = era.Boss, Heading = (past - pos).Angle, Speed = era.BossSpeed * Difficulty,
            MaxHits = era.BossHits + Loop * 4,
        };
        Play(Sfx.BossAlarm);
        Say("WARNING", 2);
    }

    // ───────────────────────────── moving ─────────────────────────────

    private void MoveThings(double dt)
    {
        var era = Era;
        foreach (var e in Enemies)
        {
            var toPlayer = Player.Pos - e.Pos;
            double dist = toPlayer.Length;
            e.Wobble += dt;
            if (e.Formation >= 0)
            {
                // A formation flies straight through, rocking its wings.
                e.Heading += Math.Sin(e.Wobble * 2.2) * 0.25 * dt;
            }
            else
            {
                double want = (Player.Pos + e.AimOffset - e.Pos).Angle;
                double turn = era.TurnRate * Difficulty * dt;
                switch (e.Craft)
                {
                    case Craft.Helicopter:
                        // Helicopters hang back and hover when close.
                        e.Speed = dist < 280 ? Math.Max(60, e.Speed - 200 * dt) : Math.Min(era.EnemySpeed * Difficulty, e.Speed + 120 * dt);
                        break;
                    case Craft.Saucer:
                        // Saucers jink about unpredictably.
                        want += Math.Sin(e.Wobble * 3.1) * 1.2;
                        break;
                }
                // Close in, they make their pass: hold course through, veering off rather than
                // ramming, and only once well clear do they turn back for another run.
                if (dist < 210) e.Passing = true;
                else if (dist > 400 && e.Passing) { e.Passing = false; e.AimOffset = Offset(); }
                if (e.Passing)
                {
                    var rel = e.Pos - Player.Pos;
                    var relVel = e.Velocity - Player.Velocity;
                    double tMin = Math.Max(0, -(rel.X * relVel.X + rel.Y * relVel.Y) / Math.Max(1, relVel.LengthSq));
                    if (tMin < 1.5 && (rel + relVel * tMin).Length < 70)
                    {
                        // Veer away from the plane's side.
                        double away = Field.AngleDiff(e.Heading, rel.Angle) > 0 ? turn : -turn;
                        e.Heading += away * 1.5;
                    }
                }
                else e.Heading += Math.Clamp(Field.AngleDiff(e.Heading, want), -turn, turn);
            }
            e.Pos += e.Velocity * dt;
            if (dist > DespawnDistance) e.Dead = true;

            if (Phase != Phase.Playing) continue;
            e.FireCooldown -= dt;
            double off = Math.Abs(Field.AngleDiff(e.Heading, toPlayer.Angle));
            if (e.FireCooldown <= 0 && dist > 170 && dist < 500 && (off < 0.6 || e.Craft is Craft.Saucer or Craft.Helicopter))
            {
                e.FireCooldown = Rand(1.8, 3.0) / Difficulty;
                if (_rng.NextDouble() < era.FireChance * Difficulty)
                {
                    if (era.Missiles && Missiles.Count < 1 + Loop && _rng.NextDouble() < 0.3)
                    {
                        Missiles.Add(new Missile { Pos = e.Pos, Heading = toPlayer.Angle });
                        Play(Sfx.Missile);
                    }
                    else
                    {
                        // Aimed where the plane will be, give or take.
                        var lead = Player.Pos + Player.Velocity * (dist / EnemyShotSpeed) * 0.6;
                        Shots.Add(new Shot { Pos = e.Pos, Vel = (lead - e.Pos).Unit * EnemyShotSpeed + e.Velocity * 0.3, Life = 2.2, Hostile = true });
                    }
                }
            }
        }
        foreach (var e in Enemies.Where(e => e.Dead && e.Formation >= 0 && _formations.ContainsKey(e.Formation)))
        {
            var f = _formations[e.Formation];
            _formations[e.Formation] = (f.Left - 1, f.Shot);
        }
        Enemies.RemoveAll(e => e.Dead);

        foreach (var s in Shots)
        {
            s.Pos += s.Vel * dt;
            s.Life -= dt;
            if (s.Life <= 0) s.Dead = true;
        }
        Shots.RemoveAll(s => s.Dead);

        foreach (var m in Missiles)
        {
            double want = (Player.Pos - m.Pos).Angle;
            m.Heading += Math.Clamp(Field.AngleDiff(m.Heading, want), -Missile.TurnRate * dt, Missile.TurnRate * dt);
            m.Pos += Vec.FromAngle(m.Heading) * (Missile.Speed * dt);
            m.Life -= dt;
            if (m.Life <= 0)
            {
                m.Dead = true;
                Effects.Add(new Effect { Kind = EffectKind.Smoke, Pos = m.Pos, Life = 0.5 });
            }
        }
        Missiles.RemoveAll(m => m.Dead);

        foreach (var p in Parachutes)
        {
            p.Age += dt;
            p.Pos += new Vec(Math.Sin(p.Age * 1.4) * 22, 26) * dt;
            if (p.Age > 16 || (p.Pos - Player.Pos).Length > DespawnDistance) p.Dead = true;
        }
        Parachutes.RemoveAll(p => p.Dead);

        if (Boss is { } b)
        {
            b.Pos += Vec.FromAngle(b.Heading) * (b.Speed * dt);
            if (b.Flash > 0) b.Flash -= dt;
            if ((b.Pos - Player.Pos).Length > 1150)
            {
                // It got away; it'll be back.
                Boss = null;
                _bossTimer = 3;
            }
            else if (Phase == Phase.Playing)
            {
                b.FireCooldown -= dt;
                if (b.FireCooldown <= 0 && (b.Pos - Player.Pos).Length < 600)
                {
                    b.FireCooldown = Rand(1.2, 2.0) / Difficulty;
                    var aim = (Player.Pos - b.Pos).Angle;
                    int n = b.Kind == BossKind.Mothership ? 5 : 3;
                    for (int i = 0; i < n; i++)
                    {
                        double a = aim + (i - (n - 1) / 2.0) * 0.22;
                        Shots.Add(new Shot { Pos = b.Pos, Vel = Vec.FromAngle(a) * EnemyShotSpeed * 0.85, Life = 2.6, Hostile = true });
                    }
                }
            }
        }
    }

    // ───────────────────────────── hits ─────────────────────────────

    private void Collide()
    {
        var era = Era;
        foreach (var s in Shots.Where(s => !s.Hostile).ToList())
        {
            if (Phase != Phase.Playing) break;
            foreach (var e in Enemies)
            {
                if (e.Dead || (s.Pos - e.Pos).Length > EnemyRadius + 4) continue;
                s.Dead = true;
                Shoot(e);
                break;
            }
            if (s.Dead) continue;
            foreach (var m in Missiles)
            {
                if (m.Dead || (s.Pos - m.Pos).Length > 12) continue;
                s.Dead = true;
                m.Dead = true;
                Explode(m.Pos);
                AddScore(150);
                Play(Sfx.Hit);
                break;
            }
            if (s.Dead || Boss is not { } b) continue;
            if (Field.SegmentDistance(s.Pos, b.Nose, b.Tail) < b.Radius)
            {
                s.Dead = true;
                b.Hits++;
                b.Flash = 0.08;
                Play(Sfx.BossHit);
                Effects.Add(new Effect { Kind = EffectKind.Smoke, Pos = s.Pos, Life = 0.4 });
                if (b.Hits >= b.MaxHits) BossDown(b);
            }
        }
        Shots.RemoveAll(s => s.Dead);
        Missiles.RemoveAll(m => m.Dead);

        foreach (var p in Parachutes.Where(p => (p.Pos - Player.Pos).Length < 34).ToList())
        {
            p.Dead = true;
            Rescued++;
            int points = 1000 * Math.Min(Rescued, 5);
            AddScore(points);
            Popup(p.Pos, points.ToString(), 1.4);
            Play(Sfx.Parachute);
        }
        Parachutes.RemoveAll(p => p.Dead);

        foreach (var (id, f) in _formations.ToList())
            if (f.Left == 0)
            {
                _formations.Remove(id);
                if (f.Shot == 5)
                {
                    int bonus = 1000 * (1 + Loop);
                    AddScore(bonus);
                    Popup(Player.Pos + new Vec(0, -60), $"FORMATION {bonus}", 1.6);
                    Play(Sfx.Formation);
                }
            }

        if (Phase != Phase.Playing || Player.Invulnerable > 0) return;
        string? cause = Shots.Any(s => s.Hostile && (s.Pos - Player.Pos).Length < PlayerRadius + 3) ? "shot"
            : Missiles.Any(m => (m.Pos - Player.Pos).Length < PlayerRadius + 8) ? "missile"
            : Boss is { } boss && Field.SegmentDistance(Player.Pos, boss.Nose, boss.Tail) < boss.Radius + PlayerRadius ? "boss" : null;
        var rammed = Enemies.FirstOrDefault(e => (e.Pos - Player.Pos).Length < 24);
        if (rammed is not null)
        {
            Shoot(rammed);
            cause ??= "collision";
        }
        if (cause is not null)
        {
            LastDeath = cause;
            PlayerDown();
        }
    }

    private void Shoot(Enemy e)
    {
        e.Dead = true;
        Kills++;
        AddScore(Era.Points);
        Explode(e.Pos);
        Play(Sfx.Hit);
        if (e.Formation >= 0 && _formations.TryGetValue(e.Formation, out var f)) _formations[e.Formation] = (f.Left, f.Shot + 1);
        if (Kills == Era.KillsToBoss) _bossTimer = 1.5;
    }

    private void BossDown(Boss b)
    {
        int points = Era.BossPoints * (1 + Loop);
        AddScore(points);
        Explode(b.Pos, big: true);
        Explode(b.Nose, big: true);
        Explode(b.Tail, big: true);
        Popup(b.Pos, points.ToString(), 2);
        Play(Sfx.Explode);
        foreach (var e in Enemies) Explode(e.Pos);
        Enemies.Clear();
        Shots.Clear();
        Missiles.Clear();
        Boss = null;
        Music(false);
        Play(Sfx.Warp);
        SetPhase(Phase.Warp);
        Say("TIME WARP", 3);
    }

    private void PlayerDown()
    {
        Lives--;
        Explode(Player.Pos, big: true);
        Play(Sfx.Die);
        Music(false);
        SetPhase(Phase.Dying);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    /// <summary>Test hook: skip the era's opening.</summary>
    internal void Go()
    {
        if (Phase != Phase.Ready) return;
        SetPhase(Phase.Playing);
        Message = null;
    }

    internal Enemy PutEnemy(Vec offset, double heading, int formation = -1)
    {
        var e = new Enemy { Pos = Player.Pos + offset, Heading = heading, Speed = 0, Craft = Era.Craft, FireCooldown = 99, Formation = formation };
        Enemies.Add(e);
        return e;
    }

    /// <summary>Test hook: five planes of a formation, hanging in a column ahead.</summary>
    internal List<Enemy> PutFormation(Vec offset, double heading)
    {
        int id = _nextFormation++;
        _formations[id] = (5, 0);
        var list = new List<Enemy>();
        for (int i = 0; i < 5; i++) list.Add(PutEnemy(offset + Vec.FromAngle(heading) * (-i * 50), heading, id));
        return list;
    }

    internal void JumpTo(int stage) => StartStage(stage);

    internal void SetKills(int kills) => Kills = kills;

    internal void PutBoss(Vec offset)
    {
        Boss = new Boss { Pos = Player.Pos + offset, Kind = Era.Boss, Heading = Math.PI / 2, Speed = 0, MaxHits = Era.BossHits + Loop * 4, FireCooldown = 99 };
    }

    internal void PutParachute(Vec offset) => Parachutes.Add(new Parachute { Pos = Player.Pos + offset });

    internal void PutMissile(Vec offset) => Missiles.Add(new Missile { Pos = Player.Pos + offset, Heading = (-offset).Angle });

    internal void PutShot(Vec offset, Vec vel) => Shots.Add(new Shot { Pos = Player.Pos + offset, Vel = vel, Life = 3, Hostile = true });

    internal void Fly(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks)
    {
        for (int i = 0; i < ticks; i++) Tick(default);
    }
}

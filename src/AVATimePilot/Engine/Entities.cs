namespace AVATimePilot.Engine;

/// <summary>The stick (eight ways) and the fire button.</summary>
public record struct Controls(int Dx, int Dy, bool Fire = false);

public sealed class Enemy
{
    public Vec Pos;
    public double Heading;
    public double Speed;
    public Craft Craft;
    public double FireCooldown;
    /// <summary>Part of a formation: flies straight through instead of chasing.</summary>
    public int Formation = -1;
    /// <summary>Each enemy aims a little off the player so they don't all pile into one point.</summary>
    public Vec AimOffset;
    public double Wobble;
    /// <summary>Making a pass: holding course through and away before turning back for another.</summary>
    public bool Passing;
    public bool Dead;
    public Vec Velocity => Vec.FromAngle(Heading) * Speed;
}

public sealed class Shot
{
    public Vec Pos;
    public Vec Vel;
    public double Life;
    public bool Hostile;
    public bool Dead;
}

public sealed class Missile
{
    public Vec Pos;
    public double Heading;
    public double Life = 4;
    public bool Dead;
    public const double Speed = 205, TurnRate = 1.2;
}

public sealed class Boss
{
    public Vec Pos;
    public double Heading;
    public double Speed;
    public BossKind Kind;
    public int Hits;
    public int MaxHits;
    public double FireCooldown = 1.5;
    public double Flash;
    /// <summary>Half its length along the heading, and how wide it is: it's hit anywhere along that capsule.</summary>
    public double HalfLength => Kind switch { BossKind.Airship => 110, BossKind.Mothership => 40, _ => 70 };
    public double Radius => Kind switch { BossKind.Airship => 36, BossKind.Mothership => 95, BossKind.Gunship => 34, _ => 40 };
    public Vec Nose => Pos + Vec.FromAngle(Heading) * HalfLength;
    public Vec Tail => Pos - Vec.FromAngle(Heading) * HalfLength;
}

public sealed class Parachute
{
    public Vec Pos;
    public double Age;
    public bool Dead;
}

public enum EffectKind { Explosion, BigExplosion, Score, Smoke }

public sealed class Effect
{
    public EffectKind Kind;
    public Vec Pos;
    public double Age;
    public double Life = 0.6;
    public string Text = "";
    public bool Done => Age >= Life;
}

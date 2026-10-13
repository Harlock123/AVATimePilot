namespace AVATimePilot.Engine;

/// <summary>What the enemy planes of an era are and how they fly and fight.</summary>
public enum Craft { Biplane, Monoplane, Helicopter, Jet, Saucer }

/// <summary>The great enemy craft that ends an era when it's shot down.</summary>
public enum BossKind { Airship, Bomber, Gunship, Stratobomber, Mothership }

/// <summary>
/// One of the five times the pilot is thrown through. Each has its own sky, its own enemy craft and
/// its own great ship to bring down before the next time warp.
/// </summary>
public sealed record Era(int Year, string Name, Craft Craft, BossKind Boss, uint Sky, uint SkyLow, uint Cloud, uint CloudShade,
    double EnemySpeed, double TurnRate, double FireChance, bool Missiles, int Points, int BossHits, double BossSpeed, int BossPoints)
{
    public const int KillsToBoss = 40;

    public static readonly Era[] All =
    [
        new(1917, "THE GREAT WAR", Craft.Biplane, BossKind.Airship, 0xFF6A9AD8, 0xFFA8C8E8, 0xFFF4F4F0, 0xFFC8D0DC,
            150, 1.5, 0.2, false, 100, 10, 55, 3000),
        new(1943, "THE WAR IN THE AIR", Craft.Monoplane, BossKind.Bomber, 0xFF4A70B0, 0xFF88A8D0, 0xFFE8ECF0, 0xFFB0B8C8,
            190, 1.7, 0.26, false, 100, 14, 70, 4000),
        new(1966, "THE ROTOR AGE", Craft.Helicopter, BossKind.Gunship, 0xFF4A8090, 0xFF90B8B0, 0xFFE0E8E0, 0xFFA8B8B0,
            150, 2.6, 0.22, true, 150, 18, 60, 5000),
        new(1988, "THE JET AGE", Craft.Jet, BossKind.Stratobomber, 0xFF5A4A8A, 0xFFD08A70, 0xFFF0D8D0, 0xFFC0A0B0,
            240, 1.4, 0.26, true, 200, 22, 90, 6000),
        new(2061, "THE SAUCER WARS", Craft.Saucer, BossKind.Mothership, 0xFF0C1030, 0xFF2A2060, 0xFF6A70A0, 0xFF3A4070,
            190, 3.0, 0.3, false, 250, 28, 65, 8000),
    ];

    public static Era Of(int stage) => All[(stage - 1) % All.Length];
}

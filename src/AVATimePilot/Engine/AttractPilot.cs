namespace AVATimePilot.Engine;

/// <summary>
/// The computer pilot for the attract mode (and the tests). It dodges what's coming at it, swings
/// round toward the nearest enemy (or the great ship, or a parachutist close by), leads its shots,
/// and keeps the trigger down whenever something is in front of it.
/// </summary>
public sealed class AttractPilot(int seed)
{
    private readonly Random _rng = new(seed);
    private double _dodge;
    private Vec _dodgeDir;

    public Controls Decide(GameEngine g)
    {
        var me = g.Player;
        double dt = Field.Step;

        // Anything hostile about to arrive? Turn across its path.
        if (_dodge > 0) _dodge -= dt;
        else
        {
            // Shots and missiles, and planes on a collision course that aren't lined up to be shot.
            var threats = g.Shots.Where(s => s.Hostile).Select(s => (s.Pos, s.Vel, Reach: 200.0))
                .Concat(g.Missiles.Select(m => (m.Pos, Vel: Vec.FromAngle(m.Heading) * Missile.Speed, Reach: 240.0)))
                .Concat(g.Enemies.Where(e => Math.Abs(Field.AngleDiff(me.Dir.Angle, (e.Pos - me.Pos).Angle)) > 0.3).Select(e => (e.Pos, Vel: e.Velocity, Reach: 130.0)));
            if (g.Boss is { } big && Field.SegmentDistance(me.Pos, big.Nose, big.Tail) < big.Radius + 70)
            {
                var away = (me.Pos - big.Pos).Unit;
                _dodgeDir = away;
                _dodge = 0.4;
            }
            else foreach (var (pos, vel, reach) in threats)
            {
                var rel = pos - me.Pos;
                var relVel = vel - me.Velocity;
                double closing = -(rel.X * relVel.X + rel.Y * relVel.Y) / Math.Max(1, rel.Length);
                // Will it pass close? Look at where it'll be nearest.
                double tMin = Math.Clamp((-(rel.X * relVel.X + rel.Y * relVel.Y)) / Math.Max(1, relVel.LengthSq), 0, 2);
                double miss = (rel + relVel * tMin).Length;
                if (rel.Length < reach && closing > 60 && miss < 40)
                {
                    // Perpendicular to the threat's approach, on the side we're already heading.
                    var perp = new Vec(-relVel.Y, relVel.X).Unit;
                    if (perp.X * me.Dir.X + perp.Y * me.Dir.Y < 0) perp = perp * -1;
                    _dodgeDir = perp;
                    _dodge = 0.35;
                    break;
                }
            }
        }

        Vec? goal = null;
        bool shoot = false;
        if (_dodge > 0) goal = me.Pos + _dodgeDir * 100;
        else
        {
            var chute = g.Parachutes.Where(p => (p.Pos - me.Pos).Length < 300).OrderBy(p => (p.Pos - me.Pos).Length).FirstOrDefault();
            if (g.Boss is { } b && (b.Pos - me.Pos).Length < 700)
            {
                // Circle the great ship at a safe distance, nose pointed in at it.
                var lead = Lead(me, b.Pos, Vec.FromAngle(b.Heading) * b.Speed);
                goal = (lead - me.Pos).Length < b.Radius + 150 ? me.Pos + new Vec(-(lead - me.Pos).Y, (lead - me.Pos).X) : lead;
            }
            else if (chute is not null) goal = chute.Pos;
            else
            {
                var target = g.Enemies.OrderBy(e => (e.Pos - me.Pos).Length).FirstOrDefault();
                if (target is not null) goal = Lead(me, target.Pos, target.Velocity);
            }
        }

        // Fire whenever anything is roughly dead ahead and in range.
        foreach (var e in g.Enemies.Select(e => e.Pos).Concat(g.Missiles.Select(m => m.Pos)).Concat(g.Boss is { } boss ? [boss.Pos] : []))
        {
            var rel = e - me.Pos;
            if (rel.Length < 560 && Math.Abs(Field.AngleDiff(me.Dir.Angle, rel.Angle)) < (g.Boss is { } bb && e == bb.Pos ? 0.6 : 0.35)) { shoot = true; break; }
        }
        if (_rng.NextDouble() < 0.02) shoot = true;

        if (goal is not { } at) return new Controls(0, 0, shoot);
        double want = (at - me.Pos).Angle;
        // The stick has eight ways: pick the one nearest the wanted heading.
        int dir8 = ((int)Math.Round(want / (Math.PI / 4)) % 8 + 8) % 8;
        int[] dx = [0, 1, 1, 1, 0, -1, -1, -1], dy = [-1, -1, 0, 1, 1, 1, 0, -1];
        return new Controls(dx[dir8], dy[dir8], shoot);
    }

    private static Vec Lead(PlayerPlane me, Vec pos, Vec vel)
    {
        double t = (pos - me.Pos).Length / GameEngine.ShotSpeed;
        return pos + (vel - me.Velocity) * t;
    }
}

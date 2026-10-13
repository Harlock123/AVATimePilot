using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVATimePilot.Engine;

namespace AVATimePilot.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVATimePilot/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Cyan = 0xFF4FD7FF, Dim = 0xFFA8B0C8, Orange = 0xFFFF9A2A;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xA0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    /// <summary>The enemy craft's colour in each era, and the pilot's own.</summary>
    private static uint CraftColour(Craft c) => c switch
    {
        Craft.Biplane => 0xFFC84A32, Craft.Monoplane => 0xFF6A8A4A, Craft.Helicopter => 0xFF5E6E42, Craft.Jet => 0xFF8A8CA4, _ => 0xFFA8AAC0,
    };

    private const uint PlayerColour = 0xFFE8EEF8;

    private static string CraftSprite(Craft c) => c.ToString().ToLowerInvariant();

    /// <summary>Paints a drawing centred on a point, turned to a heading (0 = up the screen).</summary>
    private static void Sprite(DrawingContext dc, string name, uint tint, Point centre, double heading, double scale = 3)
    {
        var rows = Sprites.All[name];
        double half = 8 * scale;
        using (dc.PushTransform(Matrix.CreateTranslation(-half, -half) * Matrix.CreateRotation(heading) * Matrix.CreateTranslation(centre.X, centre.Y)))
        {
            for (int r = 0; r < 16; r++)
            {
                string row = rows[r];
                int c = 0;
                while (c < 16)
                {
                    uint col = Sprites.Colour(row[c], tint);
                    int start = c;
                    while (c < 16 && Sprites.Colour(row[c], tint) == col) c++;
                    if (col != 0) dc.FillRectangle(Brush(col), new Rect(start * scale, r * scale, (c - start) * scale + 0.3, scale + 0.3));
                }
            }
        }
    }

    /// <summary>Where a point of the sky is on screen: the plane is always in the middle.</summary>
    private Point Screen(Vec world) => new(world.X - Engine.Player.Pos.X + Field.Centre.X, world.Y - Engine.Player.Pos.Y + Field.Centre.Y);

    private static bool OnScreen(Point p, double margin = 120) =>
        p.X > -margin && p.Y > -margin && p.X < Field.Width + margin && p.Y < Field.Height + margin;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        var era = g.Era;
        DrawSky(dc, era);
        DrawClouds(dc, era, layer: 0);
        DrawClouds(dc, era, layer: 1);

        bool flying = g.State is GameState.Playing or GameState.Paused or GameState.Attract or GameState.GameOver;
        if (flying)
        {
            foreach (var p in g.Parachutes) DrawParachute(dc, Screen(p.Pos), p.Age);
            if (g.Boss is { } b) DrawBoss(dc, b);
            foreach (var e in g.Enemies)
            {
                var at = Screen(e.Pos);
                if (!OnScreen(at)) continue;
                if (e.Craft == Craft.Saucer)
                {
                    Sprite(dc, "saucer", CraftColour(e.Craft), at, 0);
                    for (int i = 0; i < 6; i++)
                    {
                        double a = _clock * 4 + i * Math.PI / 3;
                        dc.DrawEllipse(Brush(i % 2 == 0 ? Yellow : Red), null, new Point(at.X + Math.Cos(a) * 20, at.Y + Math.Sin(a) * 20), 2.5, 2.5);
                    }
                }
                else
                {
                    Sprite(dc, CraftSprite(e.Craft), CraftColour(e.Craft), at, e.Heading);
                    if (e.Craft == Craft.Helicopter) DrawRotor(dc, at, 26);
                }
            }
            foreach (var m in g.Missiles) DrawMissile(dc, Screen(m.Pos), m.Heading);
            foreach (var s in g.Shots)
            {
                var at = Screen(s.Pos);
                if (s.Hostile) dc.DrawEllipse(Brush(g.Era.Craft == Craft.Saucer ? 0xFFFF60F0 : 0xFFFFF0A0), Pen(0xFF803000), at, 4, 4);
                else
                {
                    var tail = Screen(s.Pos - s.Vel.Unit * 14);
                    dc.DrawLine(Pen(Yellow, 4), tail, at);
                }
            }
            bool showPlane = g.Phase != Phase.Dying && !(g.Player.Invulnerable > 0 && (int)(_clock * 12) % 2 == 0);
            if (showPlane) Sprite(dc, "player", PlayerColour, new Point(Field.Centre.X, Field.Centre.Y), Field.HeadingAngle(g.Player.Heading));
        }
        else Sprite(dc, "player", PlayerColour, new Point(Field.Centre.X, Field.Centre.Y + 140), Field.HeadingAngle(g.Player.Heading));

        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawClouds(dc, era, layer: 2);
        if (g.Phase == Phase.Warp && flying) DrawWarp(dc, g.PhaseTime);
        if (flying && g.Boss is { } boss) DrawBossPointer(dc, boss);

        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawHud(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 360), 80, Yellow, center: true);
            Text(dc, "PRESS ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 420), 28, White, center: true);
        }
        if (g.State == GameState.GameOver) Text(dc, "GAME OVER", new Point(512, 330), 96, Red, center: true);
        if (g.State == GameState.Attract)
        {
            Text(dc, "DEMONSTRATION", new Point(512, 640), 34, Yellow, center: true);
            Text(dc, "PRESS ENTER TO PLAY", new Point(512, 672), 26, White, center: true);
        }
    }

    // ───────────────────────────── the sky ─────────────────────────────

    private void DrawSky(DrawingContext dc, Era era)
    {
        for (int i = 0; i < 24; i++)
        {
            double t = i / 23.0;
            dc.FillRectangle(Brush(Mix(era.Sky, era.SkyLow, t)), new Rect(0, i * 32, Field.Width, 33));
        }
        if (era.Craft == Craft.Saucer)
        {
            // Stars, far away, barely moving.
            var p = Engine.Player.Pos * 0.15;
            for (int i = 0; i < 90; i++)
            {
                uint h = Hash(i, 7, 3);
                double x = ((h % 1200) - p.X % 1200 + 2400) % 1200 - 80, y = (((h >> 11) % 900) - p.Y % 900 + 1800) % 900 - 60;
                dc.FillRectangle(Brush(i % 7 == 0 ? 0xFFFFE0A0 : 0xC0FFFFFF), new Rect(x, y, 2, 2));
            }
        }
    }

    /// <summary>
    /// Clouds in three layers: far ones drift slowly and sit beneath the planes, near ones rush past
    /// over everything, and can hide an enemy for a moment.
    /// </summary>
    private void DrawClouds(DrawingContext dc, Era era, int layer)
    {
        double parallax = layer switch { 0 => 0.5, 1 => 1.0, _ => 1.45 };
        double scale = layer switch { 0 => 0.65, 1 => 1.0, _ => 1.5 };
        double tile = layer switch { 0 => 420, 1 => 560, _ => 900 };
        uint body = layer == 0 ? Mix(era.Cloud, era.Sky, 0.45) : era.Cloud, shade = layer == 0 ? Mix(era.CloudShade, era.Sky, 0.45) : era.CloudShade;
        uint alpha = layer == 2 ? 0xB0000000u : 0xFF000000u;
        var cam = Engine.Player.Pos * parallax;
        int tx0 = (int)Math.Floor((cam.X - 700) / tile), tx1 = (int)Math.Floor((cam.X + 700) / tile);
        int ty0 = (int)Math.Floor((cam.Y - 600) / tile), ty1 = (int)Math.Floor((cam.Y + 600) / tile);
        for (int tx = tx0; tx <= tx1; tx++)
        for (int ty = ty0; ty <= ty1; ty++)
        {
            uint h = Hash(tx, ty, layer + 11);
            if (layer == 2 && h % 3 != 0) continue;
            double cx = tx * tile + (h % 1000) / 1000.0 * tile - cam.X + Field.Centre.X;
            double cy = ty * tile + ((h >> 10) % 1000) / 1000.0 * tile - cam.Y + Field.Centre.Y;
            int puffs = 4 + (int)((h >> 20) % 4);
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < puffs; i++)
            {
                uint ph = Hash(tx * 31 + i, ty * 17 + i, layer);
                double ox = ((ph % 200) - 100) * scale, oy = (((ph >> 8) % 70) - 35) * scale, r = (34 + (ph >> 16) % 30) * scale;
                var c = new Point(cx + ox + (pass == 0 ? 6 : 0), cy + oy + (pass == 0 ? 10 : 0));
                dc.DrawEllipse(Brush(((pass == 0 ? shade : body) & 0x00FFFFFF) | alpha), null, c, r * 1.25, r);
            }
        }
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Mix(uint a, uint b, double t)
    {
        uint C(int s) => (uint)Math.Round(((a >> s) & 0xFF) * (1 - t) + ((b >> s) & 0xFF) * t);
        return 0xFF000000 | (C(16) << 16) | (C(8) << 8) | C(0);
    }

    // ───────────────────────────── things in the sky ─────────────────────────────

    private void DrawRotor(DrawingContext dc, Point at, double length)
    {
        double a = _clock * 26;
        for (int i = 0; i < 2; i++)
        {
            var d = new Vec(Math.Cos(a + i * Math.PI / 2), Math.Sin(a + i * Math.PI / 2)) * length;
            dc.DrawLine(Pen(0xC0303030, 3), new Point(at.X - d.X, at.Y - d.Y), new Point(at.X + d.X, at.Y + d.Y));
        }
        dc.DrawEllipse(null, Pen(0x40303030, 1), at, length, length);
    }

    private void DrawMissile(DrawingContext dc, Point at, double heading)
    {
        var d = Vec.FromAngle(heading);
        var tail = new Point(at.X - d.X * 14, at.Y - d.Y * 14);
        dc.DrawLine(Pen(0xFFE8E8E8, 5), tail, at);
        dc.DrawLine(Pen(Red, 5), at, new Point(at.X + d.X * 3, at.Y + d.Y * 3));
        double flick = 6 + 4 * Math.Sin(_clock * 40);
        dc.DrawEllipse(Brush(0xFFFFB030), null, new Point(tail.X - d.X * 4, tail.Y - d.Y * 4), 4, 4);
        dc.DrawLine(Pen(0x80FFFFFF, 3), tail, new Point(tail.X - d.X * flick * 3, tail.Y - d.Y * flick * 3));
    }

    private void DrawParachute(DrawingContext dc, Point at, double age)
    {
        double sway = Math.Sin(age * 1.4) * 0.15;
        using (dc.PushTransform(Matrix.CreateRotation(sway) * Matrix.CreateTranslation(at.X, at.Y)))
        {
            var canopy = new StreamGeometry();
            using (var s = canopy.Open())
            {
                s.BeginFigure(new Point(-24, -10), true);
                s.ArcTo(new Point(24, -10), new Size(24, 20), 0, false, SweepDirection.Clockwise);
                s.EndFigure(true);
            }
            dc.DrawGeometry(Brush(0xFFF0F0F0), Pen(0xFF808080), canopy);
            for (int i = -1; i <= 1; i += 2) dc.FillRectangle(Brush(Red), new Rect(i * 14 - 4, -26, 8, 14));
            foreach (double x in new[] { -24.0, -8, 8, 24 }) dc.DrawLine(Pen(0xFF606060), new Point(x, -10), new Point(0, 12));
            dc.FillRectangle(Brush(0xFF6A5A3A), new Rect(-4, 12, 8, 10));
            dc.DrawEllipse(Brush(0xFFE0B090), null, new Point(0, 10), 4, 4);
        }
        if ((int)(_clock * 3) % 2 == 0) Text(dc, "HELP!", new Point(at.X, at.Y - 44), 20, White, center: true);
    }

    private void DrawBoss(DrawingContext dc, Boss b)
    {
        var at = Screen(b.Pos);
        if (!OnScreen(at, 300)) return;
        uint hit = b.Flash > 0 ? 0xFFFFFFFF : 0;
        uint C(uint col) => hit != 0 ? Mix(col, 0xFFFFFFFF, 0.6) : col;
        using (dc.PushTransform(Matrix.CreateRotation(b.Heading) * Matrix.CreateTranslation(at.X, at.Y)))
        {
            // Every boss is drawn pointing up the screen (nose at negative y).
            switch (b.Kind)
            {
                case BossKind.Airship:
                    dc.DrawEllipse(Brush(C(0xFF7A6A5A)), null, new Point(0, 6), 44, 128);
                    dc.DrawEllipse(Brush(C(0xFFB8A888)), Pen(0xFF5A4A3A, 2), new Point(0, 0), 40, 124);
                    for (int i = -4; i <= 4; i++) dc.DrawLine(Pen(C(0xFF8A7A62), 2), new Point(-38 * Math.Sqrt(1 - Math.Pow(i * 26 / 124.0, 2)), i * 26), new Point(38 * Math.Sqrt(1 - Math.Pow(i * 26 / 124.0, 2)), i * 26));
                    dc.DrawLine(Pen(C(0xFF8A7A62), 2), new Point(0, -120), new Point(0, 120));
                    dc.FillRectangle(Brush(C(0xFF4A3A2A)), new Rect(-12, -20, 24, 50));
                    foreach (int s in new[] { -1, 1 })
                    {
                        dc.FillRectangle(Brush(C(0xFF8A2A2A)), new Rect(s > 0 ? 30 : -62, 96, 32, 26));
                    }
                    dc.FillRectangle(Brush(C(0xFF8A2A2A)), new Rect(-4, 100, 8, 36));
                    break;
                case BossKind.Bomber:
                    dc.FillRectangle(Brush(C(0xFF6A7060)), new Rect(-14, -84, 28, 160));
                    Wing(dc, C(0xFF7A8070), -14, 150, 46);
                    foreach (double x in new[] { -110.0, -60, 60, 110 })
                    {
                        dc.FillRectangle(Brush(C(0xFF3A3A3A)), new Rect(x - 9, -36, 18, 40));
                        DrawProp(dc, new Point(x, -40), 16);
                    }
                    dc.FillRectangle(Brush(C(0xFF7A8070)), new Rect(-56, 56, 112, 22));
                    dc.DrawEllipse(Brush(C(0xFF60C0F0)), null, new Point(0, -70), 9, 10);
                    dc.DrawEllipse(Brush(0xFFF0F0F0), Pen(0xFF20306A, 3), new Point(-90, -6), 12, 12);
                    dc.DrawEllipse(Brush(0xFFF0F0F0), Pen(0xFF20306A, 3), new Point(90, -6), 12, 12);
                    break;
                case BossKind.Gunship:
                    dc.DrawRectangle(Brush(C(0xFF4E5A3A)), Pen(0xFF2A3020, 2), new Rect(-26, -96, 52, 192), 20, 20);
                    dc.FillRectangle(Brush(C(0xFF3A4428)), new Rect(-30, -20, 60, 40));
                    dc.DrawEllipse(Brush(C(0xFF60C0F0)), null, new Point(0, -82), 14, 10);
                    foreach (double y in new[] { -66.0, 66 }) DrawRotor(dc, new Point(0, y), 78);
                    break;
                case BossKind.Stratobomber:
                {
                    var g = new StreamGeometry();
                    using (var s = g.Open())
                    {
                        s.BeginFigure(new Point(0, -100), true);
                        s.LineTo(new Point(130, 50));
                        s.LineTo(new Point(110, 66));
                        s.LineTo(new Point(30, 40));
                        s.LineTo(new Point(26, 90));
                        s.LineTo(new Point(-26, 90));
                        s.LineTo(new Point(-30, 40));
                        s.LineTo(new Point(-110, 66));
                        s.LineTo(new Point(-130, 50));
                        s.EndFigure(true);
                    }
                    dc.DrawGeometry(Brush(C(0xFF6A6C80)), Pen(0xFF30303A, 2), g);
                    dc.FillRectangle(Brush(C(0xFF50526A)), new Rect(-12, -80, 24, 160));
                    foreach (double x in new[] { -70.0, -40, 40, 70 })
                    {
                        dc.FillRectangle(Brush(C(0xFF30303A)), new Rect(x - 7, 10, 14, 34));
                        double f = 6 + 3 * Math.Sin(_clock * 30 + x);
                        dc.DrawEllipse(Brush(0xFFFFA030), null, new Point(x, 48 + f / 2), 5, f);
                    }
                    dc.DrawEllipse(Brush(C(0xFF60C0F0)), null, new Point(0, -74), 6, 10);
                    break;
                }
                case BossKind.Mothership:
                {
                    dc.DrawEllipse(Brush(C(0xFF4A4C68)), null, new Point(0, 8), 112, 112);
                    dc.DrawEllipse(Brush(C(0xFF8A8CA8)), Pen(0xFF30304A, 3), new Point(0, 0), 108, 108);
                    dc.DrawEllipse(Brush(C(0xFF6A6C88)), null, new Point(0, 0), 70, 70);
                    dc.DrawEllipse(Brush(C(0xFF60C0F0)), Pen(0xFF2A6A9A, 3), new Point(0, 0), 36, 36);
                    double glow = 0.5 + 0.5 * Math.Sin(_clock * 5);
                    dc.DrawEllipse(Brush(Mix(0xFF60C0F0, 0xFFFFFFFF, glow)), null, new Point(0, 0), 14, 14);
                    for (int i = 0; i < 16; i++)
                    {
                        double a = -_clock * 2 + i * Math.PI / 8;
                        dc.DrawEllipse(Brush(i % 3 == 0 ? Red : i % 3 == 1 ? Yellow : 0xFF80FF80), null, new Point(Math.Cos(a) * 90, Math.Sin(a) * 90), 5, 5);
                    }
                    break;
                }
            }
        }
        // Damage meter above it.
        double frac = 1 - (double)b.Hits / b.MaxHits;
        var bar = new Rect(at.X - 60, at.Y - b.Radius - b.HalfLength * 0.3 - 40, 120, 8);
        dc.FillRectangle(Brush(0xC0000000), bar.Inflate(2));
        dc.FillRectangle(Brush(frac > 0.5 ? 0xFF60E060 : frac > 0.25 ? Yellow : Red), new Rect(bar.X, bar.Y, bar.Width * frac, bar.Height));
    }

    private static void Wing(DrawingContext dc, uint col, double y, double span, double chord)
    {
        var g = new StreamGeometry();
        using (var s = g.Open())
        {
            s.BeginFigure(new Point(-span, y + chord * 0.3), true);
            s.LineTo(new Point(-20, y));
            s.LineTo(new Point(20, y));
            s.LineTo(new Point(span, y + chord * 0.3));
            s.LineTo(new Point(span, y + chord * 0.6));
            s.LineTo(new Point(-span, y + chord * 0.6));
            s.EndFigure(true);
        }
        dc.DrawGeometry(Brush(col), Pen(0xFF30362A, 2), g);
    }

    private void DrawProp(DrawingContext dc, Point at, double r)
    {
        double a = _clock * 30;
        dc.DrawLine(Pen(0xA0202020, 3), new Point(at.X - Math.Cos(a) * r, at.Y - Math.Sin(a) * r * 0.3), new Point(at.X + Math.Cos(a) * r, at.Y + Math.Sin(a) * r * 0.3));
    }

    /// <summary>When the great ship is off the screen, an arrow at the edge shows where it is.</summary>
    private void DrawBossPointer(DrawingContext dc, Boss b)
    {
        var at = Screen(b.Pos);
        if (OnScreen(at, -40)) return;
        var d = new Vec(at.X - Field.Centre.X, at.Y - Field.Centre.Y);
        double k = Math.Min((Field.Width / 2 - 40) / Math.Max(1e-6, Math.Abs(d.X)), (Field.Height / 2 - 60) / Math.Max(1e-6, Math.Abs(d.Y)));
        var p = new Point(Field.Centre.X + d.X * k, Field.Centre.Y + d.Y * k);
        var u = d.Unit;
        var geo = new StreamGeometry();
        using (var s = geo.Open())
        {
            s.BeginFigure(new Point(p.X + u.X * 18, p.Y + u.Y * 18), true);
            s.LineTo(new Point(p.X - u.Y * 12 - u.X * 6, p.Y + u.X * 12 - u.Y * 6));
            s.LineTo(new Point(p.X + u.Y * 12 - u.X * 6, p.Y - u.X * 12 - u.Y * 6));
            s.EndFigure(true);
        }
        if ((int)(_clock * 4) % 2 == 0) dc.DrawGeometry(Brush(Red), Pen(White, 2), geo);
    }

    private void DrawEffect(DrawingContext dc, Engine.Effect e)
    {
        var at = Screen(e.Pos);
        double t = e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Explosion or EffectKind.BigExplosion:
            {
                double r = (e.Kind == EffectKind.BigExplosion ? 90 : 34) * Math.Sqrt(t);
                uint a = (uint)((1 - t) * 255) << 24;
                dc.DrawEllipse(Brush(a | 0x00FF6020), null, at, r, r);
                dc.DrawEllipse(Brush(a | 0x00FFE060), null, at, r * 0.6, r * 0.6);
                for (int i = 0; i < 8; i++)
                {
                    double ang = i * Math.PI / 4 + e.Pos.X;
                    var q = new Point(at.X + Math.Cos(ang) * r * 1.4, at.Y + Math.Sin(ang) * r * 1.4);
                    dc.FillRectangle(Brush(a | 0x00602010), new Rect(q.X - 3, q.Y - 3, 6, 6));
                }
                break;
            }
            case EffectKind.Smoke:
                dc.DrawEllipse(Brush(((uint)((1 - t) * 160) << 24) | 0x00909090), null, at, 8 + t * 14, 8 + t * 14);
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(at.X, at.Y - t * 30), 30, Yellow, center: true);
                break;
        }
    }

    /// <summary>The time warp: the sky winds itself into a spiral and flashes white.</summary>
    private void DrawWarp(DrawingContext dc, double time)
    {
        double t = Math.Clamp(time / 3.2, 0, 1);
        var c = new Point(Field.Centre.X, Field.Centre.Y);
        for (int ring = 14; ring >= 1; ring--)
        {
            double r = ring * 60 * (1 - t * 0.5) + (time * 300 % 60);
            uint col = ring % 2 == 0 ? 0x60FFFFFFu : 0x6040A0FFu;
            dc.DrawEllipse(null, Pen(col, 14), c, r, r * 0.8);
        }
        for (int i = 0; i < 24; i++)
        {
            double a = i * Math.PI / 12 + time * 4;
            dc.DrawLine(Pen(0x50FFFFFF, 3), new Point(c.X + Math.Cos(a) * 60, c.Y + Math.Sin(a) * 60), new Point(c.X + Math.Cos(a + 0.6) * 700, c.Y + Math.Sin(a + 0.6) * 700));
        }
        if (t > 0.8) dc.FillRectangle(Brush((uint)((t - 0.8) / 0.2 * 255) << 24 | 0x00FFFFFF), new Rect(0, 0, Field.Width, Field.Height));
    }

    // ───────────────────────────── words and numbers ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "1UP", new Point(30, 22), 28, Red);
        Text(dc, g.Score.ToString(), new Point(30, 50), 36, White);
        Text(dc, "HI-SCORE", new Point(512, 22), 28, Red, center: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString(), new Point(512, 50), 36, White, center: true);
        for (int i = 0; i < Math.Min(g.Lives - (g.Phase == Phase.Dying ? 0 : 1), 8); i++)
            Sprite(dc, "player", PlayerColour, new Point(990 - i * 34, 34), 0, 1.8);

        // The era and how far through it the pilot is.
        var era = g.Era;
        dc.FillRectangle(Brush(0x90000000), new Rect(0, Field.Height - 52, Field.Width, 52));
        Text(dc, $"{era.Year}  {era.Name}", new Point(24, Field.Height - 26), 30, Yellow);
        double frac = Math.Min(1, (double)g.Kills / Era.KillsToBoss);
        var bar = new Rect(560, Field.Height - 36, 300, 20);
        dc.DrawRectangle(Pen(White, 2), bar);
        dc.FillRectangle(Brush(frac >= 1 ? Red : Cyan), new Rect(bar.X + 3, bar.Y + 3, (bar.Width - 6) * frac, bar.Height - 6));
        Text(dc, frac >= 1 ? "!" : $"{g.Kills}", new Point(bar.Right + 16, bar.Center.Y), 26, White);
        if (g.Loop > 0) Text(dc, $"ROUND {g.Loop + 1}", new Point(1000, Field.Height - 26), 26, Dim, right: true);

        if (g.Message is { } msg)
        {
            bool big = g.Phase is Phase.Ready or Phase.Warp;
            if (msg != "WARNING" || (int)(_clock * 4) % 2 == 0)
                Text(dc, msg, new Point(512, big ? 250 : 200), big ? 110 : 64, msg == "WARNING" ? Red : Yellow, center: true);
            if (g.Phase == Phase.Ready && msg == $"{era.Year}") Text(dc, era.Name, new Point(512, 320), 40, White, center: true);
        }
    }

    private void DrawTitle(DrawingContext dc)
    {
        dc.FillRectangle(Brush(0x70000010), new Rect(0, 0, Field.Width, Field.Height));
        Text(dc, "AVA TIME PILOT", new Point(512, 90), 104, Yellow, center: true);
        Text(dc, "AFTER THE 1982 ARCADE CLASSIC", new Point(512, 150), 30, Dim, center: true);
        Text(dc, "FIVE ERAS. ONE PILOT. THROUGH TIME.", new Point(512, 196), 30, White, center: true);
        for (int i = 0; i < Era.All.Length; i++)
        {
            var e = Era.All[i];
            double y = 262 + i * 62;
            Sprite(dc, CraftSprite(e.Craft), CraftColour(e.Craft), new Point(270, y), 0, 2.4);
            Text(dc, e.Year.ToString(), new Point(320, y), 36, Cyan);
            Text(dc, e.Name, new Point(420, y), 34, White);
            Text(dc, e.Boss switch
            {
                BossKind.Airship => "AIRSHIP", BossKind.Bomber => "HEAVY BOMBER", BossKind.Gunship => "GUNSHIP", BossKind.Stratobomber => "STRATOBOMBER", _ => "MOTHERSHIP",
            }, new Point(780, y), 26, Orange);
        }
        Text(dc, "SHOOT 40 TO BRING OUT THE GREAT SHIP - DOWN IT TO WARP AHEAD", new Point(512, 580), 26, White, center: true);
        Text(dc, "CATCH PARACHUTISTS FOR BONUS - SHOOT A WHOLE FORMATION FOR MORE", new Point(512, 610), 26, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(512, 660), 40, Yellow, center: true);
        Text(dc, "ARROWS / WASD / STICK: STEER    Z / SPACE / CTRL: FIRE    P PAUSE    M MUTE", new Point(512, 712), 22, Dim, center: true);
        Text(dc, $"HI-SCORE {Engine.HighScores.TopScore}", new Point(512, 742), 24, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0x80000010), new Rect(0, 0, Field.Width, Field.Height));
        Text(dc, "BEST PILOTS", new Point(512, 90), 80, Yellow, center: true);
        Text(dc, "RANK  NAME     SCORE   ERA", new Point(512, 170), 32, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            string era = Era.Of(Math.Max(1, e.Stage)).Year.ToString();
            Text(dc, $"{i + 1,2}.   {e.Initials}   {e.Score,8}   {era}", new Point(512, 220 + i * 42), 36, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(512, 700), 32, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0x90000010), new Rect(0, 0, Field.Width, Field.Height));
        Text(dc, "A NEW HIGH SCORE!", new Point(512, 180), 72, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(512, 260), 56, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(512, 340), 36, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(452 + i * 60, 430), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(430 + i * 60, 470, 44, 4));
            }
        }
        Text(dc, "TYPE LETTERS, OR UP/DOWN TO CHOOSE AND SPACE TO MOVE ON. ENTER WHEN DONE.", new Point(512, 560), 24, White, center: true);
    }
}

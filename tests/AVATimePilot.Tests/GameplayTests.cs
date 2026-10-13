using AVATimePilot.Engine;

namespace AVATimePilot.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "timepilot-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game under way, the era's opening skipped, and no enemy arriving unless a test puts it there.</summary>
    private static GameEngine Game(int stage = 1)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.StartGame();
        if (stage != 1) g.JumpTo(stage);
        g.Go();
        g.Idle(1);
        return g;
    }

    [Theory]
    [InlineData(0, -1, 0)]
    [InlineData(1, -1, 2)]
    [InlineData(1, 0, 4)]
    [InlineData(1, 1, 6)]
    [InlineData(0, 1, 8)]
    [InlineData(-1, 1, 10)]
    [InlineData(-1, 0, 12)]
    [InlineData(-1, -1, 14)]
    public void The_stick_points_the_plane_one_of_sixteen_ways(int dx, int dy, int heading) =>
        Assert.Equal(heading, GameEngine.TargetHeading(dx, dy));

    [Fact]
    public void The_plane_turns_a_step_at_a_time_the_short_way_round()
    {
        var g = Game();
        Assert.Equal(0, g.Player.Heading);
        var seen = new List<int>();
        for (int i = 0; i < 60; i++)
        {
            g.Tick(new Controls(-1, 0));
            if (seen.Count == 0 || seen[^1] != g.Player.Heading) seen.Add(g.Player.Heading);
        }
        Assert.Equal([15, 14, 13, 12], seen);
        // Straight about: it goes round clockwise.
        g.Tick(new Controls(1, 0));
        Assert.Equal(13, g.Player.Heading);
    }

    [Fact]
    public void The_plane_never_stops_flying()
    {
        var g = Game();
        var start = g.Player.Pos;
        g.Idle(60);
        Assert.InRange(start.Y - g.Player.Pos.Y, 165, 175);
        Assert.Equal(start.X, g.Player.Pos.X, 3);
    }

    [Fact]
    public void Shots_fly_ahead_and_bring_down_an_enemy()
    {
        var g = Game();
        var e = g.PutEnemy(new Vec(0, -200), Math.PI);
        g.Fly(1, new Controls(0, 0, true));
        var shot = Assert.Single(g.Shots);
        Assert.True(shot.Vel.Y < -700);
        g.Idle(30);
        Assert.True(e.Dead || !g.Enemies.Contains(e));
        Assert.Equal(1, g.Kills);
        Assert.Equal(100, g.Score);
    }

    [Fact]
    public void Only_so_many_shots_at_once()
    {
        var g = Game();
        g.Fly(60, new Controls(0, 0, true));
        Assert.InRange(g.Shots.Count, 1, GameEngine.MaxPlayerShots);
    }

    [Fact]
    public void Forty_down_brings_out_the_great_ship_and_downing_it_warps_to_the_next_era()
    {
        var g = Game();
        g.SetKills(Era.KillsToBoss - 1);
        g.PutEnemy(new Vec(0, -150), Math.PI);
        g.Fly(20, new Controls(0, 0, true));
        Assert.Equal(Era.KillsToBoss, g.Kills);
        Assert.Null(g.Boss);
        g.Calm = false;
        g.Idle(120);
        Assert.NotNull(g.Boss);
        Assert.Equal(BossKind.Airship, g.Boss!.Kind);
        g.Calm = true;
        g.Enemies.Clear();
        g.PutBoss(new Vec(0, -260));
        int score = g.Score;
        for (int i = 0; i < 600 && g.Phase == Phase.Playing; i++) g.Tick(new Controls(0, 0, true));
        Assert.Equal(Phase.Warp, g.Phase);
        Assert.True(g.Score >= score + Era.All[0].BossPoints);
        g.Idle(60 * 4);
        Assert.Equal(2, g.Stage);
        Assert.Equal(1943, g.Era.Year);
        Assert.Equal(0, g.Kills);
    }

    [Fact]
    public void The_great_ship_takes_many_hits()
    {
        var g = Game(5);
        g.PutBoss(new Vec(0, -300));
        Assert.Equal(BossKind.Mothership, g.Boss!.Kind);
        g.Fly(20, new Controls(0, 0, true));
        Assert.NotNull(g.Boss);
        Assert.InRange(g.Boss!.Hits, 1, g.Boss.MaxHits - 1);
    }

    [Fact]
    public void Each_parachutist_caught_is_worth_more()
    {
        var g = Game();
        for (int i = 1; i <= 3; i++)
        {
            int before = g.Score;
            g.PutParachute(g.Player.Dir * 30);
            g.Idle(2);
            Assert.Equal(before + 1000 * i, g.Score);
        }
        Assert.Equal(3, g.Rescued);
    }

    [Fact]
    public void A_whole_formation_shot_down_earns_a_bonus()
    {
        var g = Game();
        g.PutFormation(new Vec(0, -180), Math.PI);
        for (int i = 0; i < 400 && g.Kills < 5; i++) g.Tick(new Controls(0, 0, true));
        g.Idle(3);
        Assert.Equal(5, g.Kills);
        Assert.Equal(5 * 100 + 1000, g.Score);
    }

    [Fact]
    public void Missiles_home_in()
    {
        var g = Game(3);
        g.PutMissile(new Vec(400, 0));
        var m = g.Missiles[0];
        m.Heading = 0; // pointing up, not at the plane
        g.Idle(60);
        Assert.True(Math.Abs(Field.AngleDiff(m.Heading, (g.Player.Pos - m.Pos).Angle)) < 0.6);
    }

    [Fact]
    public void A_shot_down_plane_loses_a_life_and_comes_back_protected()
    {
        var g = Game();
        g.PutShot(new Vec(0, -40), new Vec(0, 300));
        g.Idle(10);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("shot", g.LastDeath);
        Assert.Equal(GameEngine.StartingLives - 1, g.Lives);
        g.Idle(60 * 5);
        Assert.Equal(Phase.Playing, g.Phase);
        Assert.True(g.Player.Invulnerable > 0);
        g.PutShot(g.Player.Dir * 30, new Vec(0, 0));
        g.Idle(5);
        Assert.Equal(Phase.Playing, g.Phase);
    }

    [Fact]
    public void Running_out_of_lives_ends_the_game_and_a_good_score_goes_in_the_table()
    {
        var folder = TempFolder();
        var g = new GameEngine(3, HighScoreTable.Load(folder)) { Silent = true, Calm = true };
        g.StartGame();
        g.Go();
        g.AddScore(60000);
        for (int life = 0; life < 10 && g.State == GameState.Playing; life++)
        {
            if (g.Phase == Phase.Playing && g.Player.Invulnerable <= 0) g.PutShot(new Vec(0, -20), new Vec(0, 300));
            g.Idle(60 * 5);
        }
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('L');
        g.Initials.Type('W');
        g.Initials.Type('Z');
        g.Initials.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(0, g.NewEntryIndex);
        Assert.Equal("LWZ", HighScoreTable.Load(folder).Entries[0].Initials);
    }

    [Fact]
    public void Extra_planes_at_twenty_thousand_and_every_sixty_thousand()
    {
        var g = Game();
        int lives = g.Lives;
        g.AddScore(19999);
        Assert.Equal(lives, g.Lives);
        g.AddScore(1);
        Assert.Equal(lives + 1, g.Lives);
        g.AddScore(40000);
        Assert.Equal(lives + 2, g.Lives);
        g.AddScore(59999);
        Assert.Equal(lives + 2, g.Lives);
        g.AddScore(1);
        Assert.Equal(lives + 3, g.Lives);
    }

    [Fact]
    public void Enemies_arrive_by_themselves_and_formations_and_parachutists_turn_up()
    {
        var g = Game();
        g.Calm = false;
        bool formation = false, parachute = false;
        for (int i = 0; i < 60 * 25; i++)
        {
            g.Tick(default);
            formation |= g.Enemies.Any(e => e.Formation >= 0);
            parachute |= g.Parachutes.Count > 0;
            if (g.Phase != Phase.Playing) break;
        }
        Assert.NotEmpty(g.Enemies);
        Assert.True(formation);
        Assert.True(parachute);
        Assert.All(g.Enemies, e => Assert.Equal(Craft.Biplane, e.Craft));
    }

    [Fact]
    public void After_five_eras_they_come_round_again_harder()
    {
        var g = Game(6);
        Assert.Equal(1917, g.Era.Year);
        Assert.Equal(1, g.Loop);
        g.PutBoss(new Vec(0, -300));
        Assert.Equal(Era.All[0].BossHits + 4, g.Boss!.MaxHits);
    }

    [Fact]
    public void The_title_turns_into_the_demonstration_then_the_table_then_the_title()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle(60 * 16);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 46);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle(60 * 10);
        Assert.Equal(GameState.Title, g.State);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
    }

    [Fact]
    public void Pausing_stops_the_clock()
    {
        var g = Game();
        g.Pause();
        var pos = g.Player.Pos;
        g.Idle(60);
        Assert.Equal(pos, g.Player.Pos);
        g.Resume();
        g.Idle(1);
        Assert.NotEqual(pos, g.Player.Pos);
    }

    [Fact]
    public void The_computer_pilot_gets_through_the_first_era_more_often_than_not()
    {
        int warps = 0;
        for (int seed = 1; seed <= 4; seed++)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            var pilot = new AttractPilot(seed);
            for (int t = 0; t < 60 * 150 && g.State == GameState.Playing && g.Phase != Phase.Warp; t++) g.Tick(pilot.Decide(g));
            if (g.Phase == Phase.Warp) warps++;
            Assert.True(g.Kills >= 25, $"seed {seed}: {g.Kills} kills");
        }
        Assert.True(warps >= 2, $"{warps} of 4 got through");
    }

    [Fact]
    public void Every_sound_has_a_file()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Sounds");
        foreach (var s in Enum.GetValues<Sfx>().Where(s => s != Sfx.MusicStop))
            Assert.True(File.Exists(Path.Combine(dir, s + ".wav")), s.ToString());
    }

    [Fact]
    public void Every_sprite_is_16_by_16()
    {
        foreach (var (name, rows) in AVATimePilot.Views.Sprites.All)
        {
            Assert.Equal(16, rows.Length);
            Assert.All(rows, r => Assert.True(r.Length == 16, $"{name}: {r}"));
        }
        foreach (var c in Enum.GetValues<Craft>()) Assert.True(AVATimePilot.Views.Sprites.All.ContainsKey(c.ToString().ToLowerInvariant()));
    }
}

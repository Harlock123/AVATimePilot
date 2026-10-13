using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVATimePilot.Engine;
using AVATimePilot.Views;

namespace AVATimePilot.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set TIMEPILOT_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "timepilot-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("TIMEPILOT_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVATimePilot-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Plays an era with the computer pilot until something worth a picture is on screen.</summary>
    private static void PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int stage, int seed, int maxSeconds = 120)
    {
        g.StartGame();
        g.JumpTo(stage);
        var pilot = new AttractPilot(seed);
        for (int t = 0; t < 60 * maxSeconds && !done(g); t++)
        {
            g.Tick(pilot.Decide(g));
            if (g.State != GameState.Playing) { g.StartGame(); g.JumpTo(stage); }
        }
        c.Advance(0.016);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(60);
        c.Advance(0.4);
        Shot(w, c, "title");
        g.Idle(60 * 16 + 60 * 46);
        c.Advance(0.1);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Each_era()
    {
        string[] names = ["1917", "1943", "1966", "1988", "2061"];
        for (int stage = 1; stage <= 5; stage++)
        {
            var (w, c, g) = Open(20 + stage);
            PlayUntil(g, c, x => x.Phase == Phase.Playing && x.PhaseTime > 6 && x.Enemies.Count(e => (e.Pos - x.Player.Pos).Length < 380) >= 3 && x.Shots.Count > 3, stage, stage);
            Shot(w, c, names[stage - 1]);
        }
    }

    [AvaloniaFact]
    public void The_great_ships_and_the_warp()
    {
        var (w, c, g) = Open(31);
        PlayUntil(g, c, x => x.Boss is { } b && Math.Abs(b.Pos.X - x.Player.Pos.X) < 260 && Math.Abs(b.Pos.Y - x.Player.Pos.Y) < 150 && b.Hits > 1, 1, 1, 200);
        Shot(w, c, "bomber");
        (w, c, g) = Open(32);
        PlayUntil(g, c, x => x.Boss is { } b && (b.Pos - x.Player.Pos).Length < 330 && x.Boss.Hits > 2, 5, 3);
        Shot(w, c, "mothership");
        (w, c, g) = Open(33);
        PlayUntil(g, c, x => x.Phase == Phase.Warp && x.PhaseTime > 1.2, 1, 3, 300);
        Shot(w, c, "warp");
    }

    [AvaloniaFact]
    public void A_parachutist()
    {
        var (w, c, g) = Open(41);
        g.Calm = true;
        g.StartGame();
        g.Go();
        g.Idle(30);
        g.PutParachute(g.Player.Dir * 160 + new Vec(60, 0));
        g.Calm = false;
        g.Idle(60);
        c.Advance(0.016);
        Shot(w, c, "parachute");
    }
}

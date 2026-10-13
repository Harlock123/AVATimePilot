namespace AVATimePilot.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>What is happening within an era.</summary>
public enum Phase
{
    /// <summary>The year and the era's name, before the enemy arrive.</summary>
    Ready,
    Playing,
    /// <summary>Shot down.</summary>
    Dying,
    /// <summary>The great ship is down: through the time warp to the next era.</summary>
    Warp,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Shoot, Hit, Explode, Die, Missile, Parachute, BossAlarm, BossHit, Warp, ExtraLife, Formation,
    /// <summary>The engine drone and the flying music, looping.</summary>
    Music, MusicStop,
    TitleTune, StartTune, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it
/// runs in fixed 60 Hz steps, so it plays the same at any frame rate and a seed always plays the
/// same game.
/// </summary>
public sealed partial class GameEngine
{
    public const int StartingLives = 3;
    public const double TitleIdleSeconds = 15;
    public const double AttractSeconds = 45;
    public const double HighScoreSeconds = 9;

    private Random _rng;
    private double _accumulator;
    private bool _startPrev;
    private bool _titleTunePending = true;
    private bool _music;

    public GameEngine(int seed = 1982, HighScoreTable? table = null)
    {
        _rng = new Random(seed);
        HighScores = table ?? HighScoreTable.Load();
        StartStage(1);
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Ready;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public AttractPilot? Pilot { get; private set; }
    public bool Silent { get; set; }
    public int Score { get; private set; }
    public int Lives { get; private set; } = StartingLives;

    public List<Effect> Effects { get; } = [];

    /// <summary>A word across the screen, and for how much longer.</summary>
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, double seconds = 2.5)
    {
        Message = text;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls controls, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(controls, start);
        }
    }

    /// <summary>One 1/60 s step.</summary>
    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        foreach (var e in Effects) e.Age += dt;
        Effects.RemoveAll(e => e.Done);
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }

        switch (State)
        {
            case GameState.Title:
                if (_titleTunePending) { _titleTunePending = false; Play(Sfx.TitleTune); }
                Drift(dt);
                if (startPressed) StartGame();
                else if (StateTime > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot!.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > 3)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Stage);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                Drift(dt);
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    /// <summary>On the title and the tables the plane just cruises among the clouds.</summary>
    private void Drift(double dt) => Player.Pos += Vec.FromAngle(Field.HeadingAngle(Player.Heading)) * (Player.Speed * 0.6 * dt);

    public void StartGame()
    {
        Pilot = null;
        Begin(GameState.Playing, 1);
    }

    private void StartAttract()
    {
        Pilot = new AttractPilot(_rng.Next());
        Begin(GameState.Attract, 1 + _rng.Next(3));
    }

    private void Begin(GameState state, int stage)
    {
        State = state;
        StateTime = 0;
        Score = 0;
        Lives = StartingLives;
        NextExtraLife = FirstExtraLife;
        NewEntryIndex = -1;
        Effects.Clear();
        StartStage(stage);
        Play(Sfx.StartTune);
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        Pilot = null;
        _titleTunePending = true;
        Music(false);
        Effects.Clear();
        Message = null;
        StartStage(1);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
        Pilot = null;
        Music(false);
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Playing) Music(true);
    }

    private void GameOver()
    {
        Music(false);
        if (State == GameState.Attract)
        {
            ShowHighScores(-1);
            return;
        }
        Play(Sfx.GameOver);
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }
}

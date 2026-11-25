using ASToolkit.Engine;

namespace ASToolkit.Bot;

internal sealed class PlaySession
{
    public const int MaxTime = 30 * 60 * 1000;
    public const int StallTime = 240 * 1000;
    private const int GameLost = 3;

    private readonly bool _winAtTourEnd;
    private readonly EnemyStock _enemies;
    private ProgressMark _mark;
    private bool _started;

    public PlaySession(Map map, PlayerBot bot, bool winAtTourEnd)
    {
        Map = map;
        Bot = bot;
        _winAtTourEnd = winAtTourEnd;
        _enemies = new EnemyStock(map);
        Log = new PlayLog(map);
    }

    public Map Map { get; }

    public PlayerBot Bot { get; }

    public bool HasWon { get; private set; }

    public PlayLog Log { get; }

    public bool IsPlaying => Map.ReadScriptNumber("GameState"u8) != GameLost;

    public void Play() => PlayUntil(int.MaxValue);

    public void PlayUntil(int time)
    {
        if (!_started)
        {
            _started = true;
            _mark = ProgressMark.Measure(Map, Bot, _enemies.Count);
            HasWon = ReachedWin();
        }

        while (Map.Time < MaxTime && Map.Time - _mark.ProgressedAt < StallTime && IsPlaying && !HasWon && Map.Time < time)
        {
            Bot.PlayTick();
            Log.Update(Map);
            _enemies.Update();
            HasWon = ReachedWin();
            _mark = _mark.Update(Map, Bot, _enemies.Count);
        }
    }

    private bool ReachedWin() => Map.HasWon || (_winAtTourEnd && Bot.HasFinished);
}

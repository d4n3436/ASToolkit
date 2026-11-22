using ASToolkit.Engine;

namespace ASToolkit.Bot;

internal sealed class PlaySession
{
    public const int MaxTime = 30 * 60 * 1000;
    public const int StallTime = 240 * 1000;

    private readonly bool _winsAtTourEnd;
    private readonly EnemyStock _enemies;
    private ProgressMark _mark;
    private bool _started;

    public PlaySession(Map map, PlayerBot bot, bool winsAtTourEnd)
    {
        Map = map;
        Bot = bot;
        _winsAtTourEnd = winsAtTourEnd;
        _enemies = new EnemyStock(map);
        Log = new PlayLog(map);
    }

    public Map Map { get; }

    private const int PlayerDead = 2;
    private static ReadOnlySpan<byte> GameStateVariable => "GameState"u8;

    public PlayerBot Bot { get; }

    public bool Won { get; private set; }

    public PlayLog Log { get; }

    public void Play() => PlayUntil(int.MaxValue);

    public void PlayUntil(int time)
    {
        if (!_started)
        {
            _started = true;
            _mark = ProgressMark.Measure(Map, Bot, _enemies.Count);
            Won = HasWon();
        }

        while (Map.Time < MaxTime && Map.Time - _mark.ProgressedAt < StallTime && IsPlaying && !Won && Map.Time < time)
        {
            Bot.PlayTick();
            Log.Update(Map);
            _enemies.Update();
            Won = HasWon();
            _mark = _mark.Update(Map, Bot, _enemies.Count);
            Map.AdvanceClock();
        }
    }

    public bool IsPlaying => (Map.HasPlayer ? Map.Player.IsAlive : !Bot.HasStarted) || Map.ReadScriptNumber(GameStateVariable) == PlayerDead;

    private bool HasWon() => Map.HasWon || (_winsAtTourEnd && Bot.HasFinished);

    private readonly record struct ProgressMark(float Farthest, int Sites, int FewestEnemies, int ProgressedAt)
    {
        public static ProgressMark Measure(Map map, PlayerBot bot, int enemies) =>
            new(bot.FarthestProgress, map.DynamiteSites.Count, enemies, map.Time);

        public ProgressMark Update(Map map, PlayerBot bot, int enemies)
        {
            if (bot.FarthestProgress > Farthest || map.DynamiteSites.Count < Sites || enemies < FewestEnemies)
                return new ProgressMark(bot.FarthestProgress, map.DynamiteSites.Count, Math.Min(FewestEnemies, enemies), map.Time);

            return this;
        }
    }
}

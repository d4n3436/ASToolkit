using ASToolkit.Core.Engine;

namespace ASToolkit.Core.Simulation;

internal sealed class PlaySession
{
    public const int MaxTime = 30 * 60 * 1000;
    public const int StallTime = 240 * 1000;

    private readonly bool _winsAtTourEnd;
    private readonly EnemyStock _enemies;
    private ProgressMark _mark;
    private bool _started;

    public PlaySession(GameWorld world, PlayerBot bot, bool winsAtTourEnd)
    {
        World = world;
        Bot = bot;
        _winsAtTourEnd = winsAtTourEnd;
        _enemies = new EnemyStock(world);
        Log = new PlayLog(world);
    }

    public GameWorld World { get; }

    private const int PlayerDead = 2;
    private static ReadOnlySpan<byte> GameStateVariable => "GameState"u8;

    public PlayerBot Bot { get; }

    public bool Won { get; private set; }

    public PlayLog Log { get; }

    public void Play(ReplayRecorder? recorder) => PlayUntil(int.MaxValue, recorder);

    public void PlayUntil(int time, ReplayRecorder? recorder = null)
    {
        if (!_started)
        {
            _started = true;
            _mark = ProgressMark.Measure(World, Bot, _enemies.Count);
            Won = HasWon();
        }

        while (World.Time < MaxTime && World.Time - _mark.ProgressedAt < StallTime && IsPlaying && !Won && World.Time < time)
        {
            Bot.PlayTick();
            Log.Update(World);
            _enemies.Update();
            Won = HasWon();
            _mark = _mark.Update(World, Bot, _enemies.Count);
            recorder?.Capture(World);
            World.AdvanceClock();
        }
    }

    public bool IsPlaying => World.Player.IsAlive || World.ReadScriptNumber(GameStateVariable) == PlayerDead;

    private bool HasWon() => World.HasWon || (_winsAtTourEnd && Bot.HasFinished);

    private readonly record struct ProgressMark(float Farthest, int Sites, int FewestEnemies, int ProgressedAt)
    {
        public static ProgressMark Measure(GameWorld world, PlayerBot bot, int enemies) =>
            new(bot.FarthestProgress, world.DynamiteSites.Count, enemies, world.Time);

        public ProgressMark Update(GameWorld world, PlayerBot bot, int enemies)
        {
            if (bot.FarthestProgress > Farthest || world.DynamiteSites.Count < Sites || enemies < FewestEnemies)
                return new ProgressMark(bot.FarthestProgress, world.DynamiteSites.Count, Math.Min(FewestEnemies, enemies), world.Time);

            return this;
        }
    }
}

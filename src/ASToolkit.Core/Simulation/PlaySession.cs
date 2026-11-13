using ASToolkit.Core.Engine;

namespace ASToolkit.Core.Simulation;

internal sealed class PlaySession
{
    public const int MaxTime = 30 * 60 * 1000;
    public const int StallTime = 240 * 1000;

    private readonly bool _endsWithTour;
    private readonly EnemyStock _enemies;
    private ProgressMark _mark;
    private bool _started;

    public PlaySession(GameWorld world, PlayerBot bot, bool endsWithTour)
    {
        World = world;
        Bot = bot;
        _endsWithTour = endsWithTour;
        MinHealth = world.Player.Hp;
        _enemies = new EnemyStock(world);
        world.PlayerHurt += OnPlayerHurt;
    }

    public GameWorld World { get; }

    public PlayerBot Bot { get; }

    public bool Won { get; private set; }

    public int MinHealth { get; private set; }

    public void Play(ReplayRecorder? recorder) => PlayUntil(int.MaxValue, recorder);

    public void PlayUntil(int time, ReplayRecorder? recorder = null)
    {
        if (!_started)
        {
            _started = true;
            _mark = ProgressMark.Measure(World, Bot, _enemies.Count);
            Won = HasWon();
        }

        while (World.Time < MaxTime && World.Time - _mark.ProgressedAt < StallTime && World.Player.IsAlive && !Won && World.Time < time)
        {
            Bot.PlayTick();
            Won = HasWon();
            _mark = _mark.Update(World, Bot, _enemies.Count);
            recorder?.Capture(World);
            World.AdvanceClock();
        }
    }

    private void OnPlayerHurt(int health) => MinHealth = Math.Min(MinHealth, health);

    private bool HasWon() => World.HasWon || (_endsWithTour && Bot.HasFinished);

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

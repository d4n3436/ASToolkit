using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;
using System.Drawing;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Provides a tick simulation of a player bot playing a level with the game's combat rules.
/// </summary>
public sealed class Playthrough
{
    private const int MaxAttempts = 3;

    private readonly ShopBot _shop;
    private readonly ObjectCatalog _kinds;
    private readonly IReadOnlyList<int> _ammoBatches;
    private readonly Dictionary<RouteAnalysis, LevelSetup> _setups = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="Playthrough"/> class.
    /// </summary>
    /// <param name="gameFolder">The game folder, holding the sprites whose speed sets the animation times.</param>
    /// <param name="objects">The object table of the game.</param>
    /// <param name="table">The common table of the game, holding the ammunition batch of each weapon.</param>
    public Playthrough(string gameFolder, ObjectTable objects, GameTable table)
    {
        _kinds = new ObjectCatalog(gameFolder, objects);
        _shop = new ShopBot(objects, _kinds, table);
        _ammoBatches = table.GetArray("GettingAmmo");
    }

    /// <summary>
    /// Gets the times in milliseconds the game clock advances by on successive frames, repeated in order.
    /// </summary>
    public IReadOnlyList<int> FrameTimes { get; init; } = GameClock.DisplayFrameTimes;

    /// <summary>
    /// Plays a level along its tour and returns the outcome.
    /// </summary>
    /// <param name="analysis">The route analysis of the level.</param>
    /// <param name="loadout">What the player carries into the level.</param>
    /// <param name="seed">The seed of the random rolls.</param>
    /// <param name="records">A value indicating whether the result keeps the frames and shots for a replay.</param>
    public PlaythroughResult Run(RouteAnalysis analysis, PlayerLoadout loadout, int seed, bool records = false)
    {
        var recorder = records ? new ReplayRecorder() : null;
        var session = Start(analysis, loadout, seed, recorder);
        session.Play(recorder);
        recorder?.Finish(session.World);

        return CreateResult(session, loadout, recorder);
    }

    internal PlaySession Start(RouteAnalysis analysis, PlayerLoadout loadout, int seed, ReplayRecorder? recorder)
    {
        if (!_setups.TryGetValue(analysis, out var setup))
        {
            setup = CreateSetup(analysis);
            _setups.Add(analysis, setup);
        }

        var world = new GameWorld(setup.Level, loadout, new GameRandom(seed), recorder);
        AddLoosePickups(analysis, world);
        var bot = new PlayerBot(world, analysis, setup.Field.Clone(world.Movement));

        return new PlaySession(world, bot, analysis.Exit is null && !analysis.ClearsMonsters);
    }

    private static void AddLoosePickups(RouteAnalysis analysis, GameWorld world)
    {
        foreach (var element in analysis.Elements)
        {
            if (element.Placement is null && element.Kind == ElementKind.Pickup)
            {
                world.AddPickup(new Vector3(element.X, element.Y, 0), element.Id, null);
            }
        }
    }

    private static PlaythroughResult CreateResult(PlaySession session, PlayerLoadout loadout, ReplayRecorder? recorder)
    {
        var world = session.World;

        return new(loadout, world.Player.IsAlive && session.Won, world.Time, session.Bot.FarthestProgress, world.Kills, world.Respawns, session.MinHealth,
            world.Inventory.Money, world.Inventory.Implants, world.TakenPickups, world.Inventory.Items, world.Inventory.GetRemainingAmmo(),
            world.ScriptTimeline, recorder?.CreateReplay());
    }

    private LevelSetup CreateSetup(RouteAnalysis analysis)
    {
        var script = CreateLevelScript(analysis);
        var movement = _kinds.CreateMovementMap(analysis.Model);
        var field = new WalkField(analysis.Grid, script.DoorsLocked, analysis.Grid.FenceEdges.Count > 0, movement, _kinds.Find(KnownObject.PlayerStart)!.Object);
        var start = analysis.Tour.Count > 0 ? analysis.Grid.Lattice.GetCenter(analysis.Tour[0]).ToVector2() : Vector2.Zero;
        int cell = field.FindFree(start);
        var level = new LevelTemplate
        {
            Model = analysis.Model,
            Script = script,
            Kinds = _kinds,
            Movement = movement,
            Bounds = WorldBounds.Measure(analysis.Grid.Cells, analysis.Grid.Lattice),
            AmmoBatches = _ammoBatches,
            FrameTimes = FrameTimes,
            FallbackStart = cell >= 0 ? field.GetCenter(cell) : start
        };

        return new LevelSetup(level, field);
    }

    private static LevelScript CreateLevelScript(RouteAnalysis analysis)
    {
        var blackout = analysis.Objectives.FirstOrDefault(objective => objective.Kind == ObjectiveKind.Blackout);
        bool locked = analysis.DoorsInitiallyLocked ?? (blackout is null && analysis.Objectives.Any(objective => objective.Kind == ObjectiveKind.Breaker) &&
            analysis.Grid.Edges.Values.Any(kind => kind == EdgeKind.Door));
        string digits = new(analysis.Name.Where(char.IsAsciiDigit).ToArray());
        TriggerArea? blackoutArea = blackout is null ? null : new TriggerArea(new Point((int)blackout.X, (int)blackout.Y), (int)blackout.TriggerRadius);

        return new LevelScript
        {
            LevelNumber = int.TryParse(digits, out int number) ? number : 0,
            DoorsLocked = locked,
            ClearsMonsters = analysis.ClearsMonsters,
            Exit = analysis.Exit,
            Blackout = blackoutArea,
            Checkpoints = analysis.RespawnTriggers.Where(trigger => trigger.Center != blackoutArea?.Center).ToArray()
        };
    }

    /// <summary>
    /// Plays campaign levels in order, retaining equipment and shopping with the previous level's earnings.
    /// </summary>
    /// <param name="levels">The levels in campaign order.</param>
    /// <param name="endMoney">The reward for each supplied level, in the same order.</param>
    /// <param name="seed">The seed of the random rolls.</param>
    /// <param name="state">The state before the first shop, or null to begin a new campaign without shopping.</param>
    /// <param name="records">Whether to record the levels for replay.</param>
    /// <param name="recordedLevel">The zero-based index of the level to record, or null to record every level.</param>
    public CampaignResult PlayCampaign(IReadOnlyList<RouteAnalysis> levels, IReadOnlyList<int> endMoney, int seed,
        CampaignState? state = null, bool records = false, int? recordedLevel = null)
    {
        var results = new List<PlaythroughResult>();
        bool shopsFirst = state is not null;
        state ??= CampaignState.CreateInitial();
        for (int level = 0; level < levels.Count; level++)
        {
            int money = state.Money;
            int reward = level < endMoney.Count ? endMoney[level] : 0;
            int firstSeed = seed + level * MaxAttempts;
            var loadout = level > 0 || shopsFirst
                ? _shop.Shop(ref money, state.Loadout.Ammo, state.Loadout.Implants, state.Loadout.Items, reward)
                : state.Loadout;
            bool record = records && (recordedLevel is null || recordedLevel == level);
            var result = Run(levels[level], loadout, firstSeed, record);
            for (int attempt = 1; attempt < MaxAttempts && !result.Survived; attempt++)
            {
                result = Run(levels[level], loadout, firstSeed + attempt, record);
            }

            results.Add(result);

            if (!result.Survived)
            {
                break;
            }

            var items = result.Items;

            if (items.ContainsKey(KnownObject.Dynamite))
            {
                var carriedItems = items.ToDictionary();
                carriedItems.Remove(KnownObject.Dynamite);
                items = carriedItems;
            }

            state = new CampaignState(money + result.Money + reward,
                new PlayerLoadout(0, result.Ammo, result.Implants) { Items = items });
        }

        return new CampaignResult(
            Levels: results,
            State: state,
            IsCompleted: results.Count == levels.Count && results.All(result => result.Survived));
    }

    private sealed record LevelSetup(LevelTemplate Level, WalkField Field);
}

using System.Globalization;
using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Provides a tick simulation of a player bot playing a level with the game's combat rules.
/// </summary>
public sealed class Playthrough
{
    private const int MaxAttempts = 3;

    private readonly ShopBot _shop;
    private const string ConfigFile = "AlienShooter.cfg";
    private const int RegistryItemCount = 12;
    private const int LifeImplantItem = 0;
    private const int StrengthImplantItem = 1;
    private const int SpeedImplantItem = 2;
    private const int AccuracyImplantItem = 3;
    private const int LivesItem = 7;
    private const int FlashlightItem = 8;
    private static ReadOnlySpan<byte> ItemsVariable => "PlayerItem"u8;
    private const int RescueKitItem = 10;
    private static ReadOnlySpan<byte> MoneyVariable => "PlayerMoney"u8;

    private const string DefaultLevelScript = """
        #define MUSIC0 "music\\mus01.ogg"
        #define MUSIC1 "music\\mus02.ogg"
        #include "maps\export.lgc"
        #include "maps\common.lgc"
        #include "maps\common_compaign.lgc"
        main()
        {
          iff(1)
            InitGame();
          if( GameTact() )
            return;
          CompaignGameTact();
        }
        """;
    private const int DroneItem = 11;
    private readonly string _gameFolder;
    private readonly ScriptFolder _scripts;
    private readonly SpriteCatalog _definitions;
    private readonly ObjectTable _objects;
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
        _gameFolder = gameFolder;
        _scripts = new ScriptFolder(gameFolder);
        _definitions = new SpriteCatalog(gameFolder, objects);
        _objects = objects;
        _shop = new ShopBot(objects, _definitions, table);
        _ammoBatches = table.GetArray("GettingAmmo");
    }

    /// <summary>
    /// Gets the times in milliseconds the game clock advances by on successive frames, repeated in order.
    /// </summary>
    public IReadOnlyList<int> FrameTimes { get; init; } = GameClock.DisplayFrameTimes;

    /// <summary>
    /// Plays a level along its tour.
    /// </summary>
    /// <param name="analysis">The route analysis of the level.</param>
    /// <param name="loadout">The weapons, ammunition, armor and implants the player carries into the level.</param>
    /// <param name="seed">The seed of the random rolls.</param>
    /// <param name="record">A value indicating whether to keep a replay of the run.</param>
    /// <param name="session">The map state earlier levels of the game session left, or null for a new session.</param>
    /// <returns>The outcome of the playthrough.</returns>
    public PlaythroughResult Run(RouteAnalysis analysis, PlayerLoadout loadout, int seed, bool record = false, GameSession? session = null)
    {
        var recorder = record ? new ReplayRecorder() : null;
        var play = Start(analysis, loadout, seed, recorder, session);
        play.Play(recorder);

        return CreateResult(play, loadout, recorder);
    }

    /// <summary>
    /// Replays a demo of the level in the engine, from its seed and the input of every frame.
    /// </summary>
    /// <param name="analysis">The route analysis of the level the demo plays.</param>
    /// <param name="demo">The demo.</param>
    /// <param name="loadingScreenRecords">The stored sprites before which the game's loading screen redrew while the level loaded.</param>
    /// <returns>The player after the level loads and after each frame.</returns>
    public IReadOnlyList<DemoReplayPoint> ReplayDemo(RouteAnalysis analysis, DemoResource demo, IReadOnlyList<int>? loadingScreenRecords = null)
    {
        var world = LoadDemo(analysis, demo, loadingScreenRecords);
        var points = new List<DemoReplayPoint> { GetReplayPoint(world) };
        uint now = 0;
        while (true)
        {
            world.StepDemo(now, out now);

            if (!world.IsPlayingDemo)
                break;

            world.Step();
            points.Add(GetReplayPoint(world));
            world.AdvanceClock();
        }

        return points;
    }

    internal GameWorld LoadDemo(RouteAnalysis analysis, DemoResource demo, IReadOnlyList<int>? loadingScreenRecords = null)
    {
        var level = GetSetup(analysis).Level;
        var scripted = new LevelTemplate
        {
            Model = level.Model, Definitions = level.Definitions, Movement = level.Movement, Bounds = level.Bounds, AmmoBatches = level.AmmoBatches,
            FrameTimes = level.FrameTimes, FallbackStart = level.FallbackStart, ScriptFiles = new ScriptFolder(_gameFolder),
            ScriptPath = Path.ChangeExtension(GameTextEncoding.Cyrillic.GetString(demo.MapName), ".lgc"),
            LoadingScreenRecords = loadingScreenRecords ?? []
        };
        var world = new GameWorld(scripted, new GameRandom(demo.Seed), demo: demo);
        AddLoosePickups(analysis, world);

        return world;
    }

    private static DemoReplayPoint GetReplayPoint(GameWorld world) =>
        new(world.Time, world.Player.Position, world.Player.Hp, world.Player.Direction, world.Player.Speed, world.RandomState);

    private LevelSetup GetSetup(RouteAnalysis analysis)
    {
        if (!_setups.TryGetValue(analysis, out var setup))
        {
            setup = CreateSetup(analysis);
            _setups.Add(analysis, setup);
        }

        return setup;
    }

    internal PlaySession Start(RouteAnalysis analysis, PlayerLoadout loadout, int seed, ReplayRecorder? recorder, GameSession? session = null)
    {
        var setup = GetSetup(analysis);
        var world = new GameWorld(setup.Level, new GameRandom(seed), registry: CreateRegistry(_gameFolder, analysis.Name, loadout), session: session);
        recorder?.Start(world);
        AddLoosePickups(analysis, world);
        var bot = new PlayerBot(world, analysis, setup.Field.Clone(setup.Field.Obstacles.Clone(world.Movement)));

        return new PlaySession(world, bot, analysis.Exit is null && !analysis.ClearsMonsters);
    }

    private static void AddLoosePickups(RouteAnalysis analysis, GameWorld world)
    {
        foreach (var element in analysis.Elements)
        {
            if (element.Placement is null && element.Kind == ElementKind.Pickup)
            {
                var definition = world.Definitions.Find(element.Id);

                if (definition is not null)
                {
                    world.CreateSprite(definition, new Vector3(element.X, element.Y, 0), default);
                }
            }
        }
    }

    private static PlaythroughResult CreateResult(PlaySession session, PlayerLoadout loadout, ReplayRecorder? recorder)
    {
        var world = session.World;

        return new PlaythroughResult(loadout, world.Player.IsAlive && session.Won, world.Time, session.Bot.FarthestProgress, session.Log.Kills, session.Log.Respawns, session.Log.MinHealth,
            world.ReadScriptNumber(MoneyVariable), ReadImplants(world), session.Log.TakenPickups, ReadItems(world), new PlayerState(world).GetRemainingAmmo(),
            session.Log.Timeline, recorder?.CreateReplay(session.Log.PickupEvents));
    }

    private static PlayerImplants ReadImplants(GameWorld world) => new(
        world.ReadScriptNumber(ItemsVariable), world.ReadScriptNumber(ItemsVariable, StrengthImplantItem),
        world.ReadScriptNumber(ItemsVariable, SpeedImplantItem), world.ReadScriptNumber(ItemsVariable, AccuracyImplantItem));

    private static Dictionary<KnownObject, int> ReadItems(GameWorld world)
    {
        var items = new Dictionary<KnownObject, int>();
        int lives = world.ReadScriptNumber(ItemsVariable, LivesItem);
        bool hasDrone = world.Monsters.Any(unit => unit.Definition.Id == KnownObject.FlyingDrone && unit.IsAlive);

        if (lives > 0)
        {
            items[KnownObject.ExtraLife] = lives;
        }

        if (world.ReadScriptNumber(ItemsVariable, FlashlightItem) != 0)
        {
            items[KnownObject.Flashlight] = 1;
        }

        if (world.Player.Items.Contains((int)KnownObject.Medkit))
        {
            items[KnownObject.Medkit] = 1;
        }

        if (hasDrone)
        {
            items[KnownObject.Drone] = 1;
        }

        return items;
    }

    private LevelSetup CreateSetup(RouteAnalysis analysis)
    {
        var movement = _definitions.CreateMovementMap(analysis.Model);
        var obstacles = new WalkObstacles(analysis.Model, _definitions.Parts, _objects, movement);
        bool locked = analysis.DoorsInitiallyLocked ?? (analysis.Objectives.All(objective => objective.Kind != ObjectiveKind.Blackout) &&
            analysis.Objectives.Any(objective => objective.Kind == ObjectiveKind.Breaker) && analysis.Grid.Edges.Values.Any(kind => kind == EdgeKind.Door));
        var field = new WalkField(analysis.Grid, locked, analysis.Grid.FenceEdges.Count > 0, obstacles, _definitions.Find(KnownObject.PlayerStart)!.Object);
        var start = analysis.Tour.Count > 0 ? analysis.Grid.Lattice.GetCenter(analysis.Tour[0]).ToVector2() : Vector2.Zero;
        int cell = field.FindFree(start);
        string scriptPath = GetScriptPath(analysis.Name);
        var level = new LevelTemplate
        {
            Model = analysis.Model,
            Definitions = _definitions,
            Movement = movement,
            Bounds = WorldBounds.Measure(analysis.Grid.Cells, analysis.Grid.Lattice),
            AmmoBatches = _ammoBatches,
            FrameTimes = FrameTimes,
            FallbackStart = cell >= 0 ? field.GetCenter(cell) : start,
            ScriptFiles = _scripts.TryRead(scriptPath, out _) ? _scripts : new ScriptOverlay(_scripts, scriptPath, DefaultLevelScript),
            ScriptPath = scriptPath
        };

        return new LevelSetup(level, field);
    }

    /// <summary>
    /// Plays campaign levels in order, shopping with each level's earnings before the next.
    /// </summary>
    /// <param name="levels">The levels in campaign order.</param>
    /// <param name="endMoney">The reward for each supplied level, in the same order.</param>
    /// <param name="seed">The seed of the random rolls.</param>
    /// <param name="state">The state before the first shop, or null to begin a new campaign without shopping.</param>
    /// <param name="record">A value indicating whether to record the levels for replay.</param>
    /// <param name="recordedLevel">The zero-based index of the level to record, or null to record every level.</param>
    /// <returns>The result of each level played and the state after the last one completed.</returns>
    public CampaignResult PlayCampaign(IReadOnlyList<RouteAnalysis> levels, IReadOnlyList<int> endMoney, int seed,
        CampaignState? state = null, bool record = false, int? recordedLevel = null)
    {
        var results = new List<PlaythroughResult>();
        var session = new GameSession();
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
            bool recordLevel = record && (recordedLevel is null || recordedLevel == level);
            var result = Run(levels[level], loadout, firstSeed, recordLevel, session);
            for (int attempt = 1; attempt < MaxAttempts && !result.Survived; attempt++)
            {
                result = Run(levels[level], loadout, firstSeed + attempt, recordLevel, session);
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

    private static string GetScriptPath(string mapName) => Path.ChangeExtension("maps\\" + mapName.Replace('/', '\\'), ".lgc");

    /// <summary>
    /// Creates the registry a level's script reads the player's state from at the start of a level.
    /// </summary>
    /// <param name="gameFolder">The game folder holding the game's configuration.</param>
    /// <param name="mapName">The map's name relative to the maps folder, setting the game type and level number.</param>
    /// <param name="loadout">The equipment the player carries into the level.</param>
    /// <returns>The registry.</returns>
    public static ScriptRegistry CreateRegistry(string gameFolder, string mapName, PlayerLoadout loadout)
    {
        var registry = new ScriptRegistry(ScriptRegistry.ReadDefaultPath(Path.Combine(gameFolder, ConfigFile)));
        string folder = Path.GetDirectoryName(mapName) ?? "";
        registry.Set("GameType", folder.Equals("ADDON2", StringComparison.OrdinalIgnoreCase) ? "3" : folder.Equals("ADDON", StringComparison.OrdinalIgnoreCase) ? "2" : "0");
        registry.Set("LevelNumber", new string(Path.GetFileNameWithoutExtension(mapName).Where(char.IsAsciiDigit).ToArray()).TrimStart('0'));
        registry.Set("PlayerSex", "male");
        registry.Set("StateBar", "1");
        registry.Set("DifficultyLevel", "1");
        for (int slot = 0; slot < Unit.WeaponSlots; slot++)
        {
            var weapon = (KnownObject)(Unit.WeaponItemBase + slot);
            bool owned = slot == Unit.Pistols || loadout.Ammo.ContainsKey(weapon);
            registry.Set("PlayerWeapon" + slot, owned ? "1" : "0");
            registry.Set("PlayerAmmo" + slot, loadout.Ammo.GetValueOrDefault(weapon).ToString(CultureInfo.InvariantCulture));
        }

        int[] items = new int[RegistryItemCount];
        items[LifeImplantItem] = loadout.Implants.Life;
        items[StrengthImplantItem] = loadout.Implants.Strength;
        items[SpeedImplantItem] = loadout.Implants.Speed;
        items[AccuracyImplantItem] = loadout.Implants.Accuracy;

        if (loadout.Armor != 0)
        {
            items[loadout.Armor - KnownObject.WornGreenArmor] = 1;
        }

        items[LivesItem] = loadout.Items.GetValueOrDefault(KnownObject.ExtraLife);
        items[FlashlightItem] = loadout.Items.GetValueOrDefault(KnownObject.Flashlight);
        items[RescueKitItem] = loadout.Items.GetValueOrDefault(KnownObject.Medkit);
        items[DroneItem] = loadout.Items.GetValueOrDefault(KnownObject.Drone);
        for (int index = 0; index < items.Length; index++)
        {
            registry.Set("PlayerItem" + index, items[index].ToString(CultureInfo.InvariantCulture));
        }

        return registry;
    }

    private sealed record LevelSetup(LevelTemplate Level, WalkField Field);
}
